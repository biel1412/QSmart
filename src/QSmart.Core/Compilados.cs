using System.Text;
using System.Text.RegularExpressions;
using QSmart.Core.Planilhas;

namespace QSmart.Core;

/// <summary>Compilado lido inteiro como texto (colunas em maiúsculas).</summary>
public sealed class Compilado
{
    public List<string> Colunas = new();
    public List<string?[]> Linhas = new();
    public int Col(string c) => Colunas.IndexOf(c);
    public string? V(int i, int c) => c < 0 || c >= Linhas[i].Length ? null : Linhas[i][c];
}

public sealed class LinhaVolumeUsuario
{
    public string Usuario = "", Chave = "", Primeira = "", Ultima = "", Sugestao = "";
    public long Total;
    public Dictionary<string, long> Procs = new();
    public int Dias;
    public double Media;
    public bool Achado;
}

public sealed class ResultadoVolumeUsuarios
{
    public List<LinhaVolumeUsuario> Linhas = new();
    public List<string> Procs = new();
    public long Total, TotalBase;
    public int Encontrados;
    public List<LinhaVolumeUsuario> Nao = new(), ZeradosNoDia = new();
    public List<DateOnly> DiasMatriz = new();
    public List<(string usuario, long[] valores)> Matriz = new();
    public List<int> Sel = new();
}

public sealed class BaseCompilados
{
    public Compilado Df = new();
    public string[] U = Array.Empty<string>();
    public DateOnly?[] Dia = Array.Empty<DateOnly?>();
    public List<(string nome, int linhas)> Arquivos = new();
    public List<DateOnly> Dias = new();
    public int Usuarios;
}

public sealed class ResumoCompilado
{
    public string Arquivo = "";
    public Arquivo Fonte = null!;
    public int Linhas;
    public List<string> Colunas = new();
    public List<DateOnly> Dias = new();
    public HashSet<string> Usuarios = new();
}

public sealed class ResultadoUniao
{
    public int Arquivos, Lidas, Duplicadas, Gravadas, Abas;
    public List<DateOnly> Dias = new();
    public bool ColunasDiferentes;
    public byte[] Bytes = Array.Empty<byte>();
}

public static class Compilados
{
    public static readonly string[] ColsData = { "DATAHORAINICIOATIVIDADE", "DATAHORAFIMATIVIDADE" };
    public const int LimiteLinhasXlsx = 1_048_575;
    static readonly Regex Brancos = new(@"\s+", RegexOptions.Compiled);
    static readonly Regex IsoInicio = new(@"^\d{4}-\d{2}-\d{2}", RegexOptions.Compiled);

    public static string NormUsuario(string? v) => v == null ? "" : Brancos.Replace(Texto.SemAcento(v), "").ToUpperInvariant();

    /// <summary>Datas que vieram como AAAA-MM-DD hh:mm:ss viram dd/mm/aaaa hh:mm:ss (_iso_para_br).</summary>
    static void IsoParaBr(Compilado df, int col)
    {
        var idx = new List<int>();
        for (int i = 0; i < df.Linhas.Count; i++)
        {
            var v = df.V(i, col);
            if (v != null && IsoInicio.IsMatch(v)) idx.Add(i);
        }
        if (idx.Count == 0) return;
        var conv = Datas.ToDatetime(idx.Select(i => df.Linhas[i][col]).ToList());
        for (int k = 0; k < idx.Count; k++)
            if (conv[k] != null) df.Linhas[idx[k]][col] = conv[k]!.Value.ToString("dd/MM/yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>ler_compilado: 1ª aba que tiver USUARIO (CSV também). colunas: só essas, se existirem.</summary>
    public static async Task<Compilado> Ler(Arquivo a, HashSet<string>? colunas = null, Progresso? prog = null)
    {
        var op = new OpcoesLeitura { Colunas = colunas == null ? null : c => colunas.Contains(c.Trim().ToUpperInvariant()) };
        var folhas = await Leitor.Ler(a, op, prog);
        var f = folhas.FirstOrDefault(x => x.Colunas.Any(c => c.Trim().ToUpperInvariant() == "USUARIO")) ?? folhas.FirstOrDefault() ?? new Folha();
        var df = new Compilado { Colunas = f.Colunas.Select(c => c.Trim().ToUpperInvariant()).ToList() };
        if (!df.Colunas.Contains("USUARIO"))
            throw new InvalidDataException($"{a.Nome}: não tem a coluna USUARIO (é um compilado?).");
        foreach (var l in f.Linhas)
        {
            var row = l.Select(c => c.Tem ? c.Texto : null).ToArray();
            if (row.Any(v => v != null)) df.Linhas.Add(row);
        }
        foreach (var c in ColsData)
        {
            var k = df.Col(c);
            if (k >= 0) IsoParaBr(df, k);
        }
        return df;
    }

    public static DateOnly?[] DiaCompilado(Compilado df)
    {
        var col = new[] { "DATAHORAFIMATIVIDADE", "DATAHORAINICIOATIVIDADE" }.Select(df.Col).FirstOrDefault(c => c >= 0, -1);
        var r = new DateOnly?[df.Linhas.Count];
        if (col < 0) return r;
        var cache = new Dictionary<string, DateOnly?>();
        for (int i = 0; i < r.Length; i++)
        {
            var v = df.V(i, col) ?? "nan";
            if (!cache.TryGetValue(v, out var d)) cache[v] = d = Datas.DataCelula(v);
            r[i] = d;
        }
        return r;
    }

    /// <summary>ler_lista_usuarios: procura a coluna USUARIO/LOGIN; senão, a coluna com mais 'nome.sobrenome'.</summary>
    public static async Task<List<string>> LerListaUsuarios(Arquivo a)
    {
        List<string?> brutos;
        if (a.Extensao == ".txt")
        {
            var (texto, _) = LeitorCsv.Decodificar(a.Dados);
            brutos = texto.Replace("\r\n", "\n").Split('\n').Select(x => (string?)x).ToList();
        }
        else
        {
            Folha f;
            if (a.Extensao == ".csv")
            {
                var (texto, _) = LeitorCsv.Decodificar(a.Dados);
                var sep = LeitorCsv.Detectar(texto.Length > 16384 ? texto[..16384] : texto, ",;\t| ");
                f = (await LeitorCsv.Ler(a.Dados, new OpcoesLeitura { SemCabecalho = true }, null, sep)).Folha;
            }
            else f = (await LeitorXlsx.Ler(a.Dados, new OpcoesLeitura { SemCabecalho = true, SoPrimeira = true })).FirstOrDefault() ?? new Folha();
            // dropna(how=all) nas linhas e nas colunas
            var linhas = f.Linhas.Where(l => l.Any(c => c.Tem)).ToList();
            if (linhas.Count == 0) return new();
            int nCol = linhas.Max(l => l.Length);
            var cols = Enumerable.Range(0, nCol).Where(c => linhas.Any(l => c < l.Length && l[c].Tem)).ToList();
            if (cols.Count == 0) return new();
            string? V(Celula[] l, int c) => c < l.Length && l[c].Tem ? l[c].Texto : null;
            var cab = cols.Select(c => NormUsuario(V(linhas[0], c))).ToList();
            int? idx = null;
            for (int k = 0; k < cab.Count; k++)
                if (new[] { "USUARIO", "LOGIN", "USER", "MATRICULA" }.Any(x => cab[k].Contains(x))) { idx = k; break; }
            if (idx != null) brutos = linhas.Skip(1).Select(l => V(l, cols[idx.Value])).ToList();
            else
            {
                var pontos = cols.Select(c => linhas.Count(l => (V(l, c) ?? "nan").Contains('.')) / (double)linhas.Count).ToList();
                int melhor = pontos.IndexOf(pontos.Max());
                brutos = linhas.Select(l => V(l, cols[melhor])).ToList();
            }
        }
        return ExtrairUsuarios(brutos);
    }

    /// <summary>extrair_usuarios: um por linha, ou separados por vírgula/ponto e vírgula; sem repetidos.</summary>
    public static List<string> ExtrairUsuarios(IEnumerable<string?> valores)
    {
        var res = new List<string>();
        var vistos = new HashSet<string>();
        foreach (var v in valores)
        {
            if (v == null) continue;
            foreach (var parte in Regex.Split(v, @"[;,\t\n]+"))
            {
                var p = parte.Trim();
                var k = NormUsuario(p);
                if (k != "" && !vistos.Contains(k) && k is not ("USUARIO" or "USUARIOS" or "LOGIN" or "NOME"))
                {
                    vistos.Add(k);
                    res.Add(p);
                }
            }
        }
        return res;
    }

    /// <summary>preparar_compilados: lê um ou mais compilados para a aba Volume (só as colunas necessárias).</summary>
    public static async Task<BaseCompilados> Preparar(IEnumerable<Arquivo> arquivos, Progresso? prog = null)
    {
        var cols = new HashSet<string> { "USUARIO", "PROCEDIMENTO", "ATIVIDADE", "DATAHORAINICIOATIVIDADE", "DATAHORAFIMATIVIDADE", "IDOCORRENCIAPROCESSO" };
        var partes = new List<Compilado>();
        var info = new List<(string, int)>();
        foreach (var a in arquivos)
        {
            if (prog != null) prog.Msg = a.Nome;
            var f = await Ler(a, cols, prog);
            info.Add((a.Nome, f.Linhas.Count));
            partes.Add(f);
        }
        var df = partes.Count == 1 ? partes[0] : Concat(partes);
        var b = new BaseCompilados { Df = df, Arquivos = info };
        var cu = df.Col("USUARIO");
        var cache = new Dictionary<string, string>();
        b.U = new string[df.Linhas.Count];
        for (int i = 0; i < b.U.Length; i++)
        {
            var v = df.V(i, cu) ?? "";
            if (!cache.TryGetValue(v, out var n)) cache[v] = n = NormUsuario(v);
            b.U[i] = n;
        }
        b.Dia = DiaCompilado(df);
        b.Dias = b.Dia.Where(d => d != null).Select(d => d!.Value).Distinct().OrderBy(d => d).ToList();
        b.Usuarios = b.U.Distinct().Count();
        return b;
    }

    public static Compilado Concat(List<Compilado> partes, List<string>? extras = null)
    {
        var df = new Compilado();
        foreach (var p in partes) foreach (var c in p.Colunas) if (!df.Colunas.Contains(c)) df.Colunas.Add(c);
        foreach (var p in partes)
        {
            var mapa = p.Colunas.Select(c => df.Colunas.IndexOf(c)).ToArray();
            foreach (var l in p.Linhas)
            {
                var row = new string?[df.Colunas.Count];
                for (int j = 0; j < mapa.Length && j < l.Length; j++) row[mapa[j]] = l[j];
                df.Linhas.Add(row);
            }
        }
        return df;
    }

    static string Proc(string? v) => (v ?? "(vazio)").Trim().ToUpperInvariant();

    /// <summary>volume_usuarios: quanto cada usuário da lista executou (total, por procedimento e por dia).</summary>
    public static ResultadoVolumeUsuarios VolumeUsuarios(BaseCompilados b, List<string> lista, DateOnly? dia)
    {
        var df = b.Df;
        var r = new ResultadoVolumeUsuarios();
        var alvo = new Dictionary<string, string>();
        var ordem = new List<string>();
        foreach (var u in lista)
        {
            var k = NormUsuario(u);
            if (!alvo.ContainsKey(k)) ordem.Add(k);
            alvo[k] = u;
        }
        var baseIdx = Enumerable.Range(0, df.Linhas.Count).Where(i => dia == null || b.Dia[i] == dia).ToList();
        r.TotalBase = baseIdx.Count;
        r.Sel = baseIdx.Where(i => alvo.ContainsKey(b.U[i])).ToList();
        int cp = df.Col("PROCEDIMENTO");
        if (cp >= 0) r.Procs = Inspecao.ValueCounts(r.Sel.Select(i => Proc(df.V(i, cp)))).Select(x => x.valor).ToList();
        var porU = r.Sel.GroupBy(i => b.U[i]).ToDictionary(g => g.Key, g => g.ToList());
        var todosU = b.U.Distinct().ToList();
        var existe = b.U.ToHashSet();
        foreach (var k in ordem)
        {
            var nome = alvo[k];
            if (!porU.TryGetValue(k, out var g))
            {
                bool outroDia = dia != null && existe.Contains(k);
                var sug = outroDia ? "" : string.Join(", ", Difflib.CloseMatches(k, todosU, 2, 0.75));
                r.Linhas.Add(new LinhaVolumeUsuario { Usuario = nome, Chave = k, Achado = outroDia, Sugestao = sug });
                continue;
            }
            var pc = cp >= 0 ? Inspecao.ValueCounts(g.Select(i => Proc(df.V(i, cp)))).ToDictionary(x => x.valor, x => x.n) : new();
            var dias = g.Select(i => b.Dia[i]).Where(d => d != null).Select(d => d!.Value).Distinct().OrderBy(d => d).ToList();
            r.Linhas.Add(new LinhaVolumeUsuario
            {
                Usuario = nome, Chave = k, Total = g.Count, Procs = pc, Dias = dias.Count,
                Media = dias.Count > 0 ? g.Count / (double)dias.Count : g.Count,
                Primeira = dias.Count > 0 ? Texto.Dmy(dias[0]) : "", Ultima = dias.Count > 0 ? Texto.Dmy(dias[^1]) : "",
                Achado = true,
            });
        }
        r.Linhas = r.Linhas.OrderByDescending(x => x.Total).ToList();
        r.Total = r.Sel.Count;
        r.Encontrados = r.Linhas.Count(x => x.Total > 0);
        r.Nao = r.Linhas.Where(x => !x.Achado).ToList();
        r.ZeradosNoDia = r.Linhas.Where(x => x.Achado && x.Total == 0).ToList();
        var comDia = r.Sel.Where(i => b.Dia[i] != null).ToList();
        r.DiasMatriz = comDia.Select(i => b.Dia[i]!.Value).Distinct().OrderBy(d => d).ToList();
        r.Matriz = comDia.Select(i => b.U[i]).Distinct().OrderBy(x => x, StringComparer.Ordinal)
            .Select(u => (alvo[u], r.DiasMatriz.Select(d => (long)comDia.Count(i => b.U[i] == u && b.Dia[i] == d)).ToArray())).ToList();
        return r;
    }

    public static byte[] ExportarVolumeUsuarios(ResultadoVolumeUsuarios r, BaseCompilados comp, string listaNome, bool comLinhas)
    {
        var procs = r.Procs;
        var wb = new XlsxWriter();
        var s = wb.Planilha("Por usuário");
        s.Cabecalho(new object?[] { "Usuário", "Total executado" }.Concat(procs.Select(p => (object?)Texto.TitleCase(p)))
            .Concat(new object?[] { "Dias trabalhados", "Média por dia", "Primeiro dia", "Último dia", "Situação" }).ToArray());
        foreach (var x in r.Linhas)
        {
            var sit = x.Total > 0 ? "OK" : x.Achado ? "Sem registros no período"
                : "Não encontrado no compilado" + (x.Sugestao != "" ? $" (parecido: {x.Sugestao})" : "");
            s.Linha(new object?[] { x.Usuario, x.Total }.Concat(procs.Select(p => (object?)x.Procs.GetValueOrDefault(p)))
                .Concat(new object?[] { x.Dias, Texto.PyRound(x.Media, 1), x.Primeira, x.Ultima, sit }).ToArray());
        }
        s.Linha(new object?[] { "TOTAL", r.Total }.Concat(procs.Select(p => (object?)r.Linhas.Sum(x => x.Procs.GetValueOrDefault(p))))
            .Concat(new object?[] { "", "", "", "", "" }).ToArray());
        if (r.Matriz.Count > 0)
        {
            var m = wb.Planilha("Por usuário e dia");
            m.Cabecalho(new object?[] { "Usuário" }.Concat(r.DiasMatriz.Select(d => (object?)Texto.Dmy(d))).Append("Total").ToArray());
            foreach (var (u, v) in r.Matriz) m.Linha(new object?[] { u }.Concat(v.Select(x => (object?)x)).Append(v.Sum()).ToArray());
        }
        if (r.Nao.Count > 0)
        {
            var n = wb.Planilha("Não encontrados");
            n.Cabecalho("Usuário da lista", "Parecido no compilado");
            foreach (var x in r.Nao) n.Linha(x.Usuario, x.Sugestao);
        }
        var so = wb.Planilha("Sobre");
        so.Cabecalho("Item", "Valor");
        so.Linha("Compilado(s)", string.Join(", ", comp.Arquivos.Select(x => x.nome)));
        so.Linha("Linhas no compilado", comp.Df.Linhas.Count);
        so.Linha("Lista de usuários", listaNome);
        so.Linha("Usuários na lista", r.Linhas.Count);
        so.Linha("Encontrados", r.Encontrados);
        so.Linha("Gerado em", Texto.Agora());
        if (comLinhas && r.Sel.Count > 0)
        {
            var l = wb.Planilha("Linhas dos usuários");
            l.Cabecalho(comp.Df.Colunas.Select(c => (object?)c).ToArray());
            foreach (var i in r.Sel.Take(LimiteLinhasXlsx)) l.Linha(comp.Df.Linhas[i].Select(v => (object?)v).ToArray());
        }
        foreach (var a in wb.Planilhas) { a.NegritoLinha(0); a.Congelar(1, 1); a.Largura(0, 28); }
        var pu = wb.Planilhas[0];
        for (int i = 0; i < r.Linhas.Count; i++) if (r.Linhas[i].Total == 0) pu.PreencherLinha(i + 1, "F8D9D7");
        pu.NegritoLinha(r.Linhas.Count + 1);
        return wb.Salvar();
    }

    /// <summary>resumo_compilado: linhas, período, usuários e colunas (lista do Corredor).</summary>
    public static async Task<ResumoCompilado> Resumir(Arquivo a, Progresso? prog = null)
    {
        var df = await Ler(a, null, prog);
        var cu = df.Col("USUARIO");
        return new ResumoCompilado
        {
            Arquivo = a.Nome, Fonte = a, Linhas = df.Linhas.Count, Colunas = df.Colunas.ToList(),
            Dias = DiaCompilado(df).Where(d => d != null).Select(d => d!.Value).Distinct().OrderBy(d => d).ToList(),
            Usuarios = Enumerable.Range(0, df.Linhas.Count).Select(i => df.V(i, cu)).Where(v => v != null).Select(NormUsuario).ToHashSet(),
        };
    }

    /// <summary>unir_compilados: junta vários compilados num só, na ordem da data/hora de fim; divide em abas se passar do limite.</summary>
    public static async Task<ResultadoUniao> Unir(List<Arquivo> arquivos, bool csv, bool semDuplicadas, bool comArquivo, Progresso prog)
    {
        var partes = new List<Compilado>();
        var colunas = new List<string>();
        int k = 0;
        foreach (var a in arquivos)
        {
            prog.Msg = $"lendo {++k} de {arquivos.Count}: {a.Nome}";
            await prog.Talvez();
            var f = await Ler(a, null, prog);
            foreach (var c in f.Colunas) if (!colunas.Contains(c)) colunas.Add(c);
            if (comArquivo)
            {
                f.Colunas.Add("ARQUIVO_ORIGEM");
                for (int i = 0; i < f.Linhas.Count; i++) { var r0 = f.Linhas[i]; Array.Resize(ref r0, f.Colunas.Count); r0[^1] = a.Nome; f.Linhas[i] = r0; }
            }
            partes.Add(f);
        }
        prog.Msg = "unindo";
        await prog.Talvez();
        var ordem = colunas.ToList();
        if (comArquivo) ordem.Add("ARQUIVO_ORIGEM");
        var df = new Compilado { Colunas = ordem };
        foreach (var p in partes)
        {
            var mapa = p.Colunas.Select(c => ordem.IndexOf(c)).ToArray();
            foreach (var l in p.Linhas)
            {
                var row = new string?[ordem.Count];
                for (int j = 0; j < mapa.Length && j < l.Length; j++) row[mapa[j]] = l[j];
                df.Linhas.Add(row);
            }
        }
        int lidas = df.Linhas.Count, dup = 0;
        if (semDuplicadas)
        {
            prog.Msg = "removendo linhas repetidas";
            await prog.Talvez();
            var vistos = new HashSet<string>(StringComparer.Ordinal);
            var manter = new List<string?[]>(df.Linhas.Count);
            var sb = new StringBuilder();
            foreach (var l in df.Linhas)
            {
                sb.Clear();
                for (int j = 0; j < colunas.Count; j++) sb.Append(l[j] == null ? "\u0000" : l[j]).Append('\u001f');
                if (vistos.Add(sb.ToString())) manter.Add(l); else dup++;
            }
            df.Linhas = manter;
        }
        var colOrd = new[] { "DATAHORAFIMATIVIDADE", "DATAHORAINICIOATIVIDADE" }.Select(df.Col).FirstOrDefault(c => c >= 0, -1);
        if (colOrd >= 0)
        {
            prog.Msg = "ordenando por data";
            await prog.Talvez();
            var chaves = df.Linhas.Select(l => Datas.Strptime(l[colOrd], "%d/%m/%Y %H:%M:%S")).ToList();
            df.Linhas = df.Linhas.Select((l, i) => (l, i)).OrderBy(x => chaves[x.i] == null ? 1 : 0).ThenBy(x => chaves[x.i] ?? DateTime.MaxValue)
                .Select(x => x.l).ToList();
        }
        var res = new ResultadoUniao { Arquivos = arquivos.Count, Lidas = lidas, Duplicadas = dup, Gravadas = df.Linhas.Count, Abas = 1 };
        if (csv)
        {
            prog.Msg = $"gravando {Texto.Fmt(df.Linhas.Count)} linhas (CSV)";
            await prog.Talvez();
            res.Bytes = GravarCsv(df);
        }
        else
        {
            int nAbas = Math.Max(1, (int)Math.Ceiling(df.Linhas.Count / (double)LimiteLinhasXlsx));
            var wb = new XlsxWriter();
            for (int a = 0; a < nAbas; a++)
            {
                var ws = wb.Planilha(nAbas == 1 ? "Compilado" : $"Compilado_{a + 1}");
                ws.Cabecalho(df.Colunas.Select(c => (object?)c).ToArray());
                ws.NegritoLinha(0);
                ws.Congelar(1, 0);
                int ini = a * LimiteLinhasXlsx, fim = Math.Min(df.Linhas.Count, (a + 1) * LimiteLinhasXlsx);
                for (int i = ini; i < fim; i++)
                {
                    ws.Linha(df.Linhas[i].Select(v => (object?)v).ToArray());
                    if ((i - ini) % 50000 == 0 && i > ini) { prog.Msg = $"gravando linha {Texto.Fmt(i)} de {Texto.Fmt(df.Linhas.Count)}"; await prog.Talvez(); }
                }
            }
            prog.Msg = "finalizando o arquivo";
            await prog.Talvez();
            res.Bytes = wb.Salvar();
            res.Abas = nAbas;
        }
        res.Dias = DiaCompilado(df).Where(d => d != null).Select(d => d!.Value).Distinct().OrderBy(d => d).ToList();
        var setCols = colunas.ToHashSet();
        res.ColunasDiferentes = partes.Any(p => !p.Colunas.Where(c => c != "ARQUIVO_ORIGEM").ToHashSet().SetEquals(setCols));
        return res;
    }

    static byte[] GravarCsv(Compilado df)
    {
        var sb = new StringBuilder();
        string Q(string? v)
        {
            if (v == null) return "";
            if (v.IndexOfAny(new[] { ';', '"', '\n', '\r' }) >= 0) return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }
        sb.Append(string.Join(";", df.Colunas.Select(Q))).Append("\r\n");
        foreach (var l in df.Linhas) sb.Append(string.Join(";", l.Select(Q))).Append("\r\n");
        var corpo = Encoding.UTF8.GetBytes(sb.ToString());
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        return bom.Concat(corpo).ToArray();
    }
}
