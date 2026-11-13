using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace QSmart.Core.Planilhas;

/// <summary>
/// Leitura de uma aba .xlsx em blocos (sem descompactar o XML inteiro na memória), montando a <see cref="Folha"/>
/// numa passada só: o cabeçalho é a 1ª linha (como o pandas), as colunas fora do filtro nem são convertidas,
/// as linhas vazias do meio ficam e as do fim saem.
/// </summary>
internal sealed class LeitorFolhaXlsx
{
    const int Bloco = 2 << 20;                 // 2 MB por bloco de XML
    const int MaxPool = 1 << 18;               // textos repetidos (usuário, procedimento…) viram uma única string

    readonly Celula[] _shared;
    readonly List<LeitorXlsx.Formato> _estilos;
    readonly bool _d1904;
    readonly OpcoesLeitura _op;
    readonly Folha _f;
    readonly Dictionary<string, string> _pool = new(StringComparer.Ordinal);
    readonly CacheCelulas _cache = new();

    // cabeçalho e colunas
    readonly List<Celula> _cab = new();        // células da linha 1 (todas as colunas)
    bool _cabFechado;
    readonly List<string> _nomes = new();      // nome (como o pandas) de cada coluna da planilha
    readonly List<int> _pos = new();           // coluna da planilha -> índice em _f.Colunas (-1 = fora do filtro)
    readonly Dictionary<string, int> _vistos = new(StringComparer.Ordinal);

    // linhas
    int _linhaAtual = -1, _proxima;            // índice 0 = linha 1 do Excel
    Celula[]? _row;                            // linha de dados em montagem
    bool _rowEhCab;
    int _ultimaComValor = -1;                  // última linha (índice) com algum valor, em qualquer coluna
    int _maxCol = -1;                          // última coluna com valor
    bool _parar;

    LeitorFolhaXlsx(string nome, Celula[] shared, List<LeitorXlsx.Formato> estilos, bool d1904, OpcoesLeitura op)
    {
        _shared = shared; _estilos = estilos; _d1904 = d1904; _op = op;
        _f = new Folha { Nome = nome };
    }

    public static async Task<Folha> Ler(Stream s, string nome, Celula[] shared, List<LeitorXlsx.Formato> estilos, bool d1904,
                                        OpcoesLeitura op, Progresso prog)
    {
        var l = new LeitorFolhaXlsx(nome, shared, estilos, d1904, op);
        var buf = new byte[Bloco];
        int len = 0;
        bool dentro = false, fim = false;
        while (!fim && !l._parar)
        {
            // completa o bloco
            while (len < buf.Length)
            {
                int n = await s.ReadAsync(buf.AsMemory(len, buf.Length - len));
                if (n == 0) { fim = true; break; }
                len += n;
            }
            int ini = 0;
            if (!dentro)
            {
                int sd = buf.AsSpan(0, len).IndexOf("<sheetData"u8);
                if (sd < 0)
                {
                    sd = buf.AsSpan(0, len).IndexOf(":sheetData"u8);
                    if (sd >= 0) sd = buf.AsSpan(0, sd).LastIndexOf((byte)'<');
                }
                if (sd < 0)
                {
                    if (fim) break;
                    // guarda só o fim (a tag pode estar cortada) e lê mais
                    int manter = Math.Min(len, 64);
                    Buffer.BlockCopy(buf, len - manter, buf, 0, manter);
                    len = manter;
                    continue;
                }
                dentro = true;
                ini = sd;
            }
            // processa até o último </row> completo (ou tudo, no fim do arquivo)
            int corte;
            if (fim) corte = len;
            else
            {
                int k = buf.AsSpan(ini, len - ini).LastIndexOf("</"u8);
                corte = -1;
                while (k >= 0)
                {
                    int abs = ini + k;
                    int gt = buf.AsSpan(abs, len - abs).IndexOf((byte)'>');
                    if (gt >= 0 && NomeLocal(buf.AsSpan(abs + 2, gt - 2)).SequenceEqual("row"u8)) { corte = abs + gt + 1; break; }
                    k = buf.AsSpan(ini, k).LastIndexOf("</"u8);
                }
                if (corte < 0)
                {
                    // nenhuma linha completa no bloco: aumenta o bloco
                    Array.Resize(ref buf, buf.Length * 2);
                    continue;
                }
            }
            bool acabou = l.Processar(buf.AsSpan(ini, corte - ini));
            if (acabou) break;
            prog.Msg = $"linha {QSmart.Core.Texto.Fmt(l._linhaAtual + 1)}";
            await prog.Talvez();
            Buffer.BlockCopy(buf, corte, buf, 0, len - corte);
            len -= corte;
        }
        l.FecharLinha();
        return l.Finalizar();
    }

    static ReadOnlySpan<byte> NomeLocal(ReadOnlySpan<byte> n)
    {
        int dp = n.IndexOf((byte)':');
        return dp >= 0 ? n[(dp + 1)..] : n;
    }

    static bool EhBranco(byte c) => c == ' ' || c == '\t' || c == '\r' || c == '\n';

    /// <summary>Valor do atributo (sem prefixo de namespace); vazio + false se não existir.</summary>
    static bool Atributo(ReadOnlySpan<byte> area, ReadOnlySpan<byte> nome, out ReadOnlySpan<byte> valor)
    {
        valor = default;
        int k = 0;
        while (k < area.Length)
        {
            while (k < area.Length && EhBranco(area[k])) k++;
            int a = k;
            while (k < area.Length && area[k] != '=' && !EhBranco(area[k]) && area[k] != '>' && area[k] != '/') k++;
            if (k >= area.Length || area[k] != '=') { k++; continue; }
            var an = area[a..k];
            k++;
            if (k >= area.Length) break;
            var q = area[k];
            if (q != '"' && q != '\'') continue;
            int vi = k + 1;
            int vf = area[vi..].IndexOf(q);
            if (vf < 0) break;
            vf += vi;
            if (an.SequenceEqual(nome)) { valor = area[vi..vf]; return true; }
            k = vf + 1;
        }
        return false;
    }

    /// <summary>r, t e s de uma &lt;c&gt; numa passada só.</summary>
    static void AtributosCelula(ReadOnlySpan<byte> area, out ReadOnlySpan<byte> r, out ReadOnlySpan<byte> t, out ReadOnlySpan<byte> s)
    {
        r = t = s = default;
        int k = 0;
        while (k < area.Length)
        {
            while (k < area.Length && EhBranco(area[k])) k++;
            int a = k;
            while (k < area.Length && area[k] != '=' && !EhBranco(area[k]) && area[k] != '>' && area[k] != '/') k++;
            if (k >= area.Length || area[k] != '=') { k++; continue; }
            int nLen = k - a;
            byte n0 = area[a];
            k++;
            if (k >= area.Length) break;
            var q = area[k];
            if (q != '"' && q != '\'') continue;
            int vi = k + 1;
            int vf = area[vi..].IndexOf(q);
            if (vf < 0) break;
            vf += vi;
            if (nLen == 1)
            {
                if (n0 == 'r') r = area[vi..vf];
                else if (n0 == 't') t = area[vi..vf];
                else if (n0 == 's') s = area[vi..vf];
            }
            k = vf + 1;
        }
    }

    static int ColunaDeRef(ReadOnlySpan<byte> r)
    {
        int col = 0;
        foreach (var ch in r)
        {
            if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
            else if (ch >= 'a' && ch <= 'z') col = col * 26 + (ch - 'a' + 1);
            else break;
        }
        return col - 1;
    }

    static int Inteiro(ReadOnlySpan<byte> s, int padrao) => Utf8Parser.TryParse(s, out int v, out int n) && n == s.Length ? v : padrao;

    /// <summary>Processa um trecho de XML com linhas completas. Devolve true quando não precisa ler mais.</summary>
    bool Processar(ReadOnlySpan<byte> b)
    {
        int i = 0, n = b.Length;
        while (i < n)
        {
            int lt = b[i..].IndexOf((byte)'<');
            if (lt < 0) break;
            i += lt + 1;
            if (i >= n) break;
            bool fecha = b[i] == '/';
            int ini = fecha ? i + 1 : i;
            int fimNome = ini;
            while (fimNome < n && b[fimNome] != '>' && b[fimNome] != '/' && !EhBranco(b[fimNome])) fimNome++;
            var nome = NomeLocal(b[ini..fimNome]);
            if (fecha)
            {
                if (nome.SequenceEqual("row"u8)) { FecharLinha(); if (_parar) return true; }
                else if (nome.SequenceEqual("sheetData"u8)) return true;
                i = fimNome;
                continue;
            }
            int gt = b[fimNome..].IndexOf((byte)'>');
            if (gt < 0) break;
            gt += fimNome;
            bool vazio = b[gt - 1] == '/';
            var area = b[fimNome..gt];
            if (nome.SequenceEqual("row"u8))
            {
                FecharLinha();
                if (_parar) return true;
                _linhaAtual = Atributo(area, "r"u8, out var r) ? Inteiro(r, _proxima + 1) - 1 : _proxima;
                _proxima = _linhaAtual + 1;
                AbrirLinha();
                if (vazio) { FecharLinha(); if (_parar) return true; }
                i = gt + 1;
                continue;
            }
            if (nome.SequenceEqual("c"u8))
            {
                if (_row == null && !_rowEhCab)
                {
                    // célula fora de <row>: linha implícita
                    _linhaAtual = _proxima++;
                    AbrirLinha();
                }
                AtributosCelula(area, out var refc, out var t, out var sAttr);
                int col = refc.Length > 0 ? ColunaDeRef(refc) : _ultimaColLinha + 1;
                _ultimaColLinha = col;
                int estilo = sAttr.Length > 0 ? Inteiro(sAttr, 0) : 0;
                ReadOnlySpan<byte> v = default;
                bool temV = false;
                int partesTxt = 0, txtIni = 0, txtFim = 0;
                StringBuilder? txtVarios = null;
                i = gt + 1;
                if (!vazio)
                {
                    while (i < n)
                    {
                        int lt2 = b[i..].IndexOf((byte)'<');
                        if (lt2 < 0) { i = n; break; }
                        int p = i + lt2 + 1;
                        bool fecha2 = p < n && b[p] == '/';
                        int ini2 = fecha2 ? p + 1 : p;
                        int fim2 = ini2;
                        while (fim2 < n && b[fim2] != '>' && b[fim2] != '/' && !EhBranco(b[fim2])) fim2++;
                        var nome2 = NomeLocal(b[ini2..fim2]);
                        int gt2 = b[fim2..].IndexOf((byte)'>');
                        if (gt2 < 0) { i = n; break; }
                        gt2 += fim2;
                        if (fecha2 && nome2.SequenceEqual("c"u8)) { i = gt2 + 1; break; }
                        bool ehV = nome2.SequenceEqual("v"u8), ehT = !ehV && nome2.SequenceEqual("t"u8);
                        if (!fecha2 && (ehV || ehT) && b[gt2 - 1] != '/')
                        {
                            int fimTxt = b[(gt2 + 1)..].IndexOf((byte)'<');
                            fimTxt = fimTxt < 0 ? n : fimTxt + gt2 + 1;
                            if (ehV) { v = b[(gt2 + 1)..fimTxt]; temV = true; }
                            else
                            {
                                if (partesTxt == 0) { txtIni = gt2 + 1; txtFim = fimTxt; }
                                else
                                {
                                    txtVarios ??= new StringBuilder(LeitorXlsx.Decodificar(b[txtIni..txtFim]));
                                    txtVarios.Append(LeitorXlsx.Decodificar(b[(gt2 + 1)..fimTxt]));
                                }
                                partesTxt++;
                            }
                            i = fimTxt;
                            continue;
                        }
                        i = gt2 + 1;
                    }
                }
                if (col < 0) continue;
                bool temConteudo = (temV && v.Length > 0) || (partesTxt > 0 && (txtVarios != null || txtFim > txtIni));
                int destino = Destino(col, temConteudo);
                if (destino == -2) continue;                     // coluna fora do filtro
                var fmt = estilo < _estilos.Count ? _estilos[estilo] : LeitorXlsx.Formato.Geral;
                Celula cel;
                if (partesTxt == 1 && txtVarios == null && (t.Length == 0 || t.SequenceEqual("inlineStr"u8)) && !temV)
                    cel = _cache.Obter(b[txtIni..txtFim], 1, this, static (l, x, _) => LeitorXlsx.CelTexto(LeitorXlsx.Decodificar(x)));
                else if (temV && partesTxt == 0 && t.Length == 0)
                    cel = _cache.Obter(v, (byte)(2 + (int)fmt), this, static (l, x, tp) => l.Converter(default, x, true, null, (LeitorXlsx.Formato)(tp - 2)));
                else if (temV && partesTxt == 0 && t.SequenceEqual("str"u8))
                    cel = _cache.Obter(v, 1, this, static (l, x, _) => LeitorXlsx.CelTexto(LeitorXlsx.Decodificar(x)));
                else
                {
                    string? inline = partesTxt == 0 ? null : txtVarios != null ? txtVarios.ToString() : Texto(b[txtIni..txtFim]);
                    cel = Converter(t, temV ? v : default, temV, inline, fmt);
                }
                if (!cel.Tem) continue;
                Guardar(col, destino, cel);
                continue;
            }
            i = gt + 1;
        }
        return _parar;
    }

    int _ultimaColLinha = -1;

    void AbrirLinha()
    {
        _ultimaColLinha = -1;
        _rowEhCab = !_op.SemCabecalho && _linhaAtual == 0;
        if (!_rowEhCab) FecharCabecalho();
        if (_op.Limite.HasValue && !_rowEhCab && LinhaDados(_linhaAtual) >= _op.Limite.Value) { _parar = true; _row = null; return; }
        _row = _rowEhCab ? null : new Celula[_f.Colunas.Count];
    }

    int LinhaDados(int idx) => _op.SemCabecalho ? idx : idx - 1;

    void FecharLinha()
    {
        if (_row == null) { _rowEhCab = false; return; }
        int d = LinhaDados(_linhaAtual);
        while (_f.Linhas.Count < d) _f.Linhas.Add(Array.Empty<Celula>());     // linhas que não existem no XML (vazias)
        if (d == _f.Linhas.Count) _f.Linhas.Add(_row);
        else if (d >= 0 && d < _f.Linhas.Count) _f.Linhas[d] = _row;           // linha repetida: fica a última
        _row = null;
    }

    /// <summary>Onde guardar a célula: -1 = cabeçalho, -2 = descartar, senão a coluna em _f.Colunas.</summary>
    int Destino(int col, bool temConteudo)
    {
        if (_rowEhCab) return -1;
        if (_row == null) return -2;
        GarantirColunas(col);
        int p = _pos[col];
        if (p < 0 && temConteudo) Marcar(col);       // coluna fora do filtro: conta para achar a última linha
        return p < 0 ? -2 : p;
    }

    void Marcar(int col)
    {
        if (_linhaAtual > _ultimaComValor) _ultimaComValor = _linhaAtual;
        if (col > _maxCol) _maxCol = col;
    }

    void Guardar(int col, int destino, Celula cel)
    {
        if (destino == -1)
        {
            while (_cab.Count <= col) _cab.Add(Celula.Vazia);
            _cab[col] = cel;
            Marcar(col);
            return;
        }
        Marcar(col);
        if (destino >= _row!.Length) Array.Resize(ref _row, _f.Colunas.Count);
        _row[destino] = cel;
    }

    void FecharCabecalho()
    {
        if (_cabFechado) return;
        _cabFechado = true;
        GarantirColunas(_cab.Count - 1);
    }

    /// <summary>Gera, em ordem, o nome de cada coluna até <paramref name="ate"/> (mesma regra de nomes repetidos do pandas).</summary>
    void GarantirColunas(int ate)
    {
        while (_nomes.Count <= ate)
        {
            int c = _nomes.Count;
            string nomeCol;
            if (_op.SemCabecalho) nomeCol = c.ToString(CultureInfo.InvariantCulture);
            else
            {
                nomeCol = c < _cab.Count && _cab[c].Tem ? _cab[c].Texto! : $"Unnamed: {c}";
                if (_vistos.TryGetValue(nomeCol, out var k))
                {
                    var novo = $"{nomeCol}.{k}";
                    while (_vistos.ContainsKey(novo)) { k++; novo = $"{nomeCol}.{k}"; }
                    _vistos[nomeCol] = k + 1;
                    _vistos[novo] = 1;
                    nomeCol = novo;
                }
                else _vistos[nomeCol] = 1;
            }
            _nomes.Add(nomeCol);
            bool manter = _op.SemCabecalho || _op.Colunas == null || _op.Colunas(nomeCol);
            if (manter) { _pos.Add(_f.Colunas.Count); _f.Colunas.Add(nomeCol); }
            else _pos.Add(-1);
        }
    }

    Folha Finalizar()
    {
        FecharCabecalho();
        GarantirColunas(_maxCol);
        // sem nenhuma célula: aba vazia
        if (_ultimaComValor < 0) { _f.Colunas = new(); _f.Linhas = new(); return _f; }
        int nDados = Math.Max(0, LinhaDados(_ultimaComValor) + 1);
        if (_f.Linhas.Count > nDados) _f.Linhas.RemoveRange(nDados, _f.Linhas.Count - nDados);
        while (_f.Linhas.Count < nDados) _f.Linhas.Add(Array.Empty<Celula>());
        if (_op.Limite.HasValue && _f.Linhas.Count > _op.Limite.Value) _f.Linhas.RemoveRange(_op.Limite.Value, _f.Linhas.Count - _op.Limite.Value);
        int w = _f.Colunas.Count;
        for (int r = 0; r < _f.Linhas.Count; r++)
        {
            var row = _f.Linhas[r];
            if (row.Length == w) continue;
            var novo = new Celula[w];
            Array.Copy(row, novo, Math.Min(row.Length, w));
            _f.Linhas[r] = novo;
        }
        return _f;
    }

    // ---------------- conversão ----------------
    internal string Texto(ReadOnlySpan<byte> s)
    {
        var t = LeitorXlsx.Decodificar(s);
        return Intern(t);
    }

    string Intern(string t)
    {
        if (t.Length > 120) return t;
        if (_pool.TryGetValue(t, out var x)) return x;
        if (_pool.Count < MaxPool) _pool[t] = t;
        return t;
    }

    internal Celula Converter(ReadOnlySpan<byte> t, ReadOnlySpan<byte> v, bool temV, string? inline, LeitorXlsx.Formato fmt)
    {
        if (t.Length > 0)
        {
            if (t.SequenceEqual("s"u8))
            {
                if (temV && Utf8Parser.TryParse(v, out int idx, out _) && idx >= 0 && idx < _shared.Length) return _shared[idx];
                return Celula.Vazia;
            }
            if (t.SequenceEqual("inlineStr"u8)) return LeitorXlsx.CelTexto(inline ?? (temV ? Texto(v) : null));
            if (t.SequenceEqual("str"u8) || t.SequenceEqual("e"u8)) return LeitorXlsx.CelTexto(temV ? Texto(v) : null);
            if (t.SequenceEqual("b"u8))
            {
                if (!temV) return Celula.Vazia;
                bool um = v.SequenceEqual("1"u8);
                return new Celula(TipoCelula.Bool, um || v.SequenceEqual("true"u8) ? "True" : "False", um ? 1 : 0);
            }
            if (t.SequenceEqual("d"u8))
            {
                var sv = temV ? Encoding.UTF8.GetString(v) : null;
                if (sv != null && DateTime.TryParse(sv, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd)) return Celula.DeData(dd);
                return LeitorXlsx.CelTexto(sv);
            }
        }
        if (!temV) return inline != null ? LeitorXlsx.CelTexto(inline) : Celula.Vazia;
        if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return LeitorXlsx.CelTexto(Texto(v));
        if (fmt == LeitorXlsx.Formato.Data || (fmt == LeitorXlsx.Formato.Hora && num >= 1))
        {
            var d = LeitorXlsx.DeSerial(num, _d1904);
            if (d != null) return Celula.DeData(d.Value);
        }
        if (fmt == LeitorXlsx.Formato.Hora)
        {
            var d = LeitorXlsx.DeSerial(num, _d1904);
            if (d != null) return new Celula(TipoCelula.Hora, d.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture), num, d.Value);
        }
        bool inteiro = v.IndexOfAny("."u8) < 0 && v.IndexOfAny("Ee"u8) < 0;
        if (inteiro && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var li))
            return new Celula(TipoCelula.Inteiro, Intern(li.ToString(CultureInfo.InvariantCulture)), li);
        return new Celula(TipoCelula.Numero, Intern(QSmart.Core.Texto.PyFloat(num)), num);
    }
}

internal delegate Celula CriarCelula(LeitorFolhaXlsx dono, ReadOnlySpan<byte> chave, byte tipo);

/// <summary>Cache de células pelo conteúdo em bytes (tipo + texto do XML): valores repetidos não são decodificados de novo.</summary>
internal sealed class CacheCelulas
{
    const int MaxItens = 1 << 18, MaxBytes = 96;
    int[] _baldes = new int[1 << 12];
    Item[] _itens = new Item[1 << 12];
    int _n;
    struct Item { public int Hash, Prox; public byte Tipo; public byte[] Chave; public Celula Valor; }

    public Celula Obter(ReadOnlySpan<byte> chave, byte tipo, LeitorFolhaXlsx dono, CriarCelula criar)
    {
        if (chave.Length > MaxBytes) return criar(dono, chave, tipo);
        var hc = new HashCode();
        hc.Add(tipo);
        hc.AddBytes(chave);
        int h = hc.ToHashCode();
        int balde = h & (_baldes.Length - 1);
        for (int i = _baldes[balde] - 1; i >= 0; i = _itens[i].Prox)
        {
            ref var it = ref _itens[i];
            if (it.Hash == h && it.Tipo == tipo && chave.SequenceEqual(it.Chave)) return it.Valor;
        }
        var v = criar(dono, chave, tipo);
        if (_n >= MaxItens) return v;
        if (_n == _itens.Length) Crescer();
        balde = h & (_baldes.Length - 1);
        _itens[_n] = new Item { Hash = h, Prox = _baldes[balde] - 1, Tipo = tipo, Chave = chave.ToArray(), Valor = v };
        _baldes[balde] = ++_n;
        return v;
    }

    void Crescer()
    {
        Array.Resize(ref _itens, _itens.Length * 2);
        _baldes = new int[_itens.Length];
        for (int i = 0; i < _n; i++)
        {
            int b = _itens[i].Hash & (_baldes.Length - 1);
            _itens[i].Prox = _baldes[b] - 1;
            _baldes[b] = i + 1;
        }
    }
}
