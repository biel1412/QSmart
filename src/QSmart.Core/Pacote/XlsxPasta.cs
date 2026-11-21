using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace QSmart.Core.Pacote;

/// <summary>Smart Math: abre e grava .xlsx (valores, fórmulas, formatação, larguras, filtro e gráficos).</summary>
public static class XlsxPasta
{
    static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";
    static readonly XNamespace CP = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
    static readonly XNamespace VT = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";
    static string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";
    static string Inv(double d) => d.ToString("R", CultureInfo.InvariantCulture);

    // ================================================================= dados de um gráfico (comum à tela e ao .xlsx)
    public sealed record DadosGrafico(List<string> Categorias, List<(string nome, List<double?> v)> Series,
        bool Cabecalho, bool ColCategoria, int R1, int C1, int R2, int C2);

    public static DadosGrafico? Dados(Formula.Calc calc, Folha f, string faixa)
    {
        if (!Ender.LerFaixa(faixa, out var r1, out var c1, out var r2, out var c2)) return null;
        r2 = Math.Min(r2, Math.Max(r1, f.UltLinha)); c2 = Math.Min(c2, Math.Max(c1, f.UltColuna));
        object? V(int r, int c) => calc.Valor(f, r, c);
        // cabeçalho: primeira linha sem números (fora a 1ª coluna)
        bool cab = r2 > r1 && Enumerable.Range(c1, c2 - c1 + 1).All(c => V(r1, c) is not double) && Enumerable.Range(c1, c2 - c1 + 1).Any(c => V(r1, c) is string s && s.Length > 0);
        int d0 = cab ? r1 + 1 : r1;
        // coluna de categorias: primeira coluna com textos (ou mais de uma coluna e a primeira não é número)
        bool colCat = c2 > c1 && Enumerable.Range(d0, r2 - d0 + 1).Any(r => V(r, c1) is string or bool) ||
                      (c2 > c1 && Enumerable.Range(d0, r2 - d0 + 1).All(r => V(r, c1) is not double));
        var cats = new List<string>();
        for (int r = d0; r <= r2; r++) cats.Add(colCat ? FormatoPt.Exibir(V(r, c1), f.Get(r, c1)?.E ?? new EstiloCel()) : (r - d0 + 1).ToString());
        var series = new List<(string, List<double?>)>();
        for (int c = colCat ? c1 + 1 : c1; c <= c2; c++)
        {
            var nome = cab ? Formula.Txt(V(r1, c)) : $"Série {Ender.Col(c)}";
            var l = new List<double?>();
            for (int r = d0; r <= r2; r++) l.Add(V(r, c) is double d ? d : null);
            series.Add((nome, l));
        }
        if (series.Count == 0 || cats.Count == 0) return null;
        return new DadosGrafico(cats, series, cab, colCat, r1, c1, r2, c2);
    }

    // ================================================================= leitura
    public static Pasta Ler(byte[] dados)
    {
        using var ms = new MemoryStream(dados);
        ZipArchive zip;
        try { zip = new ZipArchive(ms, ZipArchiveMode.Read); } catch { throw new InvalidDataException("O arquivo não é um .xlsx válido."); }
        using var _ = zip;
        XDocument? Xml(string nome) { var e = zip.GetEntry(nome); if (e == null) return null; using var s = e.Open(); return XDocument.Load(s); }
        var wb = Xml("xl/workbook.xml") ?? throw new InvalidDataException("O arquivo não é uma planilha do Excel (.xlsx).");
        var rels = new Dictionary<string, string>();
        if (Xml("xl/_rels/workbook.xml.rels") is { } wr)
            foreach (var r in wr.Root!.Elements(PR + "Relationship")) rels[(string?)r.Attribute("Id") ?? ""] = (string?)r.Attribute("Target") ?? "";

        var textos = new List<string>();
        if (Xml("xl/sharedStrings.xml") is { } ss)
            foreach (var si in ss.Root!.Elements(S + "si")) textos.Add(string.Concat(si.Descendants(S + "t").Where(t => t.Parent?.Name != S + "rPh").Select(t => t.Value)));

        var estilos = LerEstilos(Xml("xl/styles.xml"));
        var pasta = new Pasta { Folhas = new() };
        foreach (var sh in wb.Root!.Element(S + "sheets")?.Elements(S + "sheet") ?? Enumerable.Empty<XElement>())
        {
            var nome = (string?)sh.Attribute("name") ?? $"Planilha{pasta.Folhas.Count + 1}";
            var rid = (string?)sh.Attribute(R + "id") ?? "";
            if (!rels.TryGetValue(rid, out var alvo)) continue;
            var caminho = alvo.StartsWith("/") ? alvo.TrimStart('/') : "xl/" + alvo;
            var doc = Xml(caminho);
            if (doc == null) continue;
            var f = new Folha { Nome = nome };
            LerFolha(doc, f, textos, estilos);
            pasta.Folhas.Add(f);
        }
        if (pasta.Folhas.Count == 0) pasta.Folhas.Add(new Folha());

        // gráficos do QSmart guardados no próprio arquivo
        if (Xml("docProps/custom.xml") is { } cust)
            foreach (var p in cust.Root!.Elements(CP + "property"))
                if ((string?)p.Attribute("name") == "QSmartGraficos")
                {
                    try
                    {
                        var j = JsonNode.Parse(p.Value) as JsonObject;
                        foreach (var (folha, lista) in j ?? new JsonObject())
                            if (pasta.Folha(folha) is { } fo)
                                foreach (var g in lista as JsonArray ?? new())
                                    fo.Graficos.Add(new Grafico { Tipo = g?["tipo"]?.GetValue<string>() ?? "colunas", Faixa = g?["faixa"]?.GetValue<string>() ?? "", Titulo = g?["titulo"]?.GetValue<string>() ?? "" });
                    }
                    catch { }
                }
        return pasta;
    }

    static List<EstiloCel> LerEstilos(XDocument? st)
    {
        var l = new List<EstiloCel>();
        if (st == null) return l;
        var raiz = st.Root!;
        var fmts = new Dictionary<int, string>();
        foreach (var n in raiz.Element(S + "numFmts")?.Elements(S + "numFmt") ?? Enumerable.Empty<XElement>())
            fmts[(int?)n.Attribute("numFmtId") ?? 0] = (string?)n.Attribute("formatCode") ?? "";
        var fontes = (raiz.Element(S + "fonts")?.Elements(S + "font") ?? Enumerable.Empty<XElement>()).Select(fo => (
            b: fo.Element(S + "b") is { } b && (string?)b.Attribute("val") is null or "1" or "true",
            i: fo.Element(S + "i") is { } i && (string?)i.Attribute("val") is null or "1" or "true",
            s: fo.Element(S + "strike") != null,
            cor: Cor((string?)fo.Element(S + "color")?.Attribute("rgb")))).ToList();
        var fundos = (raiz.Element(S + "fills")?.Elements(S + "fill") ?? Enumerable.Empty<XElement>()).Select(fi =>
        {
            var p = fi.Element(S + "patternFill");
            return (string?)p?.Attribute("patternType") == "solid" ? Cor((string?)p!.Element(S + "fgColor")?.Attribute("rgb")) : "";
        }).ToList();
        foreach (var xf in raiz.Element(S + "cellXfs")?.Elements(S + "xf") ?? Enumerable.Empty<XElement>())
        {
            var e = new EstiloCel();
            int fid = (int?)xf.Attribute("fontId") ?? 0, flid = (int?)xf.Attribute("fillId") ?? 0, nf = (int?)xf.Attribute("numFmtId") ?? 0;
            if (fid < fontes.Count) { var fo = fontes[fid]; e.N = fo.b; e.I = fo.i; e.S = fo.s; e.Cor = fo.cor is "#000000" ? "" : fo.cor; }
            if (flid < fundos.Count) e.Fundo = fundos[flid];
            var h = (string?)xf.Element(S + "alignment")?.Attribute("horizontal");
            e.Alinh = h switch { "center" => "centro", "right" => "dir", "left" => "esq", _ => "" };
            (e.Formato, e.Casas) = DoFormato(nf, fmts.GetValueOrDefault(nf, ""));
            l.Add(e);
        }
        return l;
    }

    static string Cor(string? argb) => argb is { Length: 8 } ? "#" + argb[2..] : argb is { Length: 6 } ? "#" + argb : "";

    static (string fmt, int casas) DoFormato(int id, string codigo)
    {
        switch (id)
        {
            case 0: return ("geral", 2);
            case 1: case 3: return ("int", 0);
            case 2: case 4: return ("num", 2);
            case 9: return ("pct", 0);
            case 10: return ("pct", 2);
            case >= 14 and <= 22: return ("data", 0);
            case 44: case 7: case 8: case 5: case 6: return ("moeda", 2);
            case 49: return ("texto", 0);
        }
        if (codigo.Length == 0) return ("geral", 2);
        var semAspas = System.Text.RegularExpressions.Regex.Replace(codigo, "\"[^\"]*\"|\\[[^\\]]*\\]", "");
        int casas = semAspas.Contains('.') ? semAspas[(semAspas.IndexOf('.') + 1)..].TakeWhile(ch => ch == '0').Count() : 0;
        if (codigo.Contains("R$") || codigo.Contains('$') || codigo.Contains("[$")) return ("moeda", casas);
        if (semAspas.Contains('%')) return ("pct", casas);
        if (System.Text.RegularExpressions.Regex.IsMatch(semAspas, "[dmyDMY]")) return ("data", 0);
        if (semAspas == "@") return ("texto", 0);
        if (semAspas.Contains('0') || semAspas.Contains('#')) return (casas == 0 ? "int" : "num", casas);
        return ("geral", 2);
    }

    static void LerFolha(XDocument doc, Folha f, List<string> textos, List<EstiloCel> estilos)
    {
        var raiz = doc.Root!;
        foreach (var col in raiz.Element(S + "cols")?.Elements(S + "col") ?? Enumerable.Empty<XElement>())
        {
            int min = (int?)col.Attribute("min") ?? 1, max = Math.Min((int?)col.Attribute("max") ?? min, min + 200);
            if (double.TryParse((string?)col.Attribute("width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                for (int c = min; c <= max; c++) f.Larg[c - 1] = Math.Round(Math.Clamp(w * 7 + 5, 24, 600));
        }
        var compart = new Dictionary<string, (string formula, int r, int c)>();
        foreach (var row in raiz.Element(S + "sheetData")?.Elements(S + "row") ?? Enumerable.Empty<XElement>())
            foreach (var c in row.Elements(S + "c"))
            {
                if (!Ender.Ler((string?)c.Attribute("r") ?? "", out var r, out var col)) continue;
                var tipo = (string?)c.Attribute("t") ?? "n";
                int si = (int?)c.Attribute("s") ?? 0;
                var est = si < estilos.Count ? estilos[si].Clonar() : new EstiloCel();
                var fe = c.Element(S + "f");
                var v = (string?)c.Element(S + "v");
                string entrada = "";
                if (fe != null)
                {
                    var txt = fe.Value;
                    if ((string?)fe.Attribute("t") == "shared" && (string?)fe.Attribute("si") is { } sid)
                    {
                        if (txt.Length > 0) compart[sid] = (Formula.ParaPortugues(txt), r, col);
                        else if (compart.TryGetValue(sid, out var m)) { entrada = "=" + Formula.Deslocar(m.formula, r - m.r, col - m.c); goto pronto; }
                    }
                    if (txt.Length > 0) entrada = "=" + Formula.ParaPortugues(txt);
                }
                if (entrada == "")
                {
                    switch (tipo)
                    {
                        case "s": entrada = int.TryParse(v, out var i) && i < textos.Count ? TextoComoEntrada(textos[i]) : ""; break;
                        case "inlineStr": entrada = TextoComoEntrada(string.Concat(c.Descendants(S + "t").Select(t => t.Value))); break;
                        case "str": entrada = TextoComoEntrada(v ?? ""); break;
                        case "b": entrada = v == "1" ? "VERDADEIRO" : "FALSO"; break;
                        case "e": entrada = v ?? ""; break;
                        default:
                            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                                entrada = est.Formato == "data" ? DateTime.FromOADate(d).ToString(DateTime.FromOADate(d).TimeOfDay.TotalSeconds > 0.5 ? "dd/MM/yyyy HH:mm" : "dd/MM/yyyy", CultureInfo.InvariantCulture)
                                        : est.Formato == "pct" ? FormatoPt.Geral(d * 100) + "%"
                                        : FormatoPt.Geral(d);
                            break;
                    }
                }
                pronto:
                if (entrada == "" && est.Padrao) continue;
                var cel = f.Pegar(r, col);
                cel.Entrada = entrada; cel.E = est;
            }
        if ((string?)raiz.Element(S + "autoFilter")?.Attribute("ref") is { } af && Ender.LerFaixa(af, out var a1, out var b1, out _, out var b2))
            f.Filtro = new Filtro { Linha = a1, C1 = b1, C2 = b2 };
    }

    /// <summary>Texto lido de uma célula: se parecer número ou fórmula, recebe ' na frente para continuar texto.</summary>
    static string TextoComoEntrada(string s)
    {
        if (s.Length == 0) return "";
        if (s.StartsWith("=") || s.StartsWith("'")) return "'" + s;
        var v = FormatoPt.Valor(s, out _);
        return v is string ? s : "'" + s;
    }

    // ================================================================= gravação
    public static byte[] Gravar(Pasta p)
    {
        var calc = new Formula.Calc(p);
        // estilos únicos
        var xfs = new List<EstiloCel> { new() };
        var idx = new Dictionary<string, int> { [new EstiloCel().Chave] = 0 };
        int Xf(EstiloCel e) { var k = e.Chave; if (!idx.TryGetValue(k, out var i)) { i = xfs.Count; xfs.Add(e); idx[k] = i; } return i; }

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Put(string nome, string xml) { var e = zip.CreateEntry(nome, CompressionLevel.Optimal); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(xml); }
            var tipos = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
                "<Override PartName=\"/docProps/custom.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.custom-properties+xml\"/>");
            var wbRels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            var sheets = new StringBuilder();
            var nomesDef = new StringBuilder();
            int chartN = 0;
            var graficosJson = new JsonObject();
            for (int i = 0; i < p.Folhas.Count; i++)
            {
                var f = p.Folhas[i];
                var sx = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
                int ul = Math.Max(0, f.UltLinha), uc = Math.Max(0, f.UltColuna);
                sx.Append($"<dimension ref=\"A1:{Ender.Cel(ul, uc)}\"/>");
                sx.Append(i == 0 ? "<sheetViews><sheetView workbookViewId=\"0\" tabSelected=\"1\"/></sheetViews>" : "<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>");
                sx.Append("<sheetFormatPr defaultRowHeight=\"15\"/>");
                if (f.Larg.Count > 0)
                {
                    sx.Append("<cols>");
                    foreach (var (c, w) in f.Larg.OrderBy(x => x.Key)) sx.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{Inv(Math.Round(Math.Max(1, (w - 5) / 7.0), 2))}\" customWidth=\"1\"/>");
                    sx.Append("</cols>");
                }
                sx.Append("<sheetData>");
                foreach (var linha in f.Cel.Where(x => x.Value.Entrada != "" || !x.Value.E.Padrao).GroupBy(x => Folha.DeK(x.Key).r).OrderBy(g => g.Key))
                {
                    sx.Append($"<row r=\"{linha.Key + 1}\">");
                    foreach (var (k, cel) in linha.OrderBy(x => Folha.DeK(x.Key).c))
                    {
                        var (r, c) = Folha.DeK(k);
                        var e = cel.E.Clonar();
                        var ent = cel.Entrada;
                        var refc = Ender.Cel(r, c);
                        if (ent.StartsWith("=") && ent.Length > 1)
                        {
                            var v = calc.Valor(f, r, c);
                            var fen = X(Formula.ParaIngles(ent[1..]));
                            var (t, vv) = v switch
                            {
                                double d => ("", $"<v>{Inv(d)}</v>"),
                                bool b => (" t=\"b\"", $"<v>{(b ? 1 : 0)}</v>"),
                                ErroF er => (" t=\"e\"", $"<v>{X(ErroEn(er.Cod))}</v>"),
                                null => ("", ""),
                                _ => (" t=\"str\"", $"<v>{X(Formula.Txt(v))}</v>"),
                            };
                            sx.Append($"<c r=\"{refc}\" s=\"{Xf(e)}\"{t}><f>{fen}</f>{vv}</c>");
                            continue;
                        }
                        if (ent == "") { sx.Append($"<c r=\"{refc}\" s=\"{Xf(e)}\"/>"); continue; }
                        var val = e.Formato == "texto" ? ent : FormatoPt.Valor(ent, out var sug);
                        if (e.Formato == "geral" && e.Formato != "texto" && FormatoPt.Valor(ent, out var sug2) is double && sug2 != "") { e.Formato = sug2; }
                        switch (val)
                        {
                            case double d: sx.Append($"<c r=\"{refc}\" s=\"{Xf(e)}\"><v>{Inv(d)}</v></c>"); break;
                            case bool b: sx.Append($"<c r=\"{refc}\" s=\"{Xf(e)}\" t=\"b\"><v>{(b ? 1 : 0)}</v></c>"); break;
                            default: sx.Append($"<c r=\"{refc}\" s=\"{Xf(e)}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{X(Formula.Txt(val))}</t></is></c>"); break;
                        }
                    }
                    sx.Append("</row>");
                }
                sx.Append("</sheetData>");
                if (f.Filtro is { } fl)
                {
                    int ultF = Math.Max(fl.Linha + 1, f.UltLinha);
                    var refF = $"{Ender.Cel(fl.Linha, fl.C1)}:{Ender.Cel(ultF, fl.C2)}";
                    sx.Append($"<autoFilter ref=\"{refF}\"/>");
                    nomesDef.Append($"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{i}\" hidden=\"1\">{X(RefFolha(f.Nome))}!{AbsRef(refF)}</definedName>");
                }
                sx.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/>");

                // gráficos de verdade no Excel (e a definição guardada para o QSmart reabrir igual)
                var validos = f.Graficos.Select(g => (g, d: Dados(calc, f, g.Faixa))).Where(x => x.d != null).ToList();
                if (validos.Count > 0)
                {
                    int dn = i + 1;
                    sx.Append("<drawing r:id=\"rId1\"/>");
                    Put($"xl/worksheets/_rels/sheet{i + 1}.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                        $"<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing{dn}.xml\"/></Relationships>");
                    var dr = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">");
                    var drRels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                    int colG = uc + 2;
                    for (int k = 0; k < validos.Count; k++)
                    {
                        var (g, d) = validos[k];
                        chartN++;
                        int top = k * 18;
                        dr.Append($"<xdr:twoCellAnchor><xdr:from><xdr:col>{colG}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{top}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>")
                          .Append($"<xdr:to><xdr:col>{colG + 8}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{top + 16}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>")
                          .Append($"<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"{k + 2}\" name=\"Gráfico {k + 1}\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>")
                          .Append("<xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">")
                          .Append($"<c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" r:id=\"rId{k + 1}\"/>")
                          .Append("</a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>");
                        drRels.Append($"<Relationship Id=\"rId{k + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart\" Target=\"../charts/chart{chartN}.xml\"/>");
                        Put($"xl/charts/chart{chartN}.xml", GraficoXml(f.Nome, g, d!));
                        tipos.Append($"<Override PartName=\"/xl/charts/chart{chartN}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>");
                    }
                    dr.Append("</xdr:wsDr>"); drRels.Append("</Relationships>");
                    Put($"xl/drawings/drawing{dn}.xml", dr.ToString());
                    Put($"xl/drawings/_rels/drawing{dn}.xml.rels", drRels.ToString());
                    tipos.Append($"<Override PartName=\"/xl/drawings/drawing{dn}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>");
                }
                if (f.Graficos.Count > 0)
                    graficosJson[f.Nome] = new JsonArray(f.Graficos.Select(g => (JsonNode)new JsonObject { ["tipo"] = g.Tipo, ["faixa"] = g.Faixa, ["titulo"] = g.Titulo }).ToArray());
                sx.Append("</worksheet>");
                Put($"xl/worksheets/sheet{i + 1}.xml", sx.ToString());
                tipos.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                wbRels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
                sheets.Append($"<sheet name=\"{X(NomeFolhaExcel(f.Nome))}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
            }
            wbRels.Append($"<Relationship Id=\"rId{p.Folhas.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Put("xl/_rels/workbook.xml.rels", wbRels.ToString());
            Put("xl/workbook.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                $"<bookViews><workbookView/></bookViews><sheets>{sheets}</sheets>{(nomesDef.Length > 0 ? $"<definedNames>{nomesDef}</definedNames>" : "")}<calcPr calcId=\"191029\" fullCalcOnLoad=\"1\"/></workbook>");
            Put("xl/styles.xml", EstilosXml(xfs));
            Put("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>" +
                "<Relationship Id=\"rId4\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties\" Target=\"docProps/custom.xml\"/></Relationships>");
            var agora = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            Put("docProps/core.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                $"<dc:creator>QSmart · Smart Math</dc:creator><dcterms:created xsi:type=\"dcterms:W3CDTF\">{agora}</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">{agora}</dcterms:modified></cp:coreProperties>");
            Put("docProps/app.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>QSmart Smart Math</Application></Properties>");
            Put("docProps/custom.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
                $"<property fmtid=\"{{D5CDD505-2E9C-101B-9397-08002B2CF9AE}}\" pid=\"2\" name=\"QSmartGraficos\"><vt:lpwstr>{X(graficosJson.ToJsonString())}</vt:lpwstr></property></Properties>");
            tipos.Append("</Types>");
            Put("[Content_Types].xml", tipos.ToString());
        }
        return ms.ToArray();
    }

    static string ErroEn(string cod) => cod switch { "#VALOR!" => "#VALUE!", "#NOME?" => "#NAME?", "#N/D" => "#N/A", "#NÚM!" => "#NUM!", "#CIRC!" => "#VALUE!", _ => cod };
    static string NomeFolhaExcel(string n) { var s = string.Concat(n.Where(ch => !"[]:*?/\\".Contains(ch))).Trim(); return (s.Length == 0 ? "Planilha" : s)[..Math.Min(31, Math.Max(1, s.Length))]; }
    static string RefFolha(string n) => "'" + NomeFolhaExcel(n).Replace("'", "''") + "'";
    static string AbsRef(string faixa) => string.Join(':', faixa.Split(':').Select(p => { Ender.Ler(p, out var r, out var c); return $"${Ender.Col(c)}${r + 1}"; }));

    static string FormatoCodigo(EstiloCel e)
    {
        var dec = e.Casas > 0 ? "." + new string('0', e.Casas) : "";
        return e.Formato switch
        {
            "num" => "#,##0" + dec, "int" => "#,##0", "moeda" => "\"R$\" #,##0" + dec, "pct" => "0" + dec + "%", "data" => "dd/mm/yyyy", "texto" => "@", _ => "",
        };
    }

    static string EstilosXml(List<EstiloCel> xfs)
    {
        var fmts = new List<string>(); var fontes = new List<string> { "<font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font>" };
        var fundos = new List<string> { "<fill><patternFill patternType=\"none\"/></fill>", "<fill><patternFill patternType=\"gray125\"/></fill>" };
        var cell = new StringBuilder();
        foreach (var e in xfs)
        {
            int numId = 0;
            var cod = FormatoCodigo(e);
            if (cod == "@") numId = 49;
            else if (cod != "") { int k = fmts.IndexOf(cod); if (k < 0) { fmts.Add(cod); k = fmts.Count - 1; } numId = 164 + k; }
            var fo = "<font>" + (e.N ? "<b/>" : "") + (e.I ? "<i/>" : "") + (e.S ? "<strike/>" : "") + "<sz val=\"11\"/>" +
                     (e.Cor.Length == 7 ? $"<color rgb=\"FF{e.Cor[1..].ToUpperInvariant()}\"/>" : "") + "<name val=\"Calibri\"/><family val=\"2\"/></font>";
            int fid = fontes.IndexOf(fo); if (fid < 0) { fontes.Add(fo); fid = fontes.Count - 1; }
            int flid = 0;
            if (e.Fundo.Length == 7)
            {
                var fl = $"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF{e.Fundo[1..].ToUpperInvariant()}\"/><bgColor indexed=\"64\"/></patternFill></fill>";
                flid = fundos.IndexOf(fl); if (flid < 0) { fundos.Add(fl); flid = fundos.Count - 1; }
            }
            var al = e.Alinh switch { "centro" => "center", "dir" => "right", "esq" => "left", _ => "" };
            cell.Append($"<xf numFmtId=\"{numId}\" fontId=\"{fid}\" fillId=\"{flid}\" borderId=\"0\" xfId=\"0\"{(numId > 0 ? " applyNumberFormat=\"1\"" : "")}{(fid > 0 ? " applyFont=\"1\"" : "")}{(flid > 0 ? " applyFill=\"1\"" : "")}")
                .Append(al != "" ? $" applyAlignment=\"1\"><alignment horizontal=\"{al}\"/></xf>" : "/>");
        }
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        if (fmts.Count > 0) { sb.Append($"<numFmts count=\"{fmts.Count}\">"); for (int i = 0; i < fmts.Count; i++) sb.Append($"<numFmt numFmtId=\"{164 + i}\" formatCode=\"{X(fmts[i])}\"/>"); sb.Append("</numFmts>"); }
        sb.Append($"<fonts count=\"{fontes.Count}\">").Append(string.Concat(fontes)).Append("</fonts>");
        sb.Append($"<fills count=\"{fundos.Count}\">").Append(string.Concat(fundos)).Append("</fills>");
        sb.Append("<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>");
        sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        sb.Append($"<cellXfs count=\"{xfs.Count}\">").Append(cell).Append("</cellXfs>");
        sb.Append("<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
        return sb.ToString();
    }

    static readonly string[] CoresSerie = { "2563EB", "10B981", "F59E0B", "EF4444", "8B5CF6", "EC4899", "14B8A6", "F97316" };

    static string GraficoXml(string folha, Grafico g, DadosGrafico d)
    {
        var rf = RefFolha(folha);
        string Abs(int r, int c) => $"${Ender.Col(c)}${r + 1}";
        int d0 = d.Cabecalho ? d.R1 + 1 : d.R1;
        var catRef = d.ColCategoria ? $"{rf}!{Abs(d0, d.C1)}:{Abs(d.R2, d.C1)}" : null;
        var ser = new StringBuilder();
        int cIni = d.ColCategoria ? d.C1 + 1 : d.C1;
        int nSeries = g.Tipo == "pizza" ? 1 : d.Series.Count;
        for (int k = 0; k < nSeries; k++)
        {
            int c = cIni + k;
            var cor = CoresSerie[k % CoresSerie.Length];
            ser.Append($"<c:ser><c:idx val=\"{k}\"/><c:order val=\"{k}\"/>");
            if (d.Cabecalho) ser.Append($"<c:tx><c:strRef><c:f>{X(rf)}!{Abs(d.R1, c)}</c:f></c:strRef></c:tx>");
            if (g.Tipo == "linhas") ser.Append($"<c:spPr><a:ln w=\"28575\" cap=\"rnd\"><a:solidFill><a:srgbClr val=\"{cor}\"/></a:solidFill><a:round/></a:ln></c:spPr><c:marker><c:symbol val=\"circle\"/><c:size val=\"6\"/></c:marker>");
            else if (g.Tipo != "pizza") ser.Append($"<c:spPr><a:solidFill><a:srgbClr val=\"{cor}\"/></a:solidFill></c:spPr><c:invertIfNegative val=\"0\"/>");
            if (g.Tipo == "pizza")
                for (int p = 0; p < d.Categorias.Count; p++)
                    ser.Append($"<c:dPt><c:idx val=\"{p}\"/><c:bubble3D val=\"0\"/><c:spPr><a:solidFill><a:srgbClr val=\"{CoresSerie[p % CoresSerie.Length]}\"/></a:solidFill></c:spPr></c:dPt>");
            if (catRef != null) ser.Append($"<c:cat><c:strRef><c:f>{X(catRef)}</c:f></c:strRef></c:cat>");
            ser.Append($"<c:val><c:numRef><c:f>{X(rf)}!{Abs(d0, c)}:{Abs(d.R2, c)}</c:f></c:numRef></c:val>");
            if (g.Tipo == "linhas") ser.Append("<c:smooth val=\"0\"/>");
            ser.Append("</c:ser>");
        }
        var plot = g.Tipo switch
        {
            "pizza" => $"<c:pieChart><c:varyColors val=\"1\"/>{ser}<c:dLbls><c:showLegendKey val=\"0\"/><c:showVal val=\"0\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"1\"/><c:showBubbleSize val=\"0\"/><c:showLeaderLines val=\"1\"/></c:dLbls><c:firstSliceAng val=\"0\"/></c:pieChart>",
            "linhas" => $"<c:lineChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>{ser}<c:marker val=\"1\"/><c:axId val=\"500\"/><c:axId val=\"501\"/></c:lineChart>{Eixos(false)}",
            _ => $"<c:barChart><c:barDir val=\"{(g.Tipo == "barras" ? "bar" : "col")}\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>{ser}<c:gapWidth val=\"80\"/><c:axId val=\"500\"/><c:axId val=\"501\"/></c:barChart>{Eixos(g.Tipo == "barras")}",
        };
        var titulo = g.Titulo.Trim();
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<c:roundedCorners val=\"0\"/><c:chart>" +
            (titulo.Length > 0 ? $"<c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"1400\" b=\"1\"/></a:pPr><a:r><a:rPr lang=\"pt-BR\" sz=\"1400\" b=\"1\"/><a:t>{X(titulo)}</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title><c:autoTitleDeleted val=\"0\"/>" : "<c:autoTitleDeleted val=\"1\"/>") +
            $"<c:plotArea><c:layout/>{plot}</c:plotArea><c:legend><c:legendPos val=\"b\"/><c:overlay val=\"0\"/></c:legend><c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart></c:chartSpace>";
    }

    static string Eixos(bool horizontal) =>
        $"<c:catAx><c:axId val=\"500\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"{(horizontal ? "l" : "b")}\"/><c:numFmt formatCode=\"General\" sourceLinked=\"1\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:crossAx val=\"501\"/><c:crosses val=\"autoZero\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>" +
        $"<c:valAx><c:axId val=\"501\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"{(horizontal ? "b" : "l")}\"/><c:majorGridlines><c:spPr><a:ln w=\"6350\"><a:solidFill><a:srgbClr val=\"E5E7EB\"/></a:solidFill></a:ln></c:spPr></c:majorGridlines><c:numFmt formatCode=\"General\" sourceLinked=\"1\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:crossAx val=\"500\"/><c:crosses val=\"autoZero\"/><c:crossBetween val=\"between\"/></c:valAx>";

    // ================================================================= rascunho (JSON) guardado no navegador
    public static string ParaJson(Pasta p)
    {
        var folhas = new JsonArray();
        foreach (var f in p.Folhas)
        {
            var est = new List<string>(); var estObj = new JsonArray();
            int Est(EstiloCel e) { var k = e.Chave; int i = est.IndexOf(k); if (i < 0) { est.Add(k); estObj.Add(new JsonObject { ["n"] = e.N, ["i"] = e.I, ["s"] = e.S, ["cor"] = e.Cor, ["fundo"] = e.Fundo, ["al"] = e.Alinh, ["fmt"] = e.Formato, ["casas"] = e.Casas }); i = est.Count - 1; } return i; }
            var cel = new JsonArray();
            foreach (var (k, c) in f.Cel) { if (c.Entrada == "" && c.E.Padrao) continue; var (r, co) = Folha.DeK(k); cel.Add(new JsonArray(r, co, c.Entrada, Est(c.E))); }
            var larg = new JsonObject(); foreach (var (c, w) in f.Larg) larg[c.ToString()] = w;
            folhas.Add(new JsonObject
            {
                ["nome"] = f.Nome, ["cel"] = cel, ["est"] = estObj, ["larg"] = larg,
                ["graf"] = new JsonArray(f.Graficos.Select(g => (JsonNode)new JsonObject { ["tipo"] = g.Tipo, ["faixa"] = g.Faixa, ["titulo"] = g.Titulo }).ToArray()),
                ["filtro"] = f.Filtro == null ? null : new JsonObject { ["l"] = f.Filtro.Linha, ["c1"] = f.Filtro.C1, ["c2"] = f.Filtro.C2 },
            });
        }
        return new JsonObject { ["folhas"] = folhas }.ToJsonString();
    }

    public static Pasta DeJson(string json)
    {
        var p = new Pasta { Folhas = new() };
        var j = JsonNode.Parse(json);
        foreach (var fj in j?["folhas"] as JsonArray ?? new())
        {
            var f = new Folha { Nome = fj?["nome"]?.GetValue<string>() ?? "Planilha" };
            var est = (fj?["est"] as JsonArray ?? new()).Select(e => new EstiloCel
            {
                N = e?["n"]?.GetValue<bool>() ?? false, I = e?["i"]?.GetValue<bool>() ?? false, S = e?["s"]?.GetValue<bool>() ?? false,
                Cor = e?["cor"]?.GetValue<string>() ?? "", Fundo = e?["fundo"]?.GetValue<string>() ?? "", Alinh = e?["al"]?.GetValue<string>() ?? "",
                Formato = e?["fmt"]?.GetValue<string>() ?? "geral", Casas = e?["casas"]?.GetValue<int>() ?? 2,
            }).ToList();
            foreach (var c in fj?["cel"] as JsonArray ?? new())
            {
                if (c is not JsonArray a || a.Count < 4) continue;
                var cel = f.Pegar(a[0]!.GetValue<int>(), a[1]!.GetValue<int>());
                cel.Entrada = a[2]!.GetValue<string>();
                int ei = a[3]!.GetValue<int>(); if (ei < est.Count) cel.E = est[ei].Clonar();
            }
            foreach (var (k, v) in fj?["larg"] as JsonObject ?? new()) if (int.TryParse(k, out var c)) f.Larg[c] = v!.GetValue<double>();
            foreach (var g in fj?["graf"] as JsonArray ?? new())
                f.Graficos.Add(new Grafico { Tipo = g?["tipo"]?.GetValue<string>() ?? "colunas", Faixa = g?["faixa"]?.GetValue<string>() ?? "", Titulo = g?["titulo"]?.GetValue<string>() ?? "" });
            if (fj?["filtro"] is JsonObject fl) f.Filtro = new Filtro { Linha = fl["l"]!.GetValue<int>(), C1 = fl["c1"]!.GetValue<int>(), C2 = fl["c2"]!.GetValue<int>() };
            p.Folhas.Add(f);
        }
        if (p.Folhas.Count == 0) p.Folhas.Add(new Folha());
        return p;
    }
}
