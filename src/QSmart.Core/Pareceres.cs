using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using QSmart.Core.Planilhas;

namespace QSmart.Core;

public sealed class ConfigPareceres
{
    public List<string> Padroes { get; set; } = new();
    public List<string> Ignorar { get; set; } = new(Pareceres.IgnorarInicial);
    public List<string> ProcExcluidos { get; set; } = new(Pareceres.ProcExcluidosInicial);
    public string Documento { get; set; } = "";

    public string ParaJson() => new JsonObject
    {
        ["documento"] = Documento,
        ["padroes"] = new JsonArray(Padroes.Select(p => (JsonNode?)p).ToArray()),
        ["ignorar"] = new JsonArray(Ignorar.Select(p => (JsonNode?)p).ToArray()),
        ["proc_excluidos"] = new JsonArray(ProcExcluidos.Select(p => (JsonNode?)p).ToArray()),
    }.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>carregar_config_pareceres: padrões só valem se houver documento gravado.</summary>
    public static ConfigPareceres DeJson(string? json)
    {
        var c = new ConfigPareceres();
        if (string.IsNullOrWhiteSpace(json)) return c;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return c;
            List<string> Lista(JsonNode? n) => (n as JsonArray)?.Select(x => x?.GetValue<string>() ?? "").ToList() ?? new();
            c.Documento = o["documento"]?.GetValue<string>() ?? "";
            c.Padroes = c.Documento != "" ? Lista(o["padroes"]) : new();
            if (o["ignorar"] != null) c.Ignorar = Lista(o["ignorar"]);
            if (o["proc_excluidos"] != null) c.ProcExcluidos = Lista(o["proc_excluidos"]);
        }
        catch { return new ConfigPareceres(); }
        return c;
    }
}

/// <summary>Planilha de pareceres preparada (derivar_pareceres): só a 1ª aba.</summary>
public sealed class PlanilhaPareceres
{
    public List<string> Usuario = new(), Data = new(), Processo = new(), Procedimento = new();
    public List<string?> Justificativa = new();
    /// <summary>Procedimento original é vazio (NaN).</summary>
    public List<bool> ProcVazio = new();
    public int N => Usuario.Count;
}

public sealed record ResultadoParecer(string Usuario, string Data, string Processo, string Procedimento, string TipoParecer,
    string Status, string Motivo, string Sugestao, string Justificativa);

public static class Pareceres
{
    public static readonly string[] IgnorarInicial = { "^EXC", "^PRIORIDADE", "^ATIVIDADE INICIAL DO FLUXO" };
    public static readonly string[] ProcExcluidosInicial = { "APROVAR" };
    public static readonly string[] ColunasObrigatorias = { "USUARIO", "JUSTIFICATIVA" };
    static readonly Regex IdRe = new(@"\b(?:CPF|CNPJ)\s*[:;]?\s*\d[\d.\-/ ]*", RegexOptions.IgnoreCase);
    static readonly Regex Letra = new("[A-Za-zÀ-ÿ]");
    static readonly Regex NaoParecer = new(@"^(\d+\s*[-–]\s*)?(parecer|quando|classifica)", RegexOptions.IgnoreCase);

    public static Regex Compilar(string padrao) => new(padrao, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static List<Regex> CompilarTodos(IEnumerable<string> padroes)
        => padroes.Where(p => p.Trim() != "").Select(Compilar).ToList();

    /// <summary>'Tipo de renda: texto…' -> 'Tipo de renda' (null se a linha não for um parecer).</summary>
    public static string? PrefixoParecer(string t)
    {
        t = t.Replace(' ', ' ').Trim(" -•\t".ToCharArray());
        var k = t.IndexOf(':');
        if (k < 0) return null;
        var p = Texto.ColapsarEspacos(t[..k]).Trim();
        if (p.Length == 0 || p.Length > 70) return null;
        if (NaoParecer.IsMatch(p)) return null;
        return p;
    }

    public static List<string> Unicos(IEnumerable<string> achados)
    {
        var vistos = new Dictionary<string, string>();
        var ordem = new List<string>();
        foreach (var p in achados)
        {
            var k = Texto.CaseFold(p);
            if (!vistos.ContainsKey(k)) { vistos[k] = p; ordem.Add(k); }
        }
        return ordem.Select(k => vistos[k]).ToList();
    }

    static IEnumerable<string> Linhas(string t)
    {
        foreach (var l in t.Split('\n'))
        {
            var p = PrefixoParecer(l);
            if (p != null) yield return p;
        }
    }

    // ---------------- documento .docx ----------------
    const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>Lê o .docx de pareceres e devolve a lista de prefixos (texto até os ':'), como o python-docx.</summary>
    public static List<string> ExtrairDocx(byte[] dados)
    {
        using var ms = new MemoryStream(dados);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var e = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Documento Word inválido (sem word/document.xml).");
        var doc = new XmlDocument();
        using (var s = e.Open()) doc.Load(s);
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("w", W);
        var body = doc.SelectSingleNode("//w:body", ns) ?? throw new InvalidDataException("Documento Word sem corpo.");
        var achados = new List<string>();
        foreach (XmlNode el in body.ChildNodes)
        {
            if (el.LocalName == "p") achados.AddRange(Linhas(TextoParagrafo(el)));
            else if (el.LocalName == "tbl")
            {
                var linhas = CelulasTabela(el);
                if (linhas.Count == 0) continue;
                var cols = linhas[0].Select((c, i) => (c, i)).Where(x => x.c.ToLowerInvariant().Contains("parecer")).Select(x => x.i).ToList();
                foreach (var r in linhas.Skip(1))
                {
                    foreach (var i in cols) if (i < r.Count) achados.AddRange(Linhas(r[i]));
                    if (cols.Count == 0) foreach (var c in r) achados.AddRange(Linhas(c));
                }
            }
        }
        return Unicos(achados);
    }

    static string TextoParagrafo(XmlNode p)
    {
        var sb = new StringBuilder();
        foreach (XmlNode filho in p.ChildNodes)
        {
            if (filho.LocalName == "r") TextoRun(filho, sb);
            else if (filho.LocalName == "hyperlink")
                foreach (XmlNode r in filho.ChildNodes) if (r.LocalName == "r") TextoRun(r, sb);
        }
        return sb.ToString();
    }

    static void TextoRun(XmlNode r, StringBuilder sb)
    {
        foreach (XmlNode x in r.ChildNodes)
        {
            switch (x.LocalName)
            {
                case "t": sb.Append(x.InnerText); break;
                case "tab": case "ptab": sb.Append('\t'); break;
                case "br": case "cr": sb.Append('\n'); break;
                case "noBreakHyphen": sb.Append('-'); break;
            }
        }
    }

    /// <summary>Texto de cada célula da tabela, expandindo células mescladas como o python-docx (row.cells).</summary>
    static List<List<string>> CelulasTabela(XmlNode tbl)
    {
        var res = new List<List<string>>();
        foreach (XmlNode tr in tbl.ChildNodes)
        {
            if (tr.LocalName != "tr") continue;
            var linha = new List<string>();
            foreach (XmlNode tc in tr.ChildNodes)
            {
                if (tc.LocalName != "tc") continue;
                int span = 1;
                bool continua = false;
                foreach (XmlNode pr in tc.ChildNodes)
                {
                    if (pr.LocalName != "tcPr") continue;
                    foreach (XmlNode x in pr.ChildNodes)
                    {
                        if (x.LocalName == "gridSpan" && int.TryParse(x.Attributes?["w:val"]?.Value, out var g)) span = g;
                        if (x.LocalName == "vMerge" && (x.Attributes?["w:val"]?.Value ?? "continue") == "continue") continua = true;
                    }
                }
                var txt = string.Join("\n", tc.ChildNodes.Cast<XmlNode>().Where(n => n.LocalName == "p").Select(TextoParagrafo));
                for (int k = 0; k < span; k++)
                {
                    int col = linha.Count;
                    if (continua && res.Count > 0 && col < res[^1].Count) linha.Add(res[^1][col]);
                    else linha.Add(txt);
                }
            }
            res.Add(linha);
        }
        return res;
    }

    /// <summary>Linhas de texto de um PDF (extraídas pelo navegador) -> prefixos dos pareceres.</summary>
    public static List<string> ExtrairDeLinhas(IEnumerable<string> linhas)
        => Unicos(linhas.Select(PrefixoParecer).Where(p => p != null).Select(p => p!));

    // ---------------- conferência ----------------
    public static string NormalizarParecer(string s, bool ignorarAcentos)
    {
        s = s.Replace(' ', ' ');
        s = ignorarAcentos ? Texto.SemAcento(s) : Texto.Nfc(s);
        return Texto.CaseFold(Texto.ColapsarEspacos(s).Trim());
    }

    /// <summary>separar_parecer: (tipo, prefixo, texto_limpo). tipo: vazio | ignorado | parecer.</summary>
    public static (string tipo, string pref, string limpo) Separar(string? texto, List<Regex> regexIgnorar)
    {
        if (texto == null) return ("vazio", "", "");
        var t = texto.Replace("\\\n", "\n");
        var limpo = IdRe.Replace(t, " ");
        limpo = Texto.ColapsarEspacos(limpo).Trim(" -:;.,\t".ToCharArray());
        if (!Letra.IsMatch(limpo)) return ("vazio", "", "");
        var chave = Texto.SemAcento(limpo).ToUpperInvariant();
        if (regexIgnorar.Any(r => r.IsMatch(chave))) return ("ignorado", "", limpo);
        var ini = limpo.Length > 90 ? limpo[..90] : limpo;
        if (ini.Contains(':')) return ("parecer", limpo[..limpo.IndexOf(':')].Trim(), limpo);
        return ("parecer", limpo.Length > 60 ? limpo[..60] : limpo, limpo);
    }

    public sealed class Contexto
    {
        public List<Regex> RegexIgnorar = new();
        public Dictionary<string, string> Mapa = new();
        public List<string> Chaves = new();
        public bool IgnorarAcentos;
        public Dictionary<string, List<string>> Cache = new();

        public static Contexto Criar(IEnumerable<string> padroes, IEnumerable<string> ignorar, bool ignorarAcentos)
        {
            var c = new Contexto { RegexIgnorar = CompilarTodos(ignorar), IgnorarAcentos = ignorarAcentos };
            foreach (var p in padroes) c.Mapa[NormalizarParecer(p, ignorarAcentos)] = p;
            c.Chaves = c.Mapa.Keys.ToList();
            return c;
        }
    }

    /// <summary>classificar_parecer: (tipo, status, motivo, sugestão, tipo de parecer, texto limpo).</summary>
    public static (string tipo, string status, string motivo, string sug, string canon, string limpo) Classificar(string? just, Contexto ctx)
    {
        var (tipo, pref, limpo) = Separar(just, ctx.RegexIgnorar);
        if (tipo != "parecer") return (tipo, "", "", "", "", limpo);
        var k = NormalizarParecer(pref, ctx.IgnorarAcentos);
        bool semDp = !(limpo.Length > 90 ? limpo[..90] : limpo).Contains(':');
        if (!ctx.Cache.TryGetValue(k, out var perto)) ctx.Cache[k] = perto = Difflib.CloseMatches(k, ctx.Chaves, 1, 0.6);
        if (!semDp && ctx.Mapa.TryGetValue(k, out var canon)) return (tipo, "Correto", "", "", canon, limpo);
        var sug = perto.Count > 0 ? ctx.Mapa[perto[0]] : "";
        string motivo;
        if (semDp) motivo = "Sem ':' após o tipo";
        else if (sug != "" && Texto.CaseFold(Texto.SemAcento(pref)) == Texto.CaseFold(Texto.SemAcento(sug))) motivo = "Acentuação diferente do padrão";
        else motivo = "Tipo não consta nos padrões";
        return (tipo, "Divergente", motivo, sug, pref, limpo);
    }

    public static (List<ResultadoParecer> res, int vazio, int ign) Analisar(PlanilhaPareceres df, IEnumerable<int> linhas,
        IEnumerable<string> padroes, IEnumerable<string> ignorar, bool ignorarAcentos)
    {
        var ctx = Contexto.Criar(padroes, ignorar, ignorarAcentos);
        var res = new List<ResultadoParecer>();
        int nVazio = 0, nIgn = 0;
        foreach (var i in linhas)
        {
            var (tipo, status, motivo, sug, canon, limpo) = Classificar(df.Justificativa[i], ctx);
            if (tipo == "vazio") { nVazio++; continue; }
            if (tipo == "ignorado") { nIgn++; continue; }
            res.Add(new ResultadoParecer(df.Usuario[i], df.Data[i], df.Processo[i], df.Procedimento[i], canon, status, motivo, sug, limpo));
        }
        return (res, nVazio, nIgn);
    }

    /// <summary>derivar_pareceres: valida e cria DATA, USUARIO, PROCESSO e PROCEDIMENTO (só a 1ª aba).</summary>
    public static PlanilhaPareceres Derivar(Folha f)
    {
        var cols = f.Colunas.Select(c => c.Trim().ToUpperInvariant()).ToList();
        var faltando = ColunasObrigatorias.Where(c => !cols.Contains(c)).ToList();
        if (faltando.Count > 0) throw new InvalidDataException("A planilha não possui a(s) coluna(s): " + string.Join(", ", faltando));
        int Col(string c) => cols.IndexOf(c);
        var p = new PlanilhaPareceres();
        int cFim = Col("DATAHORAFIMATIVIDADE");
        if (cFim < 0) cFim = Col("DATAHORAINICIOATIVIDADE");
        string[] datas;
        if (cFim >= 0)
        {
            var dt = Datas.ToDatetime(f.Coluna(cFim), diaPrimeiro: true);
            datas = dt.Select(d => d?.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "").ToArray();
        }
        else datas = Enumerable.Repeat("", f.N).ToArray();
        int cU = Col("USUARIO"), cJ = Col("JUSTIFICATIVA"), cProc = Col("NOMEPROCESSO"), cPr = Col("PROCEDIMENTO");
        for (int i = 0; i < f.N; i++)
        {
            var u = f.Valor(i, cU);
            p.Usuario.Add(u.Tem ? u.Texto!.Trim() : "(sem usuário)");
            p.Data.Add(datas[i]);
            var j = f.Valor(i, cJ);
            p.Justificativa.Add(j.Tem ? j.Texto : null);
            var pr = cProc >= 0 ? f.Valor(i, cProc) : Celula.Vazia;
            p.Processo.Add(pr.Tem ? pr.Texto! : "");
            var pc = cPr >= 0 ? f.Valor(i, cPr) : Celula.Vazia;
            p.Procedimento.Add(pc.Tem ? pc.Texto! : "");
            p.ProcVazio.Add(!pc.Tem);
        }
        return p;
    }

    public static async Task<PlanilhaPareceres> Preparar(Arquivo a, Progresso? prog = null)
    {
        var folhas = await Leitor.Ler(a, new OpcoesLeitura { SoPrimeira = true }, prog);
        if (folhas.Count == 0) throw new InvalidDataException("A planilha não tem abas.");
        return Derivar(folhas[0]);
    }

    public static string NormProc(string p) => Texto.SemAcento(p).ToUpperInvariant().Trim();
}
