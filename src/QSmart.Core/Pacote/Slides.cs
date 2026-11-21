using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QSmart.Core.Pacote;

// =====================================================================================================================
// Smart Screen: modelo da apresentação. O slide tem 960 × 540 "pontos de tela" (16:9); no .pptx isso vira 12.192.000 × 6.858.000 EMU.
// =====================================================================================================================

public sealed class Serie
{
    public string Nome = "";
    public List<double?> V = new();
}

public sealed class Elemento
{
    public string Id = Guid.NewGuid().ToString("N")[..8];
    /// <summary>texto, imagem, forma, tabela, grafico</summary>
    public string Tipo = "texto";
    public double X, Y, W = 300, H = 80, Rot;

    // ---- texto (também usado dentro das formas)
    public string Html = "";
    /// <summary>titulo, subtitulo, corpo ou vazio. Define a fonte e o tamanho padrão do tema e o texto de ajuda.</summary>
    public string Papel = "";
    /// <summary>t, m, b (alinhamento vertical)</summary>
    public string AlinhV = "t";
    /// <summary>Tamanho base da letra (px do slide). 0 = padrão do papel.</summary>
    public double Tam;
    /// <summary>Cor base do texto. Vazio = cor do tema.</summary>
    public string Cor = "";

    // ---- forma
    /// <summary>ret, arred, elipse, triangulo, seta, estrela, losango, linha</summary>
    public string Forma = "ret";
    public string Preench = "";
    public string Borda = "";
    public double BordaL = 0;

    // ---- imagem
    public string Src = "";

    // ---- tabela
    public List<List<string>> Celulas = new();
    public bool CabDestaque = true;

    // ---- gráfico
    /// <summary>colunas, barras, linhas, pizza</summary>
    public string Graf = "colunas";
    public string TituloGraf = "";
    public List<string> Cats = new();
    public List<Serie> Series = new();

    // ---- animação de entrada (em ordem de clique)
    /// <summary>vazio, aparecer, esmaecer, subir, zoom</summary>
    public string Anim = "";
    /// <summary>Ordem do clique na animação (1, 2, 3…).</summary>
    public int Ordem;

    public Elemento Clonar()
    {
        var e = (Elemento)MemberwiseClone();
        e.Id = Guid.NewGuid().ToString("N")[..8];
        e.Celulas = Celulas.Select(l => l.ToList()).ToList();
        e.Cats = Cats.ToList();
        e.Series = Series.Select(s => new Serie { Nome = s.Nome, V = s.V.ToList() }).ToList();
        return e;
    }
}

public sealed class Slide
{
    public string Id = Guid.NewGuid().ToString("N")[..8];
    public string Layout = "conteudo";
    /// <summary>Cor de fundo própria. Vazio = fundo do tema.</summary>
    public string Fundo = "";
    /// <summary>nenhuma, esmaecer, empurrar, revelar, zoom</summary>
    public string Transicao = "";
    public string Notas = "";
    public bool Oculto;
    public List<Elemento> Elementos = new();

    /// <summary>Elementos animados na ordem dos cliques.</summary>
    public List<Elemento> Animados => Elementos.Where(e => e.Anim != "").OrderBy(e => e.Ordem).ThenBy(e => Elementos.IndexOf(e)).ToList();
    public void Renumerar() { int i = 1; foreach (var e in Animados) e.Ordem = i++; }

    public Slide Clonar() => new()
    {
        Layout = Layout, Fundo = Fundo, Transicao = Transicao, Notas = Notas, Oculto = Oculto,
        Elementos = Elementos.Select(e => e.Clonar()).ToList(),
    };
}

public sealed class Tema
{
    public string Chave = "", Nome = "";
    public string Fundo = "#FFFFFF", Fundo2 = "";          // Fundo2 = gradiente (vazio = cor sólida)
    public string FundoTitulo = "", FundoTitulo2 = "";      // slides de título/seção
    public string Texto = "#1F2937", Titulo = "#111827", TextoTitulo = "";  // TextoTitulo = cor do texto nos slides de título
    public string[] Cores = { "#2563EB", "#10B981", "#F59E0B", "#EF4444", "#8B5CF6", "#EC4899" };
    public string FonteTit = "Inter", FonteCorpo = "Inter";
    /// <summary>Detalhe visual: barra, faixa, nenhum</summary>
    public string Enfeite = "";

    public string Destaque => Cores[0];
    public (string f1, string f2) FundoDe(Slide s) =>
        s.Fundo != "" ? (s.Fundo, "") :
        (s.Layout is "titulo" or "secao") && FundoTitulo != "" ? (FundoTitulo, FundoTitulo2) : (Fundo, Fundo2);
    public string TextoDe(Slide s, Elemento e)
    {
        if (e.Cor != "") return e.Cor;
        if (s.Fundo == "" && (s.Layout is "titulo" or "secao") && TextoTitulo != "") return TextoTitulo;
        if (s.Fundo != "") return Apresentacao.Escura(s.Fundo) ? "#FFFFFF" : "#1F2937";
        return e.Papel == "titulo" ? Titulo : Texto;
    }
    public string FonteDe(Elemento e) => e.Papel is "titulo" or "subtitulo" ? FonteTit : FonteCorpo;
}

public sealed class Apresentacao
{
    public string Tema = "claro";
    public List<Slide> Slides = new();

    // ============================================================================ temas
    public static readonly Tema[] Temas =
    {
        new() { Chave = "claro", Nome = "Claro", Fundo = "#FFFFFF", Texto = "#334155", Titulo = "#0F172A", FundoTitulo = "#EEF2FF", FundoTitulo2 = "#FFFFFF",
                Cores = new[] { "#4F46E5", "#0EA5E9", "#10B981", "#F59E0B", "#EF4444", "#A855F7" }, Enfeite = "barra" },
        new() { Chave = "qsmart", Nome = "QSmart", Fundo = "#0B1220", Fundo2 = "#172554", Texto = "#CBD5E1", Titulo = "#FFFFFF", FundoTitulo = "#0B1220", FundoTitulo2 = "#4338CA",
                Cores = new[] { "#22D3EE", "#A78BFA", "#34D399", "#FBBF24", "#F472B6", "#60A5FA" }, Enfeite = "faixa" },
        new() { Chave = "esmeralda", Nome = "Esmeralda", Fundo = "#F0FDF4", Texto = "#14532D", Titulo = "#064E3B", FundoTitulo = "#065F46", FundoTitulo2 = "#10B981", TextoTitulo = "#FFFFFF",
                Cores = new[] { "#059669", "#0D9488", "#65A30D", "#CA8A04", "#0284C7", "#7C3AED" }, FonteTit = "Georgia", Enfeite = "barra" },
        new() { Chave = "coral", Nome = "Coral", Fundo = "#FFF7ED", Texto = "#431407", Titulo = "#7C2D12", FundoTitulo = "#EA580C", FundoTitulo2 = "#F43F5E", TextoTitulo = "#FFFFFF",
                Cores = new[] { "#EA580C", "#E11D48", "#D97706", "#0891B2", "#7C3AED", "#16A34A" }, Enfeite = "faixa" },
        new() { Chave = "grafite", Nome = "Grafite", Fundo = "#18181B", Texto = "#D4D4D8", Titulo = "#FAFAFA", FundoTitulo = "#09090B", FundoTitulo2 = "#27272A",
                Cores = new[] { "#F59E0B", "#38BDF8", "#4ADE80", "#F87171", "#C084FC", "#FB7185" }, Enfeite = "barra" },
        new() { Chave = "lavanda", Nome = "Lavanda", Fundo = "#FAF5FF", Texto = "#3B0764", Titulo = "#581C87", FundoTitulo = "#7C3AED", FundoTitulo2 = "#DB2777", TextoTitulo = "#FFFFFF",
                Cores = new[] { "#7C3AED", "#DB2777", "#2563EB", "#059669", "#F59E0B", "#0EA5E9" }, FonteTit = "Georgia", Enfeite = "faixa" },
        new() { Chave = "simples", Nome = "Simples", Fundo = "#FFFFFF", Texto = "#000000", Titulo = "#000000",
                Cores = new[] { "#4472C4", "#ED7D31", "#A5A5A5", "#FFC000", "#5B9BD5", "#70AD47" }, FonteTit = "Arial", FonteCorpo = "Arial" },
    };

    public Tema T => Temas.FirstOrDefault(t => t.Chave == Tema) ?? Temas[0];

    // ============================================================================ layouts
    public static readonly (string chave, string nome)[] Layouts =
    {
        ("titulo", "Título"), ("conteudo", "Título e conteúdo"), ("duas", "Duas colunas"), ("secao", "Seção"),
        ("imagem", "Imagem com legenda"), ("so-titulo", "Só título"), ("branco", "Em branco"),
    };

    public static Slide NovoSlide(string layout)
    {
        var s = new Slide { Layout = layout };
        Elemento Txt(string papel, double x, double y, double w, double h, string alinhV = "t", string html = "") =>
            new() { Tipo = "texto", Papel = papel, X = x, Y = y, W = w, H = h, AlinhV = alinhV, Html = html };
        switch (layout)
        {
            case "titulo":
                s.Elementos.Add(Txt("titulo", 80, 170, 800, 120, "b", "<p style=\"text-align:center\"></p>"));
                s.Elementos.Add(Txt("subtitulo", 80, 305, 800, 70, "t", "<p style=\"text-align:center\"></p>"));
                break;
            case "secao":
                s.Elementos.Add(Txt("titulo", 80, 200, 800, 110, "b"));
                s.Elementos.Add(Txt("subtitulo", 80, 320, 800, 60));
                break;
            case "conteudo":
                s.Elementos.Add(Txt("titulo", 60, 36, 840, 80, "m"));
                s.Elementos.Add(Txt("corpo", 60, 135, 840, 360, "t", "<ul><li></li></ul>"));
                break;
            case "duas":
                s.Elementos.Add(Txt("titulo", 60, 36, 840, 80, "m"));
                s.Elementos.Add(Txt("corpo", 60, 135, 405, 360, "t", "<ul><li></li></ul>"));
                s.Elementos.Add(Txt("corpo", 495, 135, 405, 360, "t", "<ul><li></li></ul>"));
                break;
            case "imagem":
                s.Elementos.Add(Txt("titulo", 60, 36, 840, 80, "m"));
                s.Elementos.Add(new Elemento { Tipo = "imagem", X = 60, Y = 130, W = 540, H = 360 });
                s.Elementos.Add(Txt("corpo", 630, 130, 270, 360));
                break;
            case "so-titulo":
                s.Elementos.Add(Txt("titulo", 60, 36, 840, 80, "m"));
                break;
        }
        return s;
    }

    public static Apresentacao Nova()
    {
        var a = new Apresentacao();
        a.Slides.Add(NovoSlide("titulo"));
        return a;
    }

    public Apresentacao Clonar() => new() { Tema = Tema, Slides = Slides.Select(s => { var c = s.Clonar(); c.Id = s.Id; for (int i = 0; i < c.Elementos.Count; i++) c.Elementos[i].Id = s.Elementos[i].Id; return c; }).ToList() };

    public static double TamPadrao(string papel) => papel switch { "titulo" => 46, "subtitulo" => 26, "corpo" => 24, _ => 22 };

    public static string Ajuda(Slide s, Elemento e) => e.Papel switch
    {
        "titulo" => s.Layout is "titulo" or "secao" ? "Clique para adicionar um título" : "Clique para adicionar o título",
        "subtitulo" => "Clique para adicionar um subtítulo",
        "corpo" => "Clique para adicionar texto",
        _ => "",
    };

    public static bool Escura(string cor)
    {
        var c = cor.TrimStart('#');
        if (c.Length != 6 || !int.TryParse(c, NumberStyles.HexNumber, null, out var v)) return false;
        double r = (v >> 16) & 255, g = (v >> 8) & 255, b = v & 255;
        return 0.299 * r + 0.587 * g + 0.114 * b < 140;
    }

    // ============================================================================ JSON (rascunho no navegador)
    static string S(JsonNode? n, string padrao = "") => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : padrao;
    static double D(JsonNode? n, double padrao = 0) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : padrao;
    static bool B(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    public string ParaJson()
    {
        var sl = new JsonArray();
        foreach (var s in Slides)
        {
            var els = new JsonArray();
            foreach (var e in s.Elementos)
            {
                var o = new JsonObject { ["t"] = e.Tipo, ["x"] = e.X, ["y"] = e.Y, ["w"] = e.W, ["h"] = e.H };
                if (e.Rot != 0) o["rot"] = e.Rot;
                if (e.Html != "") o["html"] = e.Html;
                if (e.Papel != "") o["papel"] = e.Papel;
                if (e.AlinhV != "t") o["av"] = e.AlinhV;
                if (e.Tam != 0) o["tam"] = e.Tam;
                if (e.Cor != "") o["cor"] = e.Cor;
                if (e.Tipo == "forma") { o["forma"] = e.Forma; o["pre"] = e.Preench; o["borda"] = e.Borda; o["bl"] = e.BordaL; }
                if (e.Src != "") o["src"] = e.Src;
                if (e.Tipo == "tabela")
                {
                    o["cels"] = new JsonArray(e.Celulas.Select(l => (JsonNode)new JsonArray(l.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray())).ToArray());
                    o["cab"] = e.CabDestaque;
                }
                if (e.Tipo == "grafico")
                {
                    o["graf"] = e.Graf; o["tg"] = e.TituloGraf;
                    o["cats"] = new JsonArray(e.Cats.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray());
                    o["series"] = new JsonArray(e.Series.Select(x => (JsonNode)new JsonObject
                    {
                        ["n"] = x.Nome,
                        ["v"] = new JsonArray(x.V.Select(v => v is double d ? (JsonNode)JsonValue.Create(d)! : null).ToArray()),
                    }).ToArray());
                }
                if (e.Anim != "") { o["anim"] = e.Anim; o["ord"] = e.Ordem; }
                els.Add(o);
            }
            sl.Add(new JsonObject
            {
                ["layout"] = s.Layout, ["fundo"] = s.Fundo, ["trans"] = s.Transicao, ["notas"] = s.Notas, ["oculto"] = s.Oculto, ["els"] = els,
            });
        }
        return new JsonObject { ["tema"] = Tema, ["slides"] = sl }.ToJsonString();
    }

    public static Apresentacao DeJson(string json)
    {
        var j = JsonNode.Parse(json)!;
        var a = new Apresentacao { Tema = S(j["tema"], "claro") };
        foreach (var sn in j["slides"]?.AsArray() ?? new JsonArray())
        {
            if (sn == null) continue;
            var s = new Slide { Layout = S(sn["layout"], "branco"), Fundo = S(sn["fundo"]), Transicao = S(sn["trans"]), Notas = S(sn["notas"]), Oculto = B(sn["oculto"]) };
            foreach (var o in sn["els"]?.AsArray() ?? new JsonArray())
            {
                if (o == null) continue;
                var e = new Elemento
                {
                    Tipo = S(o["t"], "texto"), X = D(o["x"]), Y = D(o["y"]), W = D(o["w"], 100), H = D(o["h"], 50), Rot = D(o["rot"]),
                    Html = S(o["html"]), Papel = S(o["papel"]), AlinhV = S(o["av"], "t"), Tam = D(o["tam"]), Cor = S(o["cor"]),
                    Forma = S(o["forma"], "ret"), Preench = S(o["pre"]), Borda = S(o["borda"]), BordaL = D(o["bl"]),
                    Src = S(o["src"]), CabDestaque = o["cab"] == null || B(o["cab"]),
                    Graf = S(o["graf"], "colunas"), TituloGraf = S(o["tg"]), Anim = S(o["anim"]), Ordem = (int)D(o["ord"]),
                };
                foreach (var l in o["cels"]?.AsArray() ?? new JsonArray())
                    e.Celulas.Add(l?.AsArray().Select(c => S(c)).ToList() ?? new());
                foreach (var c in o["cats"]?.AsArray() ?? new JsonArray()) e.Cats.Add(S(c));
                foreach (var x in o["series"]?.AsArray() ?? new JsonArray())
                    if (x != null) e.Series.Add(new Serie { Nome = S(x["n"]), V = x["v"]?.AsArray().Select(v => v is JsonValue vv && vv.TryGetValue<double>(out var d) ? (double?)d : null).ToList() ?? new() });
                s.Elementos.Add(e);
            }
            a.Slides.Add(s);
        }
        if (a.Slides.Count == 0) a.Slides.Add(NovoSlide("titulo"));
        return a;
    }

    // ============================================================================ dados do gráfico em texto (para editar)
    public static string DadosTsv(Elemento e)
    {
        var sb = new StringBuilder();
        sb.Append("Categoria");
        foreach (var s in e.Series) sb.Append('\t').Append(s.Nome);
        for (int i = 0; i < e.Cats.Count; i++)
        {
            sb.Append('\n').Append(e.Cats[i]);
            foreach (var s in e.Series) sb.Append('\t').Append(i < s.V.Count && s.V[i] is double d ? d.ToString(FormatoPt.Pt) : "");
        }
        return sb.ToString();
    }

    public static void LerTsv(Elemento e, string tsv)
    {
        var linhas = tsv.Replace("\r", "").Split('\n').Where(l => l.Trim() != "").Select(l => l.Split('\t', ';')).ToList();
        if (linhas.Count == 0) return;
        var cab = linhas[0];
        e.Series = cab.Skip(1).Select(n => new Serie { Nome = n.Trim() }).ToList();
        e.Cats = new();
        foreach (var l in linhas.Skip(1))
        {
            e.Cats.Add(l[0].Trim());
            for (int k = 0; k < e.Series.Count; k++)
            {
                var t = k + 1 < l.Length ? l[k + 1].Trim() : "";
                e.Series[k].V.Add(FormatoPt.Numero(t, out var d) ? d : null);
            }
        }
    }

    public static XlsxPasta.DadosGrafico? Dados(Elemento e)
    {
        if (e.Series.Count == 0 || e.Cats.Count == 0) return null;
        var ser = e.Series.Select(s => (s.Nome, Enumerable.Range(0, e.Cats.Count).Select(i => i < s.V.Count ? s.V[i] : null).ToList())).ToList();
        if (ser.All(s => s.Item2.All(v => v == null))) return null;
        return new XlsxPasta.DadosGrafico(e.Cats.ToList(), ser, true, true, 0, 0, e.Cats.Count, e.Series.Count);
    }

    public static Elemento GraficoExemplo() => new()
    {
        Tipo = "grafico", X = 180, Y = 130, W = 600, H = 360, Graf = "colunas",
        Cats = new() { "Jan", "Fev", "Mar", "Abr" },
        Series = new() { new Serie { Nome = "Realizado", V = new() { 120, 150, 98, 180 } }, new Serie { Nome = "Meta", V = new() { 130, 130, 130, 130 } } },
    };

    public static Elemento TabelaExemplo(int l = 4, int c = 3) => new()
    {
        Tipo = "tabela", X = 120, Y = 150, W = 720, H = 40 * l,
        Celulas = Enumerable.Range(0, l).Select(i => Enumerable.Range(0, c).Select(j => i == 0 ? $"Coluna {j + 1}" : "").ToList()).ToList(),
    };
}

// =====================================================================================================================
// Texto rico: o HTML limitado do editor (p, ul/ol/li, b/i/u/s, span com cor/tamanho/fonte, br) em parágrafos e trechos.
// =====================================================================================================================
public sealed class Trecho
{
    public string Texto = "";
    public bool N, I, U, S;
    public string Cor = "", Fonte = "";
    public double Tam;   // px do slide; 0 = padrão
    public string Link = "";
}

public sealed class Paragrafo
{
    public string Alinh = "";          // left, center, right, justify
    public string Lista = "";          // "", "ul", "ol"
    public int Nivel;
    public List<Trecho> Trechos = new();
    public string TextoPuro => string.Concat(Trechos.Select(t => t.Texto));
}

public static class TextoRico
{
    static readonly Regex Tok = new(@"<(/?)([a-zA-Z0-9]+)([^>]*)>|([^<]+)", RegexOptions.Compiled);
    static readonly Regex Attr = new(@"([a-zA-Z-]+)\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)", RegexOptions.Compiled);

    public static List<Paragrafo> Ler(string html)
    {
        var res = new List<Paragrafo>();
        var pilha = new List<(string tag, Trecho est)>();
        var listas = new List<string>();
        Paragrafo? atual = null;
        string alinhBloco = "";
        Trecho Est() => pilha.Count > 0 ? pilha[^1].est : new Trecho();

        Paragrafo Novo()
        {
            atual = new Paragrafo { Alinh = alinhBloco, Lista = listas.Count > 0 ? listas[^1] : "", Nivel = Math.Max(0, listas.Count - 1) };
            res.Add(atual);
            return atual;
        }
        void Fechar() { atual = null; }

        foreach (Match m in Tok.Matches(html ?? ""))
        {
            if (m.Groups[4].Success)
            {
                var t = WebUtility.HtmlDecode(m.Groups[4].Value).Replace("\n", " ").Replace("\r", "");
                if (t.Length == 0) continue;
                if (atual == null && t.Trim() == "") continue;
                var p = atual ?? Novo();
                var e = Est();
                p.Trechos.Add(new Trecho { Texto = t, N = e.N, I = e.I, U = e.U, S = e.S, Cor = e.Cor, Fonte = e.Fonte, Tam = e.Tam, Link = e.Link });
                continue;
            }
            bool fecha = m.Groups[1].Value == "/";
            var tag = m.Groups[2].Value.ToLowerInvariant();
            var attrs = m.Groups[3].Value;
            switch (tag)
            {
                case "p": case "div": case "h1": case "h2": case "h3": case "h4": case "blockquote":
                    if (fecha) { Fechar(); alinhBloco = ""; }
                    else { alinhBloco = AlinhDe(attrs); Novo(); if (tag.StartsWith("h")) Empilhar(tag, attrs, true); }
                    if (fecha && tag.StartsWith("h")) Desempilhar(tag);
                    break;
                case "li":
                    if (fecha) Fechar();
                    else
                    {
                        alinhBloco = AlinhDe(attrs); var li = Novo();
                        var mm = Regex.Match(Atributo(attrs, "style"), @"margin-left\s*:\s*([\d.]+)em");
                        if (mm.Success) li.Nivel += (int)Math.Round(double.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture) / 1.5);
                    }
                    break;
                case "ul": case "ol":
                    if (fecha) { if (listas.Count > 0) listas.RemoveAt(listas.Count - 1); }
                    else listas.Add(tag);
                    Fechar();
                    break;
                case "br":
                    if (atual == null) Novo();
                    Fechar();
                    break;
                case "b": case "strong": case "i": case "em": case "u": case "s": case "strike": case "span": case "font": case "a":
                    if (fecha) Desempilhar(tag); else Empilhar(tag, attrs, false);
                    break;
            }
        }
        // linhas vazias no fim não contam
        while (res.Count > 1 && res[^1].Trechos.All(t => t.Texto.Trim() == "")) res.RemoveAt(res.Count - 1);
        if (res.Count == 0) res.Add(new Paragrafo { Lista = "" });
        return res;

        void Empilhar(string tag, string attrs, bool negrito)
        {
            var b = Est();
            var e = new Trecho { N = b.N, I = b.I, U = b.U, S = b.S, Cor = b.Cor, Fonte = b.Fonte, Tam = b.Tam, Link = b.Link };
            if (negrito || tag is "b" or "strong") e.N = true;
            if (tag is "i" or "em") e.I = true;
            if (tag == "u") e.U = true;
            if (tag is "s" or "strike") e.S = true;
            var est = Atributo(attrs, "style");
            if (tag == "font") { var c = Atributo(attrs, "color"); if (c != "") e.Cor = Cor(c); var f = Atributo(attrs, "face"); if (f != "") e.Fonte = f; }
            if (tag == "a") e.Link = Atributo(attrs, "href");
            foreach (var dec in est.Split(';'))
            {
                var kv = dec.Split(':', 2);
                if (kv.Length < 2) continue;
                var k = kv[0].Trim().ToLowerInvariant(); var v = kv[1].Trim();
                switch (k)
                {
                    case "font-weight": e.N = v is "bold" or "700" or "800" or "900" || (int.TryParse(v, out var w) && w >= 600); break;
                    case "font-style": e.I = v == "italic"; break;
                    case "text-decoration": case "text-decoration-line": if (v.Contains("underline")) e.U = true; if (v.Contains("line-through")) e.S = true; break;
                    case "color": e.Cor = Cor(v); break;
                    case "font-size": if (v.EndsWith("px") && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px)) e.Tam = px;
                                      else if (v.EndsWith("pt") && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var pt)) e.Tam = pt / 0.75;
                                      else if (v.EndsWith("em") && double.TryParse(v[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var em)) e.Tam = em * (b.Tam > 0 ? b.Tam : 0) ; break;
                    case "font-family": e.Fonte = v.Split(',')[0].Trim().Trim('"', '\''); break;
                }
            }
            pilha.Add((tag, e));
        }
        void Desempilhar(string tag)
        {
            for (int i = pilha.Count - 1; i >= 0; i--)
                if (pilha[i].tag == tag || (tag is "strong" or "b" && pilha[i].tag is "b" or "strong")) { pilha.RemoveRange(i, pilha.Count - i); return; }
        }
    }

    static string Atributo(string attrs, string nome)
    {
        foreach (Match a in Attr.Matches(attrs))
            if (a.Groups[1].Value.Equals(nome, StringComparison.OrdinalIgnoreCase))
                return WebUtility.HtmlDecode(a.Groups[2].Value.Trim('"', '\''));
        return "";
    }

    static string AlinhDe(string attrs)
    {
        var m = Regex.Match(Atributo(attrs, "style"), @"text-align\s*:\s*(left|center|right|justify)");
        return m.Success ? m.Groups[1].Value : "";
    }

    /// <summary>Converte cores CSS (#abc, #aabbcc, rgb()) para #AABBCC.</summary>
    public static string Cor(string v)
    {
        v = v.Trim();
        var m = Regex.Match(v, @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)");
        if (m.Success) return $"#{int.Parse(m.Groups[1].Value):X2}{int.Parse(m.Groups[2].Value):X2}{int.Parse(m.Groups[3].Value):X2}";
        if (Regex.IsMatch(v, "^#[0-9a-fA-F]{3}$")) return ("#" + string.Concat(v[1..].Select(c => $"{c}{c}"))).ToUpperInvariant();
        if (Regex.IsMatch(v, "^#[0-9a-fA-F]{6}$")) return v.ToUpperInvariant();
        return v.ToLowerInvariant() switch { "black" => "#000000", "white" => "#FFFFFF", "red" => "#FF0000", "blue" => "#0000FF", "green" => "#008000", _ => "" };
    }

    /// <summary>Monta o HTML do editor a partir de parágrafos (usado ao abrir um .pptx).</summary>
    public static string Html(IEnumerable<Paragrafo> ps)
    {
        var sb = new StringBuilder();
        string? listaAberta = null;
        foreach (var p in ps)
        {
            if (p.Lista != listaAberta)
            {
                if (listaAberta != null) sb.Append($"</{listaAberta}>");
                if (p.Lista != "") sb.Append($"<{p.Lista}>");
                listaAberta = p.Lista == "" ? null : p.Lista;
            }
            var tag = listaAberta != null ? "li" : "p";
            var est = p.Alinh != "" && p.Alinh != "left" ? $" style=\"text-align:{p.Alinh}\"" : "";
            if (listaAberta != null && p.Nivel > 0) est = $" style=\"margin-left:{p.Nivel * 1.5:0.#}em{(p.Alinh is "" or "left" ? "" : $";text-align:{p.Alinh}")}\"";
            sb.Append($"<{tag}{est}>");
            foreach (var t in p.Trechos)
            {
                var css = new List<string>();
                if (t.Cor != "") css.Add($"color:{t.Cor}");
                if (t.Tam > 0) css.Add($"font-size:{t.Tam.ToString("0.#", CultureInfo.InvariantCulture)}px");
                if (t.Fonte != "") css.Add($"font-family:{t.Fonte}");
                var abre = ""; var fecha = "";
                if (css.Count > 0) { abre += $"<span style=\"{string.Join(";", css)}\">"; fecha = "</span>" + fecha; }
                if (t.N) { abre += "<b>"; fecha = "</b>" + fecha; }
                if (t.I) { abre += "<i>"; fecha = "</i>" + fecha; }
                if (t.U) { abre += "<u>"; fecha = "</u>" + fecha; }
                if (t.S) { abre += "<s>"; fecha = "</s>" + fecha; }
                sb.Append(abre).Append(Esc(t.Texto)).Append(fecha);
            }
            if (p.Trechos.Count == 0) sb.Append("<br>");
            sb.Append($"</{tag}>");
        }
        if (listaAberta != null) sb.Append($"</{listaAberta}>");
        return sb.ToString();
    }

    static string Esc(string t) => t.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static bool Vazio(string html) => Ler(html).All(p => p.TextoPuro.Trim() == "");
}
