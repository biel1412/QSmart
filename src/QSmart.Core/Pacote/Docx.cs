using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace QSmart.Core.Pacote;

/// <summary>Smart Write: lê um .docx para HTML do editor e grava um .docx a partir da estrutura do editor.</summary>
public static class Docx
{
    static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    static readonly XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";
    static readonly XNamespace V = "urn:schemas-microsoft-com:vml";

    static string H(string s) => WebUtility.HtmlEncode(s);
    static string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";
    static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    // =====================================================================
    // Leitura: .docx → HTML
    // =====================================================================
    sealed class Leitura
    {
        public ZipArchive Zip = null!;
        public Dictionary<string, (string alvo, bool externo)> Rels = new();
        public Dictionary<string, string> Estilos = new();      // styleId → nome (minúsculo)
        public Dictionary<string, string> EstiloBase = new();   // styleId → basedOn
        public Dictionary<string, XElement> EstiloNum = new();  // styleId → numPr do estilo (ex.: "List Bullet")
        public Dictionary<(string num, int nivel), bool> Numerada = new(); // true = ol, false = ul
        public StringBuilder Sb = new();
        public List<(bool ol, int nivel)> Listas = new();
    }

    public static string ParaHtml(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        ZipArchive zip;
        try { zip = new ZipArchive(ms, ZipArchiveMode.Read); }
        catch { throw new InvalidDataException("O arquivo não é um .docx válido."); }
        using var _ = zip;
        var doc = Xml(zip, "word/document.xml") ?? throw new InvalidDataException("O arquivo não é um documento do Word (.docx).");
        var L = new Leitura { Zip = zip };
        if (Xml(zip, "word/_rels/document.xml.rels") is { } rels)
            foreach (var r in rels.Root!.Elements(PR + "Relationship"))
                L.Rels[(string?)r.Attribute("Id") ?? ""] = ((string?)r.Attribute("Target") ?? "", ((string?)r.Attribute("TargetMode")) == "External");
        if (Xml(zip, "word/styles.xml") is { } st)
            foreach (var s in st.Root!.Elements(W + "style"))
            {
                var id = (string?)s.Attribute(W + "styleId") ?? "";
                L.Estilos[id] = ((string?)s.Element(W + "name")?.Attribute(W + "val") ?? id).ToLowerInvariant();
                if ((string?)s.Element(W + "basedOn")?.Attribute(W + "val") is { } b) L.EstiloBase[id] = b;
                if (s.Element(W + "pPr")?.Element(W + "numPr") is { } np) L.EstiloNum[id] = np;
            }
        if (Xml(zip, "word/numbering.xml") is { } nb)
        {
            var abs = nb.Root!.Elements(W + "abstractNum").ToDictionary(a => (string?)a.Attribute(W + "abstractNumId") ?? "", a => a);
            foreach (var n in nb.Root.Elements(W + "num"))
            {
                var numId = (string?)n.Attribute(W + "numId") ?? "";
                var absId = (string?)n.Element(W + "abstractNumId")?.Attribute(W + "val") ?? "";
                if (!abs.TryGetValue(absId, out var a)) continue;
                foreach (var lvl in a.Elements(W + "lvl"))
                {
                    int il = int.TryParse((string?)lvl.Attribute(W + "ilvl"), out var x) ? x : 0;
                    var fmt = (string?)lvl.Element(W + "numFmt")?.Attribute(W + "val") ?? "bullet";
                    L.Numerada[(numId, il)] = fmt != "bullet" && fmt != "none";
                }
            }
        }
        var body = doc.Root!.Element(W + "body");
        if (body != null) Blocos(L, body.Elements());
        FecharListas(L, -1);
        var html = L.Sb.ToString();
        return html.Length == 0 ? "<p><br></p>" : html;
    }

    static XDocument? Xml(ZipArchive zip, string nome)
    {
        var e = zip.GetEntry(nome);
        if (e == null) return null;
        using var s = e.Open();
        return XDocument.Load(s);
    }

    static void Blocos(Leitura L, IEnumerable<XElement> els)
    {
        foreach (var el in els)
        {
            if (el.Name == W + "p") Paragrafo(L, el);
            else if (el.Name == W + "tbl") { FecharListas(L, -1); Tabela(L, el); }
            else if (el.Name == W + "sdt") Blocos(L, el.Element(W + "sdtContent")?.Elements() ?? Enumerable.Empty<XElement>());
            else if (el.Name == W + "customXml" || el.Name == W + "ins") Blocos(L, el.Elements());
        }
    }

    static void FecharListas(Leitura L, int ateNivel)
    {
        while (L.Listas.Count > 0 && L.Listas[^1].nivel > ateNivel) FecharTopo(L);
    }

    static void FecharTopo(Leitura L)
    {
        var (ol, _) = L.Listas[^1];
        L.Listas.RemoveAt(L.Listas.Count - 1);
        L.Sb.Append(ol ? "</li></ol>" : "</li></ul>");
    }

    static string NomeEstilo(Leitura L, string? id)
    {
        int guarda = 0;
        while (id != null && guarda++ < 10)
        {
            var n = L.Estilos.GetValueOrDefault(id, id.ToLowerInvariant());
            if (n.StartsWith("heading") || n.StartsWith("título") || n.StartsWith("titulo") || n == "title" || n.Contains("quote") || n.Contains("citação")) return n;
            id = L.EstiloBase.GetValueOrDefault(id);
        }
        return "";
    }

    static string TagDoEstilo(string n)
    {
        if (n is "title" or "título" or "titulo") return "h1";
        if (n.Contains("quote") || n.Contains("citação")) return "blockquote";
        foreach (var pre in new[] { "heading ", "título ", "titulo " })
            if (n.StartsWith(pre) && int.TryParse(n[pre.Length..].Trim(), out var nv)) return nv <= 1 ? "h1" : nv == 2 ? "h2" : "h3";
        return "p";
    }

    static void Paragrafo(Leitura L, XElement p)
    {
        var pPr = p.Element(W + "pPr");
        var estilo = NomeEstilo(L, (string?)pPr?.Element(W + "pStyle")?.Attribute(W + "val"));
        var tag = TagDoEstilo(estilo);
        var estiloP = new List<string>();
        switch ((string?)pPr?.Element(W + "jc")?.Attribute(W + "val"))
        {
            case "center": estiloP.Add("text-align:center"); break;
            case "right" or "end": estiloP.Add("text-align:right"); break;
            case "both" or "distribute": estiloP.Add("text-align:justify"); break;
        }

        // conteúdo (runs) + quebra de página antes/depois
        var conteudo = new StringBuilder();
        bool quebraPagina = pPr?.Element(W + "pageBreakBefore") != null;
        bool quebraDepois = false;
        Runs(L, p.Elements(), conteudo, ref quebraDepois);
        if (quebraPagina) { FecharListas(L, -1); L.Sb.Append("<hr class=\"sw-quebra\">"); }

        var numPr = pPr?.Element(W + "numPr");
        if (numPr == null)
        {
            var sid = (string?)pPr?.Element(W + "pStyle")?.Attribute(W + "val"); int g = 0;
            while (sid != null && g++ < 10 && numPr == null) { L.EstiloNum.TryGetValue(sid, out numPr); sid = L.EstiloBase.GetValueOrDefault(sid); }
        }
        var numId = (string?)numPr?.Element(W + "numId")?.Attribute(W + "val");
        if (numId != null && numId != "0")
        {
            int nivel = int.TryParse((string?)numPr!.Element(W + "ilvl")?.Attribute(W + "val"), out var il) ? Math.Clamp(il, 0, 8) : 0;
            bool ol = L.Numerada.GetValueOrDefault((numId, nivel));
            // cada lista aberta na pilha tem um <li> aberto no fim
            while (L.Listas.Count > nivel + 1) FecharTopo(L);
            if (L.Listas.Count == nivel + 1)
            {
                if (L.Listas[^1].ol != ol) FecharTopo(L);
                else L.Sb.Append("</li>");
            }
            while (L.Listas.Count < nivel + 1)
            {
                L.Sb.Append(ol ? "<ol>" : "<ul>");
                L.Listas.Add((ol, L.Listas.Count));
                if (L.Listas.Count < nivel + 1) L.Sb.Append("<li>");
            }
            L.Sb.Append("<li").Append(estiloP.Count > 0 ? $" style=\"{string.Join(";", estiloP)}\"" : "").Append('>')
                .Append(conteudo.Length > 0 ? conteudo.ToString() : "<br>");
        }
        else
        {
            FecharListas(L, -1);
            L.Sb.Append('<').Append(tag).Append(estiloP.Count > 0 ? $" style=\"{string.Join(";", estiloP)}\"" : "").Append('>')
                .Append(conteudo.Length > 0 ? conteudo.ToString() : "<br>").Append("</").Append(tag).Append('>');
        }
        if (quebraDepois) { FecharListas(L, -1); L.Sb.Append("<hr class=\"sw-quebra\">"); }
    }

    static readonly Dictionary<string, string> Realces = new()
    {
        ["yellow"] = "#FFFF00", ["green"] = "#00FF00", ["cyan"] = "#00FFFF", ["magenta"] = "#FF00FF", ["blue"] = "#0000FF", ["red"] = "#FF0000",
        ["darkBlue"] = "#000080", ["darkCyan"] = "#008080", ["darkGreen"] = "#008000", ["darkMagenta"] = "#800080", ["darkRed"] = "#800000",
        ["darkYellow"] = "#808000", ["darkGray"] = "#808080", ["lightGray"] = "#C0C0C0", ["black"] = "#000000", ["white"] = "#FFFFFF",
    };

    static void Runs(Leitura L, IEnumerable<XElement> els, StringBuilder sb, ref bool quebraDepois)
    {
        foreach (var el in els)
        {
            if (el.Name == W + "r") Run(L, el, sb, ref quebraDepois);
            else if (el.Name == W + "hyperlink")
            {
                var id = (string?)el.Attribute(R + "id");
                var href = id != null && L.Rels.TryGetValue(id, out var rr) ? rr.alvo : null;
                var dentro = new StringBuilder();
                Runs(L, el.Elements(), dentro, ref quebraDepois);
                if (href != null && (href.StartsWith("http", StringComparison.OrdinalIgnoreCase) || href.StartsWith("mailto:")))
                    sb.Append("<a href=\"").Append(H(href)).Append("\">").Append(dentro).Append("</a>");
                else sb.Append(dentro);
            }
            else if (el.Name == W + "ins" || el.Name == W + "smartTag" || el.Name == W + "fldSimple" || el.Name == W + "customXml")
                Runs(L, el.Elements(), sb, ref quebraDepois);
            else if (el.Name == W + "sdt") Runs(L, el.Element(W + "sdtContent")?.Elements() ?? Enumerable.Empty<XElement>(), sb, ref quebraDepois);
        }
    }

    static void Run(Leitura L, XElement r, StringBuilder sb, ref bool quebraDepois)
    {
        var rPr = r.Element(W + "rPr");
        bool On(string n) { var e = rPr?.Element(W + n); if (e == null) return false; var v = (string?)e.Attribute(W + "val"); return v == null || (v != "0" && v != "false" && v != "none"); }
        var estilos = new List<string>();
        var cor = (string?)rPr?.Element(W + "color")?.Attribute(W + "val");
        if (cor != null && cor != "auto" && cor.Length == 6) estilos.Add("color:#" + cor);
        var realce = (string?)rPr?.Element(W + "highlight")?.Attribute(W + "val");
        var fundo = realce != null && Realces.TryGetValue(realce, out var hx) ? hx : null;
        var shd = (string?)rPr?.Element(W + "shd")?.Attribute(W + "fill");
        if (fundo == null && shd != null && shd != "auto" && shd.Length == 6) fundo = "#" + shd;
        if (fundo != null) estilos.Add("background-color:" + fundo);
        if (int.TryParse((string?)rPr?.Element(W + "sz")?.Attribute(W + "val"), out var sz) && sz > 0) estilos.Add($"font-size:{N(sz / 2.0)}pt");
        var fonte = (string?)rPr?.Element(W + "rFonts")?.Attribute(W + "ascii");
        if (!string.IsNullOrWhiteSpace(fonte)) estilos.Add($"font-family:'{fonte.Replace("'", "")}'");
        bool b = On("b"), i = On("i"), s = On("strike") || On("dstrike");
        var uVal = (string?)rPr?.Element(W + "u")?.Attribute(W + "val");
        bool u = rPr?.Element(W + "u") != null && uVal != "none";
        var vert = (string?)rPr?.Element(W + "vertAlign")?.Attribute(W + "val");

        var txt = new StringBuilder();
        foreach (var c in r.Elements())
        {
            if (c.Name == W + "t") txt.Append(H(c.Value));
            else if (c.Name == W + "tab") txt.Append("&emsp;");
            else if (c.Name == W + "br")
            {
                if ((string?)c.Attribute(W + "type") == "page") quebraDepois = true;
                else txt.Append("<br>");
            }
            else if (c.Name == W + "cr") txt.Append("<br>");
            else if (c.Name == W + "noBreakHyphen") txt.Append('-');
            else if (c.Name == W + "drawing") txt.Append(Imagem(L, c));
            else if (c.Name == W + "pict")
            {
                var id = (string?)c.Descendants(V + "imagedata").FirstOrDefault()?.Attribute(R + "id");
                if (id != null) txt.Append(ImgTag(L, id, 0, 0));
            }
        }
        if (txt.Length == 0) return;
        var conteudo = txt.ToString();
        if (vert == "superscript") conteudo = $"<sup>{conteudo}</sup>"; else if (vert == "subscript") conteudo = $"<sub>{conteudo}</sub>";
        if (s) conteudo = $"<s>{conteudo}</s>";
        if (u) conteudo = $"<u>{conteudo}</u>";
        if (i) conteudo = $"<i>{conteudo}</i>";
        if (b) conteudo = $"<b>{conteudo}</b>";
        if (estilos.Count > 0) conteudo = $"<span style=\"{string.Join(";", estilos)}\">{conteudo}</span>";
        sb.Append(conteudo);
    }

    static string Imagem(Leitura L, XElement drawing)
    {
        var blip = drawing.Descendants(A + "blip").FirstOrDefault();
        var id = (string?)blip?.Attribute(R + "embed");
        if (id == null) return "";
        var ext = drawing.Descendants(WP + "extent").FirstOrDefault();
        double w = long.TryParse((string?)ext?.Attribute("cx"), out var cx) ? cx / 9525.0 : 0;
        double h = long.TryParse((string?)ext?.Attribute("cy"), out var cy) ? cy / 9525.0 : 0;
        return ImgTag(L, id, w, h);
    }

    static string ImgTag(Leitura L, string id, double w, double h)
    {
        if (!L.Rels.TryGetValue(id, out var rel) || rel.externo) return "";
        var caminho = rel.alvo.StartsWith("/") ? rel.alvo.TrimStart('/') : "word/" + rel.alvo;
        caminho = string.Join('/', caminho.Split('/').Aggregate(new List<string>(), (l, p) => { if (p == "..") { if (l.Count > 0) l.RemoveAt(l.Count - 1); } else l.Add(p); return l; }));
        var e = L.Zip.GetEntry(caminho);
        if (e == null || e.Length > 8_000_000) return "";
        var mime = Path.GetExtension(caminho).ToLowerInvariant() switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp", _ => "" };
        if (mime == "") return "";
        using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m);
        var estilo = w > 0 ? $" style=\"width:{N(Math.Min(w, 640))}px\"" : "";
        return $"<img src=\"data:{mime};base64,{Convert.ToBase64String(m.ToArray())}\"{estilo}>";
    }

    static void Tabela(Leitura L, XElement tbl)
    {
        L.Sb.Append("<table><tbody>");
        foreach (var tr in tbl.Elements(W + "tr"))
        {
            L.Sb.Append("<tr>");
            foreach (var tc in tr.Elements(W + "tc"))
            {
                var tcPr = tc.Element(W + "tcPr");
                var vm = tcPr?.Element(W + "vMerge");
                if (vm != null && (string?)vm.Attribute(W + "val") != "restart") continue;   // célula mesclada na vertical: some
                int span = int.TryParse((string?)tcPr?.Element(W + "gridSpan")?.Attribute(W + "val"), out var g) ? g : 1;
                L.Sb.Append(span > 1 ? $"<td colspan=\"{span}\">" : "<td>");
                var sub = new Leitura { Zip = L.Zip, Rels = L.Rels, Estilos = L.Estilos, EstiloBase = L.EstiloBase, EstiloNum = L.EstiloNum, Numerada = L.Numerada };
                Blocos(sub, tc.Elements());
                FecharListas(sub, -1);
                L.Sb.Append(sub.Sb.Length > 0 ? sub.Sb.ToString() : "<br>");
                L.Sb.Append("</td>");
            }
            L.Sb.Append("</tr>");
        }
        L.Sb.Append("</tbody></table>");
    }

    // =====================================================================
    // Gravação: estrutura do editor (JSON) → .docx
    // =====================================================================
    sealed class Gravacao
    {
        public StringBuilder Doc = new();
        public List<(string id, string alvo, string tipo, bool externo)> Rels = new();
        public List<(string nome, byte[] dados)> Midias = new();
        public List<(int num, bool ol)> Nums = new();
        public Dictionary<string, int> NumPorLista = new();
        public int ProxRel = 10, ProxImg = 1;
    }

    public static byte[] Gravar(JsonNode? blocos, string titulo)
    {
        var G = new Gravacao();
        foreach (var b in blocos as JsonArray ?? new()) Bloco(G, b);
        if (G.Doc.Length == 0) G.Doc.Append("<w:p/>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Put(string nome, string xml) { var e = zip.CreateEntry(nome, CompressionLevel.Optimal); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(xml); }
            var tipos = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">")
                .Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>")
                .Append("<Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/><Default Extension=\"gif\" ContentType=\"image/gif\"/><Default Extension=\"webp\" ContentType=\"image/webp\"/>")
                .Append("<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>")
                .Append("<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>")
                .Append("<Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/>")
                .Append("<Override PartName=\"/word/settings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml\"/>")
                .Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>")
                .Append("<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/></Types>");
            Put("[Content_Types].xml", tipos.ToString());
            Put("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/></Relationships>");
            var agora = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            Put("docProps/core.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                $"<dc:title>{X(titulo)}</dc:title><dc:creator>QSmart · Smart Write</dc:creator><dcterms:created xsi:type=\"dcterms:W3CDTF\">{agora}</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">{agora}</dcterms:modified></cp:coreProperties>");
            Put("docProps/app.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>QSmart Smart Write</Application></Properties>");
            Put("word/settings.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:settings xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:defaultTabStop w:val=\"708\"/><w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"15\"/></w:compat></w:settings>");
            Put("word/styles.xml", Estilos());
            Put("word/numbering.xml", Numeracao(G));
            var rels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">")
                .Append("<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>")
                .Append("<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/>")
                .Append("<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings\" Target=\"settings.xml\"/>");
            foreach (var (id, alvo, tipo, ext) in G.Rels)
                rels.Append($"<Relationship Id=\"{id}\" Type=\"{tipo}\" Target=\"{X(alvo)}\"{(ext ? " TargetMode=\"External\"" : "")}/>");
            rels.Append("</Relationships>");
            Put("word/_rels/document.xml.rels", rels.ToString());
            foreach (var (nome, dados) in G.Midias)
            {
                var e = zip.CreateEntry("word/media/" + nome, CompressionLevel.NoCompression);
                using var s = e.Open(); s.Write(dados);
            }
            Put("word/document.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
                "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><w:body>" +
                G.Doc + "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1418\" w:right=\"1418\" w:bottom=\"1418\" w:left=\"1418\" w:header=\"709\" w:footer=\"709\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>");
        }
        return ms.ToArray();
    }

    static string S(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    static bool B(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    static double D(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;

    static void Bloco(Gravacao G, JsonNode? b)
    {
        switch (S(b, "t"))
        {
            case "quebra": G.Doc.Append("<w:p><w:r><w:br w:type=\"page\"/></w:r></w:p>"); break;
            case "linha": G.Doc.Append("<w:p><w:pPr><w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"1\" w:color=\"BFBFBF\"/></w:pBdr></w:pPr></w:p>"); break;
            case "tabela": Tabela(G, b); break;
            default: Paragrafo(G, b); break;
        }
    }

    static void Paragrafo(Gravacao G, JsonNode? b)
    {
        G.Doc.Append("<w:p><w:pPr>");
        var est = S(b, "s") switch { "h1" => "Ttulo1", "h2" => "Ttulo2", "h3" => "Ttulo3", "citacao" => "Citao", "codigo" => "Codigo", _ => "" };
        var li = b?["li"];
        if (li != null && est == "") est = "PargrafodaLista";
        if (est != "") G.Doc.Append($"<w:pStyle w:val=\"{est}\"/>");
        if (li != null)
        {
            var chave = S(li, "id"); bool ol = S(li, "tipo") == "ol";
            if (!G.NumPorLista.TryGetValue(chave, out var num)) { num = G.Nums.Count + 1; G.Nums.Add((num, ol)); G.NumPorLista[chave] = num; }
            G.Doc.Append($"<w:numPr><w:ilvl w:val=\"{Math.Clamp((int)D(li, "n"), 0, 8)}\"/><w:numId w:val=\"{num}\"/></w:numPr>");
        }
        var al = S(b, "a") switch { "center" => "center", "right" => "right", "justify" => "both", _ => "" };
        if (al != "") G.Doc.Append($"<w:jc w:val=\"{al}\"/>");
        G.Doc.Append("</w:pPr>");
        foreach (var r in b?["r"] as JsonArray ?? new()) Run(G, r);
        G.Doc.Append("</w:p>");
    }

    static string Hex(string cor)
    {
        cor = cor.Trim();
        if (cor.StartsWith("#") && cor.Length == 7) return cor[1..].ToUpperInvariant();
        if (cor.StartsWith("#") && cor.Length == 4) return string.Concat(cor[1..].Select(c => $"{c}{c}")).ToUpperInvariant();
        var m = System.Text.RegularExpressions.Regex.Match(cor, @"rgba?\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)(?:\s*,\s*([\d.]+))?");
        if (m.Success)
        {
            if (m.Groups[4].Success && double.TryParse(m.Groups[4].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var al) && al < 0.05) return "";
            return $"{int.Parse(m.Groups[1].Value):X2}{int.Parse(m.Groups[2].Value):X2}{int.Parse(m.Groups[3].Value):X2}";
        }
        return "";
    }

    static void Run(Gravacao G, JsonNode? r)
    {
        if (r?["img"] is JsonNode img) { Imagem(G, img); return; }
        var href = S(r, "a");
        string? relId = null;
        if (href != "" && (href.StartsWith("http", StringComparison.OrdinalIgnoreCase) || href.StartsWith("mailto:")))
        {
            relId = "rId" + G.ProxRel++;
            G.Rels.Add((relId, href, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink", true));
            G.Doc.Append($"<w:hyperlink r:id=\"{relId}\">");
        }
        G.Doc.Append("<w:r><w:rPr>");
        if (relId != null) G.Doc.Append("<w:rStyle w:val=\"Hyperlink\"/>");
        var fonte = S(r, "fo").Split(',')[0].Trim().Trim('"', '\'');
        if (fonte != "") G.Doc.Append($"<w:rFonts w:ascii=\"{X(fonte)}\" w:hAnsi=\"{X(fonte)}\" w:cs=\"{X(fonte)}\"/>");
        if (B(r, "b")) G.Doc.Append("<w:b/>");
        if (B(r, "i")) G.Doc.Append("<w:i/>");
        if (B(r, "s")) G.Doc.Append("<w:strike/>");
        var cor = Hex(S(r, "c"));
        if (cor != "" && cor != "000000") G.Doc.Append($"<w:color w:val=\"{cor}\"/>");
        var z = D(r, "z");
        if (z > 0) G.Doc.Append($"<w:sz w:val=\"{(int)Math.Round(z * 2)}\"/><w:szCs w:val=\"{(int)Math.Round(z * 2)}\"/>");
        var fundo = Hex(S(r, "f"));
        if (fundo != "" && fundo != "FFFFFF") G.Doc.Append($"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fundo}\"/>");
        if (B(r, "u")) G.Doc.Append("<w:u w:val=\"single\"/>");
        var va = S(r, "v");
        if (va == "sup") G.Doc.Append("<w:vertAlign w:val=\"superscript\"/>"); else if (va == "sub") G.Doc.Append("<w:vertAlign w:val=\"subscript\"/>");
        G.Doc.Append("</w:rPr>");
        if (B(r, "br")) G.Doc.Append("<w:br/>");
        else
        {
            var partes = S(r, "x").Split('\t');
            for (int k = 0; k < partes.Length; k++)
            {
                if (k > 0) G.Doc.Append("<w:tab/>");
                if (partes[k].Length > 0) G.Doc.Append($"<w:t xml:space=\"preserve\">{X(partes[k])}</w:t>");
            }
        }
        G.Doc.Append("</w:r>");
        if (relId != null) G.Doc.Append("</w:hyperlink>");
    }

    static void Imagem(Gravacao G, JsonNode img)
    {
        var d = S(img, "d");
        var m = System.Text.RegularExpressions.Regex.Match(d, @"^data:image/(png|jpe?g|gif|webp);base64,(.+)$", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!m.Success) return;
        byte[] dados;
        try { dados = Convert.FromBase64String(m.Groups[2].Value); } catch { return; }
        var ext = m.Groups[1].Value == "jpg" ? "jpeg" : m.Groups[1].Value;
        var nome = $"imagem{G.ProxImg}.{ext}";
        var relId = "rId" + G.ProxRel++;
        G.Midias.Add((nome, dados));
        G.Rels.Add((relId, "media/" + nome, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", false));
        double w = D(img, "w"), h = D(img, "h");
        if (w <= 0 || h <= 0) { w = 400; h = 300; }
        if (w > 604) { h = h * 604 / w; w = 604; }     // largura útil da página A4
        long cx = (long)(w * 9525), cy = (long)(h * 9525);
        int id = G.ProxImg++;
        G.Doc.Append("<w:r><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">")
            .Append($"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:docPr id=\"{id}\" name=\"Imagem {id}\"/>")
            .Append("<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>")
            .Append("<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:pic>")
            .Append($"<pic:nvPicPr><pic:cNvPr id=\"{id}\" name=\"{nome}\"/><pic:cNvPicPr/></pic:nvPicPr>")
            .Append($"<pic:blipFill><a:blip r:embed=\"{relId}\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>")
            .Append($"<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr>")
            .Append("</pic:pic></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>");
    }

    static void Tabela(Gravacao G, JsonNode? b)
    {
        var linhas = b?["l"] as JsonArray ?? new();
        int cols = linhas.Select(l => (l as JsonArray ?? new()).Sum(c => Math.Max(1, (int)D(c, "span")))).DefaultIfEmpty(1).Max();
        int larg = 9070 / Math.Max(1, cols);
        G.Doc.Append("<w:tbl><w:tblPr><w:tblStyle w:val=\"Tabelacomgrade\"/><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"1\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/></w:tblPr><w:tblGrid>");
        for (int c = 0; c < cols; c++) G.Doc.Append($"<w:gridCol w:w=\"{larg}\"/>");
        G.Doc.Append("</w:tblGrid>");
        foreach (var l in linhas)
        {
            G.Doc.Append("<w:tr>");
            foreach (var c in l as JsonArray ?? new())
            {
                int span = Math.Max(1, (int)D(c, "span"));
                G.Doc.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{larg * span}\" w:type=\"dxa\"/>{(span > 1 ? $"<w:gridSpan w:val=\"{span}\"/>" : "")}</w:tcPr>");
                var bs = c?["b"] as JsonArray;
                int antes = G.Doc.Length;
                foreach (var x in bs ?? new()) { if (S(x, "t") == "tabela") continue; Bloco(G, x); }
                if (G.Doc.Length == antes) G.Doc.Append("<w:p/>");
                G.Doc.Append("</w:tc>");
            }
            G.Doc.Append("</w:tr>");
        }
        G.Doc.Append("</w:tbl><w:p/>");
    }

    static string Numeracao(Gravacao G)
    {
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:numbering xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">");
        string[] marcas = { "•", "◦", "▪" };
        sb.Append("<w:abstractNum w:abstractNumId=\"0\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
        for (int i = 0; i < 9; i++)
            sb.Append($"<w:lvl w:ilvl=\"{i}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"{marcas[i % 3]}\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"{720 + 360 * i}\" w:hanging=\"360\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\"/></w:rPr></w:lvl>");
        sb.Append("</w:abstractNum><w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"hybridMultilevel\"/>");
        string[] fmts = { "decimal", "lowerLetter", "lowerRoman" };
        for (int i = 0; i < 9; i++)
            sb.Append($"<w:lvl w:ilvl=\"{i}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"{fmts[i % 3]}\"/><w:lvlText w:val=\"%{i + 1}.\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:ind w:left=\"{720 + 360 * i}\" w:hanging=\"360\"/></w:pPr></w:lvl>");
        sb.Append("</w:abstractNum>");
        foreach (var (num, ol) in G.Nums)
            sb.Append($"<w:num w:numId=\"{num}\"><w:abstractNumId w:val=\"{(ol ? 1 : 0)}\"/>{(ol ? "<w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"1\"/></w:lvlOverride>" : "")}</w:num>");
        sb.Append("</w:numbering>");
        return sb.ToString();
    }

    static string Estilos() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:styles xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\">" +
        "<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Calibri\" w:hAnsi=\"Calibri\" w:eastAsia=\"Calibri\" w:cs=\"Calibri\"/><w:sz w:val=\"22\"/><w:szCs w:val=\"22\"/><w:lang w:val=\"pt-BR\"/></w:rPr></w:rPrDefault>" +
        "<w:pPrDefault><w:pPr><w:spacing w:after=\"160\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>" +
        "<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Ttulo1\"><w:name w:val=\"heading 1\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before=\"360\" w:after=\"120\"/><w:outlineLvl w:val=\"0\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"1F3864\"/><w:sz w:val=\"36\"/><w:szCs w:val=\"36\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Ttulo2\"><w:name w:val=\"heading 2\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before=\"240\" w:after=\"80\"/><w:outlineLvl w:val=\"1\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"2F5496\"/><w:sz w:val=\"28\"/><w:szCs w:val=\"28\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Ttulo3\"><w:name w:val=\"heading 3\"/><w:basedOn w:val=\"Normal\"/><w:next w:val=\"Normal\"/><w:qFormat/><w:pPr><w:keepNext/><w:spacing w:before=\"200\" w:after=\"60\"/><w:outlineLvl w:val=\"2\"/></w:pPr><w:rPr><w:b/><w:color w:val=\"404040\"/><w:sz w:val=\"24\"/><w:szCs w:val=\"24\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Citao\"><w:name w:val=\"Quote\"/><w:basedOn w:val=\"Normal\"/><w:qFormat/><w:pPr><w:ind w:left=\"567\" w:right=\"567\"/><w:pBdr><w:left w:val=\"single\" w:sz=\"18\" w:space=\"8\" w:color=\"2563EB\"/></w:pBdr></w:pPr><w:rPr><w:i/><w:color w:val=\"404040\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"Codigo\"><w:name w:val=\"Código\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"F1F5F9\"/><w:spacing w:after=\"0\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"Consolas\" w:hAnsi=\"Consolas\"/><w:sz w:val=\"20\"/></w:rPr></w:style>" +
        "<w:style w:type=\"paragraph\" w:styleId=\"PargrafodaLista\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Normal\"/><w:qFormat/><w:pPr><w:spacing w:after=\"60\"/><w:ind w:left=\"720\"/><w:contextualSpacing/></w:pPr></w:style>" +
        "<w:style w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/><w:rPr><w:color w:val=\"2563EB\"/><w:u w:val=\"single\"/></w:rPr></w:style>" +
        "<w:style w:type=\"table\" w:styleId=\"Tabelacomgrade\"><w:name w:val=\"Table Grid\"/><w:tblPr><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/><w:left w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/><w:right w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/></w:tblBorders><w:tblCellMar><w:left w:w=\"108\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr><w:pPr><w:spacing w:after=\"0\"/></w:pPr></w:style>" +
        "</w:styles>";
}
