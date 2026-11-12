using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace QSmart.Core.Planilhas;

/// <summary>Progresso de tarefas longas: mensagem + ponto de "respiro" para a tela atualizar (o navegador tem uma só thread).</summary>
public sealed class Progresso
{
    public string Msg { get; set; } = "";
    public Func<Task>? Ceder { get; init; }
    DateTime _ultimo = DateTime.UtcNow;

    public async ValueTask Talvez()
    {
        if (Ceder == null) return;
        var agora = DateTime.UtcNow;
        if ((agora - _ultimo).TotalMilliseconds < 120) return;
        _ultimo = agora;
        await Ceder();
    }

    public static readonly Progresso Nenhum = new();
}

/// <summary>Opções de leitura (equivalentes a usecols / nrows / dtype do pandas).</summary>
public sealed class OpcoesLeitura
{
    /// <summary>Filtra colunas pelo nome original do cabeçalho (como o usecols chamável do pandas).</summary>
    public Func<string, bool>? Colunas { get; init; }
    /// <summary>Lê no máximo N linhas de dados por aba.</summary>
    public int? Limite { get; init; }
    /// <summary>Só a primeira aba (sheet_name=0).</summary>
    public bool SoPrimeira { get; init; }
    /// <summary>Sem cabeçalho (header=None): colunas 0..n.</summary>
    public bool SemCabecalho { get; init; }
}

/// <summary>Leitor de .xlsx/.xlsm escrito do zero (zip + XML), rápido o bastante para o navegador.</summary>
public static class LeitorXlsx
{
    /// <summary>Valores que o pandas trata como vazio (na_values padrão).</summary>
    public static readonly HashSet<string> ValoresNa = new(StringComparer.Ordinal)
    {
        "", "#N/A", "#N/A N/A", "#NA", "-1.#IND", "-1.#QNAN", "-NaN", "-nan", "1.#IND", "1.#QNAN",
        "<NA>", "N/A", "NA", "NULL", "NaN", "None", "n/a", "nan", "null",
    };

    public static async Task<List<Folha>> Ler(byte[] dados, OpcoesLeitura? op = null, Progresso? prog = null)
    {
        op ??= new OpcoesLeitura();
        prog ??= Progresso.Nenhum;
        using var ms = new MemoryStream(dados, false);
        ZipArchive zip;
        try { zip = new ZipArchive(ms, ZipArchiveMode.Read); }
        catch (InvalidDataException)
        {
            if (dados.Length > 8 && dados[0] == 0xD0 && dados[1] == 0xCF)
                throw new InvalidDataException("Arquivo .xls antigo (Excel 97-2003) ou protegido: abra no Excel e salve como .xlsx.");
            throw new InvalidDataException("O arquivo não é uma planilha .xlsx válida.");
        }
        using (zip)
        {
            var entradas = zip.Entries.ToDictionary(e => e.FullName.TrimStart('/'), e => e, StringComparer.OrdinalIgnoreCase);
            ZipArchiveEntry? Achar(string caminho)
            {
                caminho = caminho.TrimStart('/');
                return entradas.TryGetValue(caminho, out var e) ? e : null;
            }

            var wbEntry = Achar("xl/workbook.xml") ?? throw new InvalidDataException("Planilha sem xl/workbook.xml.");
            var (abas, data1904) = LerWorkbook(wbEntry);
            var rels = LerRels(Achar("xl/_rels/workbook.xml.rels"));
            var compartilhadas = LerShared(Achar("xl/sharedStrings.xml"));
            var estilos = LerEstilos(Achar("xl/styles.xml"));

            var folhas = new List<Folha>();
            foreach (var (nome, rid) in abas)
            {
                if (!rels.TryGetValue(rid, out var alvo)) continue;
                var caminho = alvo.StartsWith("/") ? alvo.TrimStart('/') : "xl/" + alvo;
                caminho = NormalizarCaminho(caminho);
                var e = Achar(caminho);
                if (e == null) continue;
                prog.Msg = nome;
                byte[] bytes;
                using (var s = e.Open())
                using (var buf = new MemoryStream())
                {
                    await s.CopyToAsync(buf);
                    bytes = buf.ToArray();
                }
                await prog.Talvez();
                var celulas = await LerFolha(bytes, compartilhadas, estilos, data1904, op, prog);
                folhas.Add(MontarFolha(nome, celulas, op));
                if (op.SoPrimeira) break;
            }
            return folhas;
        }
    }

    static string NormalizarCaminho(string c)
    {
        var partes = new List<string>();
        foreach (var p in c.Split('/'))
        {
            if (p == "..") { if (partes.Count > 0) partes.RemoveAt(partes.Count - 1); }
            else if (p != "." && p != "") partes.Add(p);
        }
        return string.Join('/', partes);
    }

    static XmlReader Xml(Stream s) => XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, IgnoreWhitespace = false });

    static (List<(string nome, string rid)>, bool) LerWorkbook(ZipArchiveEntry e)
    {
        var abas = new List<(string, string)>();
        bool d1904 = false;
        using var s = e.Open();
        using var r = Xml(s);
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element) continue;
            if (r.LocalName == "sheet")
            {
                var nome = r.GetAttribute("name") ?? "";
                string? rid = null;
                for (int i = 0; i < r.AttributeCount; i++)
                {
                    r.MoveToAttribute(i);
                    if (r.LocalName == "id") rid = r.Value;
                }
                r.MoveToElement();
                if (rid != null) abas.Add((nome, rid));
            }
            else if (r.LocalName == "workbookPr")
            {
                var v = r.GetAttribute("date1904");
                d1904 = v == "1" || v == "true";
            }
        }
        return (abas, d1904);
    }

    static Dictionary<string, string> LerRels(ZipArchiveEntry? e)
    {
        var d = new Dictionary<string, string>();
        if (e == null) return d;
        using var s = e.Open();
        using var r = Xml(s);
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship")
                d[r.GetAttribute("Id") ?? ""] = r.GetAttribute("Target") ?? "";
        return d;
    }

    static List<string> LerShared(ZipArchiveEntry? e)
    {
        var lista = new List<string>();
        if (e == null) return lista;
        using var s = e.Open();
        using var r = Xml(s);
        StringBuilder? atual = null;
        int nivelRph = 0;
        while (r.Read())
        {
            if (r.NodeType == XmlNodeType.Element)
            {
                if (r.LocalName == "si") { atual = new StringBuilder(); if (r.IsEmptyElement) { lista.Add(""); atual = null; } }
                else if (r.LocalName == "rPh" && !r.IsEmptyElement) nivelRph++;
                else if (r.LocalName == "t" && atual != null && nivelRph == 0 && !r.IsEmptyElement)
                    atual.Append(r.ReadElementContentAsString());
            }
            else if (r.NodeType == XmlNodeType.EndElement)
            {
                if (r.LocalName == "si" && atual != null) { lista.Add(atual.ToString()); atual = null; }
                else if (r.LocalName == "rPh") nivelRph = Math.Max(0, nivelRph - 1);
            }
        }
        return lista;
    }

    enum Formato : byte { Geral, Data, Hora }

    static List<Formato> LerEstilos(ZipArchiveEntry? e)
    {
        var res = new List<Formato>();
        if (e == null) return res;
        var custom = new Dictionary<int, string>();
        var xfs = new List<int>();
        using (var s = e.Open())
        using (var r = Xml(s))
        {
            bool emCellXfs = false;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    if (r.LocalName == "numFmt")
                    {
                        if (int.TryParse(r.GetAttribute("numFmtId"), out var id)) custom[id] = r.GetAttribute("formatCode") ?? "";
                    }
                    else if (r.LocalName == "cellXfs") emCellXfs = !r.IsEmptyElement;
                    else if (r.LocalName == "xf" && emCellXfs)
                        xfs.Add(int.TryParse(r.GetAttribute("numFmtId"), out var n) ? n : 0);
                }
                else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "cellXfs") emCellXfs = false;
            }
        }
        foreach (var id in xfs) res.Add(ClassificarFormato(id, custom.TryGetValue(id, out var c) ? c : null));
        return res;
    }

    static Formato ClassificarFormato(int id, string? codigo)
    {
        if (codigo == null)
        {
            if (id is >= 14 and <= 17 or 22 or >= 27 and <= 36 or >= 50 and <= 58) return Formato.Data;
            if (id is >= 18 and <= 21 or >= 45 and <= 47) return Formato.Hora;
            return Formato.Geral;
        }
        // remove trechos entre aspas, colchetes (cores/condições) e escapes
        var sb = new StringBuilder();
        bool aspas = false;
        int colchete = 0;
        for (int i = 0; i < codigo.Length; i++)
        {
            var c = codigo[i];
            if (c == '"') { aspas = !aspas; continue; }
            if (aspas) continue;
            if (c == '\\' || c == '_' || c == '*') { i++; continue; }
            if (c == '[') { colchete++; continue; }
            if (c == ']') { colchete = Math.Max(0, colchete - 1); continue; }
            if (colchete > 0) continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        var t = sb.ToString();
        if (t.Contains("general")) t = t.Replace("general", "");
        bool data = t.IndexOfAny(new[] { 'd', 'y' }) >= 0;
        bool hora = t.IndexOfAny(new[] { 'h', 's' }) >= 0;
        bool m = t.Contains('m');
        if (data || (m && !hora)) return Formato.Data;
        if (hora) return Formato.Hora;
        return Formato.Geral;
    }

    // ---------- folha: leitor de bytes rápido (sem XmlReader) ----------
    sealed class Bruta
    {
        public List<(int linha, List<(int col, Celula val)> cels)> Linhas = new();
    }

    static async Task<Bruta> LerFolha(byte[] b, List<string> shared, List<Formato> estilos, bool d1904, OpcoesLeitura op, Progresso prog)
    {
        var res = new Bruta();
        int i = 0, n = b.Length;
        int linhaAtual = -1;
        List<(int, Celula)>? cels = null;
        int proximaLinha = 0;
        int contador = 0;
        int limiteLinhas = op.Limite.HasValue ? op.Limite.Value + (op.SemCabecalho ? 0 : 1) : int.MaxValue;
        // pula até sheetData
        int sd = IndexOf(b, "sheetData"u8, 0);
        if (sd < 0) return res;
        i = sd;
        while (i < n)
        {
            int lt = Array.IndexOf(b, (byte)'<', i);
            if (lt < 0) break;
            i = lt + 1;
            if (i >= n) break;
            bool fecha = b[i] == '/';
            int ini = fecha ? i + 1 : i;
            int fimNome = ini;
            while (fimNome < n && b[fimNome] != ' ' && b[fimNome] != '>' && b[fimNome] != '/' && b[fimNome] != '\t' && b[fimNome] != '\r' && b[fimNome] != '\n') fimNome++;
            var nome = LocalName(b, ini, fimNome);
            if (fecha)
            {
                if (nome == "row")
                {
                    if (cels != null) res.Linhas.Add((linhaAtual, cels));
                    cels = null;
                    if (res.Linhas.Count >= limiteLinhas) break;
                }
                else if (nome == "sheetData") break;
                i = fimNome;
                continue;
            }
            int gt = Array.IndexOf(b, (byte)'>', fimNome);
            if (gt < 0) break;
            bool vazio = b[gt - 1] == '/';
            if (nome == "row")
            {
                var r = Atributo(b, fimNome, gt, "r");
                linhaAtual = r != null && int.TryParse(r, out var rr) ? rr - 1 : proximaLinha;
                proximaLinha = linhaAtual + 1;
                cels = new List<(int, Celula)>();
                if (vazio) { res.Linhas.Add((linhaAtual, cels)); cels = null; if (res.Linhas.Count >= limiteLinhas) break; }
                i = gt + 1;
                if (++contador % 2000 == 0) { prog.Msg = $"linha {QSmart.Core.Texto.Fmt(contador)}"; await prog.Talvez(); }
                continue;
            }
            if (nome == "c")
            {
                if (cels == null) { linhaAtual = proximaLinha++; cels = new List<(int, Celula)>(); }
                var refc = Atributo(b, fimNome, gt, "r");
                int col = refc != null ? ColunaDeRef(refc) : (cels.Count > 0 ? cels[^1].Item1 + 1 : 0);
                var t = Atributo(b, fimNome, gt, "t");
                var sAttr = Atributo(b, fimNome, gt, "s");
                int estilo = sAttr != null && int.TryParse(sAttr, out var ss) ? ss : 0;
                string? v = null;
                string? inline = null;
                i = gt + 1;
                if (!vazio)
                {
                    // conteúdo até </c>
                    while (i < n)
                    {
                        int lt2 = Array.IndexOf(b, (byte)'<', i);
                        if (lt2 < 0) { i = n; break; }
                        int p = lt2 + 1;
                        bool fecha2 = b[p] == '/';
                        int ini2 = fecha2 ? p + 1 : p;
                        int fim2 = ini2;
                        while (fim2 < n && b[fim2] != ' ' && b[fim2] != '>' && b[fim2] != '/') fim2++;
                        var nome2 = LocalName(b, ini2, fim2);
                        int gt2 = Array.IndexOf(b, (byte)'>', fim2);
                        if (fecha2 && nome2 == "c") { i = gt2 + 1; break; }
                        if (!fecha2 && (nome2 == "v" || nome2 == "t") && b[gt2 - 1] != '/')
                        {
                            int fimTxt = Array.IndexOf(b, (byte)'<', gt2 + 1);
                            var txt = Decodificar(b, gt2 + 1, fimTxt);
                            if (nome2 == "v") v = txt; else inline = (inline ?? "") + txt;
                            i = fimTxt;
                            continue;
                        }
                        i = gt2 + 1;
                    }
                }
                var cel = Converter(t, v, inline, estilo < estilos.Count ? estilos[estilo] : Formato.Geral, shared, d1904);
                if (cel.Tem) cels.Add((col, cel));
                continue;
            }
            i = gt + 1;
        }
        if (cels != null) res.Linhas.Add((linhaAtual, cels));
        return res;
    }

    static int IndexOf(byte[] b, ReadOnlySpan<byte> alvo, int desde)
    {
        var k = b.AsSpan(desde).IndexOf(alvo);
        return k < 0 ? -1 : desde + k;
    }

    static string LocalName(byte[] b, int ini, int fim)
    {
        for (int k = ini; k < fim; k++) if (b[k] == ':') ini = k + 1;
        return Encoding.ASCII.GetString(b, ini, fim - ini);
    }

    static string? Atributo(byte[] b, int ini, int fim, string nome)
    {
        int k = ini;
        while (k < fim)
        {
            while (k < fim && (b[k] == ' ' || b[k] == '\t' || b[k] == '\r' || b[k] == '\n')) k++;
            int a = k;
            while (k < fim && b[k] != '=' && b[k] != ' ' && b[k] != '>' && b[k] != '/') k++;
            if (k >= fim || b[k] != '=') { k++; continue; }
            var an = LocalNameAttr(b, a, k);
            k++;
            if (k >= fim) break;
            var q = b[k];
            if (q != '"' && q != '\'') continue;
            int vi = k + 1;
            int vf = Array.IndexOf(b, q, vi);
            if (vf < 0) break;
            if (an == nome && Encoding.ASCII.GetString(b, a, k - 1 - a).IndexOf(':') < 0)
                return Decodificar(b, vi, vf);
            k = vf + 1;
        }
        return null;
    }

    static string LocalNameAttr(byte[] b, int ini, int fim) => Encoding.ASCII.GetString(b, ini, fim - ini);

    static string Decodificar(byte[] b, int ini, int fim)
    {
        if (fim <= ini) return "";
        var s = Encoding.UTF8.GetString(b, ini, fim - ini);
        if (s.IndexOf('&') < 0 && s.IndexOf('_') < 0) return s.Replace("\r\n", "\n");
        var sb = new StringBuilder(s.Length);
        for (int k = 0; k < s.Length; k++)
        {
            var c = s[k];
            if (c == '&')
            {
                int pv = s.IndexOf(';', k);
                if (pv > k)
                {
                    var ent = s.Substring(k + 1, pv - k - 1);
                    string? rep = ent switch { "amp" => "&", "lt" => "<", "gt" => ">", "quot" => "\"", "apos" => "'", _ => null };
                    if (rep == null && ent.StartsWith('#'))
                    {
                        try
                        {
                            int cp = ent.StartsWith("#x") || ent.StartsWith("#X")
                                ? int.Parse(ent[2..], NumberStyles.HexNumber) : int.Parse(ent[1..]);
                            rep = char.ConvertFromUtf32(cp);
                        }
                        catch { rep = null; }
                    }
                    if (rep != null) { sb.Append(rep); k = pv; continue; }
                }
            }
            // escapes do Excel: _x000D_
            if (c == '_' && k + 6 < s.Length && s[k + 1] == 'x' && s[k + 6] == '_' &&
                int.TryParse(s.AsSpan(k + 2, 4), NumberStyles.HexNumber, null, out var hx))
            {
                sb.Append((char)hx); k += 6; continue;
            }
            sb.Append(c);
        }
        return sb.ToString().Replace("\r\n", "\n");
    }

    static int ColunaDeRef(string r)
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

    static Celula Converter(string? t, string? v, string? inline, Formato fmt, List<string> shared, bool d1904)
    {
        switch (t)
        {
            case "s":
                if (v != null && int.TryParse(v, out var idx) && idx >= 0 && idx < shared.Count) return Texto(shared[idx]);
                return Celula.Vazia;
            case "inlineStr":
                return Texto(inline ?? v);
            case "str":
                return Texto(v);
            case "b":
                return v == null ? Celula.Vazia : new Celula(TipoCelula.Bool, v == "1" || v == "true" ? "True" : "False", v == "1" ? 1 : 0);
            case "e":
                return Texto(v);
            case "d":
                if (v != null && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd)) return Celula.DeData(dd);
                return Texto(v);
        }
        if (v == null) return inline != null ? Texto(inline) : Celula.Vazia;
        if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) return Texto(v);
        if (fmt == Formato.Data || (fmt == Formato.Hora && num >= 1))
        {
            var d = DeSerial(num, d1904);
            if (d != null) return Celula.DeData(d.Value);
        }
        if (fmt == Formato.Hora)
        {
            var d = DeSerial(num, d1904);
            if (d != null) return new Celula(TipoCelula.Hora, d.Value.ToString("HH:mm:ss", CultureInfo.InvariantCulture), num, d.Value);
        }
        bool inteiro = v.IndexOf('.') < 0 && v.IndexOf('E') < 0 && v.IndexOf('e') < 0;
        if (inteiro && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var li))
            return new Celula(TipoCelula.Inteiro, li.ToString(CultureInfo.InvariantCulture), li);
        // openpyxl: floats inteiros como 3.0 continuam float (str = "3.0")
        return new Celula(TipoCelula.Numero, QSmart.Core.Texto.PyFloat(num), num);
    }

    static Celula Texto(string? s)
    {
        if (s == null || ValoresNa.Contains(s)) return Celula.Vazia;
        return new Celula(TipoCelula.Texto, s);
    }

    public static DateTime? DeSerial(double serial, bool d1904 = false)
    {
        if (double.IsNaN(serial) || serial < 0 || serial > 2958465) return null;
        var baseData = d1904 ? new DateTime(1904, 1, 1) : new DateTime(1899, 12, 30);
        if (!d1904 && serial < 60) baseData = baseData.AddDays(1);
        var ticks = (long)Math.Round(serial * TimeSpan.TicksPerDay / 10.0) * 10;
        // arredonda para microssegundos como o openpyxl
        var dt = baseData.AddTicks(ticks);
        var micro = dt.Ticks % TimeSpan.TicksPerSecond;
        if (micro % 10 != 0) dt = dt.AddTicks(-(micro % 10));
        // remove ruído de ponto flutuante (< 1 ms)
        var resto = dt.Ticks % TimeSpan.TicksPerSecond;
        if (resto < TimeSpan.TicksPerMillisecond) dt = dt.AddTicks(-resto);
        else if (TimeSpan.TicksPerSecond - resto < TimeSpan.TicksPerMillisecond) dt = dt.AddTicks(TimeSpan.TicksPerSecond - resto);
        return dt;
    }

    /// <summary>Converte as células brutas em uma Folha, como o pandas: 1ª linha = cabeçalho, sem as linhas vazias do fim.</summary>
    static Folha MontarFolha(string nome, Bruta b, OpcoesLeitura op)
    {
        var f = new Folha { Nome = nome };
        if (b.Linhas.Count == 0) return f;
        // grade densa
        int ultimaLinha = -1, ultimaCol = -1;
        foreach (var (lin, cels) in b.Linhas)
            foreach (var (c, _) in cels)
            {
                ultimaLinha = Math.Max(ultimaLinha, lin);
                ultimaCol = Math.Max(ultimaCol, c);
            }
        if (ultimaLinha < 0) return f;
        // o pandas usa a linha 1 da planilha como cabeçalho, mesmo vazia
        const int primeira = 0;
        int nLin = ultimaLinha - primeira + 1, nCol = ultimaCol + 1;
        var grade = new Celula[nLin][];
        var porLinha = b.Linhas.GroupBy(x => x.linha).ToDictionary(g => g.Key, g => g.SelectMany(x => x.cels).ToList());
        for (int r = 0; r < nLin; r++)
        {
            var arr = new Celula[nCol];
            if (porLinha.TryGetValue(r + primeira, out var cs))
                foreach (var (c, v) in cs) if (c < nCol) arr[c] = v;
            grade[r] = arr;
        }
        if (op.SemCabecalho)
        {
            f.Colunas = Enumerable.Range(0, nCol).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList();
            f.Linhas = grade.ToList();
            if (op.Limite.HasValue && f.Linhas.Count > op.Limite.Value) f.Linhas = f.Linhas.Take(op.Limite.Value).ToList();
            return f;
        }
        var cab = grade[0];
        var nomes = new List<string>();
        var vistos = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int c = 0; c < nCol; c++)
        {
            var nomeCol = cab[c].Tem ? cab[c].Texto! : $"Unnamed: {c}";
            if (vistos.TryGetValue(nomeCol, out var k))
            {
                var novo = $"{nomeCol}.{k}";
                while (vistos.ContainsKey(novo)) { k++; novo = $"{nomeCol}.{k}"; }
                vistos[nomeCol] = k + 1;
                vistos[novo] = 1;
                nomeCol = novo;
            }
            else vistos[nomeCol] = 1;
            nomes.Add(nomeCol);
        }
        var manter = Enumerable.Range(0, nCol).Where(c => op.Colunas == null || op.Colunas(nomes[c])).ToArray();
        f.Colunas = manter.Select(c => nomes[c]).ToList();
        int fim = nLin;
        while (fim > 1 && grade[fim - 1].All(x => x.EhVazia)) fim--;
        for (int r = 1; r < fim; r++)
        {
            var row = new Celula[manter.Length];
            for (int j = 0; j < manter.Length; j++) row[j] = grade[r][manter[j]];
            f.Linhas.Add(row);
            if (op.Limite.HasValue && f.Linhas.Count >= op.Limite.Value) break;
        }
        return f;
    }
}
