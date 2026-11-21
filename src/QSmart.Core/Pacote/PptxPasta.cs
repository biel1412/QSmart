using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace QSmart.Core.Pacote;

/// <summary>Lê e grava apresentações do PowerPoint (.pptx) para o Smart Screen.</summary>
public static class PptxPasta
{
    const long Emu = 12700;                   // 1 ponto do slide (960 × 540) = 12.700 EMU = 1 pt
    const long Cx = 960 * Emu, Cy = 540 * Emu;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    static string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";
    static long E(double px) => (long)Math.Round(px * Emu);
    static string Hex(string cor) { var c = TextoRico.Cor(cor); return c == "" ? "000000" : c[1..]; }

    const string NsP = "http://schemas.openxmlformats.org/presentationml/2006/main";
    const string NsA = "http://schemas.openxmlformats.org/drawingml/2006/main";
    const string NsR = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    const string NsC = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    const string NsRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    const string Cab = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
    const string Nss = "xmlns:a=\"" + NsA + "\" xmlns:r=\"" + NsR + "\" xmlns:p=\"" + NsP + "\"";
    const string RT = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    // =================================================================================================================
    // GRAVAR
    // =================================================================================================================
    public static byte[] Gravar(Apresentacao ap, string titulo = "Apresentação")
    {
        var tema = ap.T;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Put(string nome, string xml) { var e = zip.CreateEntry(nome, CompressionLevel.Optimal); using var w = new StreamWriter(e.Open(), new UTF8Encoding(false)); w.Write(xml); }
            void PutB(string nome, byte[] b) { var e = zip.CreateEntry(nome, CompressionLevel.NoCompression); using var s = e.Open(); s.Write(b); }

            var tipos = new StringBuilder(Cab + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Default Extension=\"png\" ContentType=\"image/png\"/><Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/><Default Extension=\"gif\" ContentType=\"image/gif\"/><Default Extension=\"webp\" ContentType=\"image/webp\"/>" +
                "<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>" +
                "<Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/>" +
                "<Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/>" +
                "<Override PartName=\"/ppt/notesMasters/notesMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.notesMaster+xml\"/>" +
                "<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>" +
                "<Override PartName=\"/ppt/theme/theme2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>" +
                "<Override PartName=\"/ppt/presProps.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presProps+xml\"/>" +
                "<Override PartName=\"/ppt/viewProps.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.viewProps+xml\"/>" +
                "<Override PartName=\"/ppt/tableStyles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.tableStyles+xml\"/>" +
                "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
                "<Override PartName=\"/docProps/custom.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.custom-properties+xml\"/>");

            Put("_rels/.rels", Cab + $"<Relationships xmlns=\"{NsRel}\">" +
                $"<Relationship Id=\"rId1\" Type=\"{RT}officeDocument\" Target=\"ppt/presentation.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                $"<Relationship Id=\"rId3\" Type=\"{RT}extended-properties\" Target=\"docProps/app.xml\"/>" +
                $"<Relationship Id=\"rId4\" Type=\"{RT}custom-properties\" Target=\"docProps/custom.xml\"/></Relationships>");
            var agora = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            Put("docProps/core.xml", Cab + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                $"<dc:title>{X(titulo)}</dc:title><dc:creator>QSmart · Smart Screen</dc:creator><dcterms:created xsi:type=\"dcterms:W3CDTF\">{agora}</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">{agora}</dcterms:modified></cp:coreProperties>");
            Put("docProps/app.xml", Cab + $"<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Application>QSmart Smart Screen</Application><Slides>{ap.Slides.Count}</Slides><PresentationFormat>Widescreen</PresentationFormat></Properties>");
            Put("docProps/custom.xml", Cab + "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/custom-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
                $"<property fmtid=\"{{D5CDD505-2E9C-101B-9397-08002B2CF9AE}}\" pid=\"2\" name=\"QSmartTema\"><vt:lpwstr>{X(ap.Tema)}</vt:lpwstr></property>" +
                $"<property fmtid=\"{{D5CDD505-2E9C-101B-9397-08002B2CF9AE}}\" pid=\"3\" name=\"QSmartLayouts\"><vt:lpwstr>{X(string.Join(",", ap.Slides.Select(s => s.Layout + ":" + (s.Fundo == "" ? "" : s.Fundo.TrimStart('#')))))}</vt:lpwstr></property></Properties>");

            // ---------------------------------------------------------------- tema, mestre, layout, anotações
            Put("ppt/theme/theme1.xml", TemaXml(tema));
            Put("ppt/theme/theme2.xml", TemaXml(Apresentacao.Temas.First(t => t.Chave == "simples")));
            Put("ppt/presProps.xml", Cab + $"<p:presentationPr {Nss}/>");
            Put("ppt/viewProps.xml", Cab + $"<p:viewPr {Nss}><p:normalViewPr><p:restoredLeft sz=\"15620\"/><p:restoredTop sz=\"94660\"/></p:normalViewPr><p:gridSpacing cx=\"76200\" cy=\"76200\"/></p:viewPr>");
            Put("ppt/tableStyles.xml", Cab + $"<a:tblStyleLst xmlns:a=\"{NsA}\" def=\"{{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}}\"/>");

            var spVazio = "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";
            var clrMap = "bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"";
            string Nivel(string tag, int sz, bool tit) => $"<a:{tag} marL=\"0\" algn=\"l\" defTabSz=\"914400\"><a:lnSpc><a:spcPct val=\"{(tit ? 90000 : 100000)}\"/></a:lnSpc><a:spcBef><a:spcPts val=\"0\"/></a:spcBef><a:buNone/><a:defRPr sz=\"{sz}\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"{(tit ? "+mj-lt" : "+mn-lt")}\"/></a:defRPr></a:{tag}>";
            Put("ppt/slideMasters/slideMaster1.xml", Cab + $"<p:sldMaster {Nss}><p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg><p:spTree>{spVazio}</p:spTree></p:cSld>" +
                $"<p:clrMap {clrMap}/><p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst>" +
                $"<p:txStyles><p:titleStyle>{Nivel("lvl1pPr", 4400, true)}</p:titleStyle><p:bodyStyle>{Nivel("lvl1pPr", 2400, false)}</p:bodyStyle><p:otherStyle>{Nivel("lvl1pPr", 1800, false)}</p:otherStyle></p:txStyles></p:sldMaster>");
            Put("ppt/slideMasters/_rels/slideMaster1.xml.rels", Cab + $"<Relationships xmlns=\"{NsRel}\"><Relationship Id=\"rId1\" Type=\"{RT}slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/><Relationship Id=\"rId2\" Type=\"{RT}theme\" Target=\"../theme/theme1.xml\"/></Relationships>");
            Put("ppt/slideLayouts/slideLayout1.xml", Cab + $"<p:sldLayout {Nss} type=\"blank\" preserve=\"1\"><p:cSld name=\"Em branco\"><p:spTree>{spVazio}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
            Put("ppt/slideLayouts/_rels/slideLayout1.xml.rels", Cab + $"<Relationships xmlns=\"{NsRel}\"><Relationship Id=\"rId1\" Type=\"{RT}slideMaster\" Target=\"../slideMasters/slideMaster1.xml\"/></Relationships>");
            Put("ppt/notesMasters/notesMaster1.xml", Cab + $"<p:notesMaster {Nss}><p:cSld><p:bg><p:bgRef idx=\"1001\"><a:schemeClr val=\"bg1\"/></p:bgRef></p:bg><p:spTree>{spVazio}" +
                "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Imagem do slide\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\" idx=\"2\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"685800\" y=\"1143000\"/><a:ext cx=\"5486400\" cy=\"3086100\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom><a:noFill/><a:ln w=\"12700\"><a:solidFill><a:prstClr val=\"black\"/></a:solidFill></a:ln></p:spPr></p:sp>" +
                "<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Anotações\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" sz=\"quarter\" idx=\"3\"/></p:nvPr></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"685800\" y=\"4400550\"/><a:ext cx=\"5486400\" cy=\"3600450\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr><p:txBody><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"pt-BR\"/></a:p></p:txBody></p:sp>" +
                $"</p:spTree></p:cSld><p:clrMap {clrMap}/><p:notesStyle><a:lvl1pPr marL=\"0\" algn=\"l\"><a:defRPr sz=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/></a:defRPr></a:lvl1pPr></p:notesStyle></p:notesMaster>");
            Put("ppt/notesMasters/_rels/notesMaster1.xml.rels", Cab + $"<Relationships xmlns=\"{NsRel}\"><Relationship Id=\"rId1\" Type=\"{RT}theme\" Target=\"../theme/theme2.xml\"/></Relationships>");

            // ---------------------------------------------------------------- slides
            var presRels = new StringBuilder($"<Relationship Id=\"rId1\" Type=\"{RT}slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/><Relationship Id=\"rId2\" Type=\"{RT}notesMaster\" Target=\"notesMasters/notesMaster1.xml\"/>" +
                $"<Relationship Id=\"rId3\" Type=\"{RT}presProps\" Target=\"presProps.xml\"/><Relationship Id=\"rId4\" Type=\"{RT}viewProps\" Target=\"viewProps.xml\"/><Relationship Id=\"rId5\" Type=\"{RT}theme\" Target=\"theme/theme1.xml\"/><Relationship Id=\"rId6\" Type=\"{RT}tableStyles\" Target=\"tableStyles.xml\"/>");
            var sldIds = new StringBuilder();
            int nImg = 0, nGraf = 0;
            for (int i = 0; i < ap.Slides.Count; i++)
            {
                var s = ap.Slides[i];
                int n = i + 1;
                var rels = new StringBuilder($"<Relationship Id=\"rId1\" Type=\"{RT}slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/><Relationship Id=\"rId2\" Type=\"{RT}notesSlide\" Target=\"../notesSlides/notesSlide{n}.xml\"/>");
                int rid = 3;
                var arvore = new StringBuilder();
                int id = 2;
                var anims = new List<(int id, string anim, int ordem)>();

                foreach (var (dec, _) in Enfeites(tema, s))
                    arvore.Append(Forma(id++, dec, tema, s, "QSmart Enfeite"));

                foreach (var e in s.Elementos)
                {
                    int meu = id++;
                    switch (e.Tipo)
                    {
                        case "imagem":
                        {
                            var m = Regex.Match(e.Src, @"^data:image/(png|jpe?g|gif|webp);base64,(.+)$", RegexOptions.Singleline);
                            if (!m.Success) continue;
                            var ext = m.Groups[1].Value.StartsWith("jp") ? "jpeg" : m.Groups[1].Value;
                            var nome = $"imagem{++nImg}.{ext}";
                            PutB("ppt/media/" + nome, Convert.FromBase64String(m.Groups[2].Value));
                            rels.Append($"<Relationship Id=\"rId{rid}\" Type=\"{RT}image\" Target=\"../media/{nome}\"/>");
                            arvore.Append($"<p:pic><p:nvPicPr><p:cNvPr id=\"{meu}\" name=\"Imagem {meu}\"/><p:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></p:cNvPicPr><p:nvPr/></p:nvPicPr>" +
                                $"<p:blipFill><a:blip r:embed=\"rId{rid}\"/><a:stretch><a:fillRect/></a:stretch></p:blipFill><p:spPr>{Xfrm(e)}<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr></p:pic>");
                            rid++;
                            break;
                        }
                        case "tabela":
                            if (e.Celulas.Count == 0) continue;
                            arvore.Append(Tabela(meu, e, tema, s));
                            break;
                        case "grafico":
                        {
                            if (Apresentacao.Dados(e) is not { } d) continue;
                            var nome = $"chart{++nGraf}.xml";
                            Put("ppt/charts/" + nome, GraficoXml(e, d, tema, s));
                            tipos.Append($"<Override PartName=\"/ppt/charts/{nome}\" ContentType=\"application/vnd.openxmlformats-officedocument.drawingml.chart+xml\"/>");
                            rels.Append($"<Relationship Id=\"rId{rid}\" Type=\"{RT}chart\" Target=\"../charts/{nome}\"/>");
                            arvore.Append($"<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"{meu}\" name=\"Gráfico {meu}\"/><p:cNvGraphicFramePr/><p:nvPr/></p:nvGraphicFramePr>" +
                                $"<p:xfrm><a:off x=\"{E(e.X)}\" y=\"{E(e.Y)}\"/><a:ext cx=\"{E(e.W)}\" cy=\"{E(e.H)}\"/></p:xfrm>" +
                                $"<a:graphic><a:graphicData uri=\"{NsC}\"><c:chart xmlns:c=\"{NsC}\" r:id=\"rId{rid}\"/></a:graphicData></a:graphic></p:graphicFrame>");
                            rid++;
                            break;
                        }
                        case "forma":
                            arvore.Append(Forma(meu, e, tema, s, null));
                            break;
                        default:
                            if (e.Papel != "" && TextoRico.Vazio(e.Html)) continue;   // espaço reservado vazio não vai para o arquivo
                            arvore.Append(Forma(meu, e, tema, s, null));
                            break;
                    }
                    if (e.Anim != "") anims.Add((meu, e.Anim, e.Ordem));
                }

                var (f1, f2) = tema.FundoDe(s);
                var bg = f2 != ""
                    ? $"<a:gradFill rotWithShape=\"1\"><a:gsLst><a:gs pos=\"0\"><a:srgbClr val=\"{Hex(f1)}\"/></a:gs><a:gs pos=\"100000\"><a:srgbClr val=\"{Hex(f2)}\"/></a:gs></a:gsLst><a:lin ang=\"2700000\" scaled=\"0\"/></a:gradFill>"
                    : $"<a:solidFill><a:srgbClr val=\"{Hex(f1)}\"/></a:solidFill>";
                Put($"ppt/slides/slide{n}.xml", Cab + $"<p:sld {Nss}{(s.Oculto ? " show=\"0\"" : "")}><p:cSld><p:bg><p:bgPr>{bg}<a:effectLst/></p:bgPr></p:bg><p:spTree>{spVazio}{arvore}</p:spTree></p:cSld>" +
                    $"<p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr>{Transicao(s.Transicao)}{Animacoes(anims.OrderBy(a => a.ordem).Select(a => (a.id, a.anim)).ToList())}</p:sld>");
                Put($"ppt/slides/_rels/slide{n}.xml.rels", Cab + $"<Relationships xmlns=\"{NsRel}\">{rels}</Relationships>");
                tipos.Append($"<Override PartName=\"/ppt/slides/slide{n}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>");

                // anotações
                var notas = string.Concat(s.Notas.Replace("\r", "").Split('\n').Select(l => l == "" ? "<a:p><a:endParaRPr lang=\"pt-BR\"/></a:p>" : $"<a:p><a:r><a:rPr lang=\"pt-BR\" dirty=\"0\"/><a:t>{X(l)}</a:t></a:r></a:p>"));
                Put($"ppt/notesSlides/notesSlide{n}.xml", Cab + $"<p:notes {Nss}><p:cSld><p:spTree>{spVazio}" +
                    "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Imagem do slide\"/><p:cNvSpPr><a:spLocks noGrp=\"1\" noRot=\"1\" noChangeAspect=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"sldImg\"/></p:nvPr></p:nvSpPr><p:spPr/></p:sp>" +
                    $"<p:sp><p:nvSpPr><p:cNvPr id=\"3\" name=\"Anotações\"/><p:cNvSpPr><a:spLocks noGrp=\"1\"/></p:cNvSpPr><p:nvPr><p:ph type=\"body\" idx=\"1\"/></p:nvPr></p:nvSpPr><p:spPr/><p:txBody><a:bodyPr/><a:lstStyle/>{(notas == "" ? "<a:p><a:endParaRPr lang=\"pt-BR\"/></a:p>" : notas)}</p:txBody></p:sp>" +
                    "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:notes>");
                Put($"ppt/notesSlides/_rels/notesSlide{n}.xml.rels", Cab + $"<Relationships xmlns=\"{NsRel}\"><Relationship Id=\"rId1\" Type=\"{RT}notesMaster\" Target=\"../notesMasters/notesMaster1.xml\"/><Relationship Id=\"rId2\" Type=\"{RT}slide\" Target=\"../slides/slide{n}.xml\"/></Relationships>");
                tipos.Append($"<Override PartName=\"/ppt/notesSlides/notesSlide{n}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml\"/>");

                presRels.Append($"<Relationship Id=\"rId{100 + n}\" Type=\"{RT}slide\" Target=\"slides/slide{n}.xml\"/>");
                sldIds.Append($"<p:sldId id=\"{255 + n}\" r:id=\"rId{100 + n}\"/>");
            }

            Put("ppt/presentation.xml", Cab + $"<p:presentation {Nss} saveSubsetFonts=\"1\"><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>" +
                $"<p:notesMasterIdLst><p:notesMasterId r:id=\"rId2\"/></p:notesMasterIdLst><p:sldIdLst>{sldIds}</p:sldIdLst>" +
                $"<p:sldSz cx=\"{Cx}\" cy=\"{Cy}\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/>" +
                "<p:defaultTextStyle><a:lvl1pPr marL=\"0\" algn=\"l\"><a:defRPr sz=\"1800\" kern=\"1200\"><a:solidFill><a:schemeClr val=\"tx1\"/></a:solidFill><a:latin typeface=\"+mn-lt\"/></a:defRPr></a:lvl1pPr></p:defaultTextStyle></p:presentation>");
            Put("ppt/_rels/presentation.xml.rels", Cab + $"<Relationships xmlns=\"{NsRel}\">{presRels}</Relationships>");
            tipos.Append("</Types>");
            Put("[Content_Types].xml", tipos.ToString());
        }
        return ms.ToArray();
    }

    /// <summary>Detalhes do tema desenhados no slide (barra de destaque, faixa). O editor desenha os mesmos.</summary>
    public static IEnumerable<(Elemento e, string css)> Enfeites(Tema t, Slide s)
    {
        if (s.Fundo != "" || s.Layout == "branco") yield break;
        bool tituloSlide = s.Layout is "titulo" or "secao";
        if (t.Enfeite == "barra" && !tituloSlide && s.Elementos.Any(e => e.Papel == "titulo"))
        {
            var tit = s.Elementos.First(e => e.Papel == "titulo");
            yield return (new Elemento { Tipo = "forma", Forma = "arred", X = tit.X + 10, Y = tit.Y + tit.H + 2, W = 72, H = 6, Preench = t.Cores[0] }, "");
        }
        if (t.Enfeite == "faixa")
        {
            yield return (new Elemento { Tipo = "forma", Forma = "ret", X = 0, Y = 532, W = 960, H = 8, Preench = t.Cores[0] }, "");
            yield return (new Elemento { Tipo = "forma", Forma = "ret", X = 640, Y = 532, W = 320, H = 8, Preench = t.Cores[1] }, "");
        }
        if (tituloSlide && t.Enfeite == "barra")
            yield return (new Elemento { Tipo = "forma", Forma = "ret", X = 0, Y = 0, W = 14, H = 540, Preench = t.Cores[0] }, "");
    }

    static string Xfrm(Elemento e) =>
        $"<a:xfrm{(e.Rot != 0 ? $" rot=\"{(long)Math.Round(e.Rot * 60000)}\"" : "")}><a:off x=\"{E(e.X)}\" y=\"{E(e.Y)}\"/><a:ext cx=\"{Math.Max(1, E(e.W))}\" cy=\"{Math.Max(1, E(e.H))}\"/></a:xfrm>";

    static readonly Dictionary<string, string> Geo = new()
    {
        ["ret"] = "rect", ["arred"] = "roundRect", ["elipse"] = "ellipse", ["triangulo"] = "triangle", ["seta"] = "rightArrow",
        ["estrela"] = "star5", ["losango"] = "diamond", ["linha"] = "line",
    };

    static string Forma(int id, Elemento e, Tema t, Slide s, string? nome)
    {
        bool forma = e.Tipo == "forma";
        var geo = forma ? Geo.GetValueOrDefault(e.Forma, "rect") : "rect";
        var fill = forma && e.Preench != "" && e.Forma != "linha" ? $"<a:solidFill><a:srgbClr val=\"{Hex(e.Preench)}\"/></a:solidFill>" : "<a:noFill/>";
        var ln = forma && (e.BordaL > 0 || e.Forma == "linha") && (e.Borda != "" || e.Forma == "linha")
            ? $"<a:ln w=\"{E(Math.Max(e.BordaL, e.Forma == "linha" ? 2 : 0))}\"><a:solidFill><a:srgbClr val=\"{Hex(e.Borda != "" ? e.Borda : e.Preench != "" ? e.Preench : t.Texto)}\"/></a:solidFill></a:ln>"
            : "<a:ln><a:noFill/></a:ln>";
        nome ??= e.Papel switch { "titulo" => "Título", "subtitulo" => "Subtítulo", "corpo" => "Texto", _ => forma ? "Forma" : "Caixa de texto" } + $" {id}";
        var sb = new StringBuilder($"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"{X(nome)}\"/><p:cNvSpPr{(forma ? "" : " txBox=\"1\"")}/><p:nvPr/></p:nvSpPr>");
        sb.Append($"<p:spPr>{Xfrm(e)}<a:prstGeom prst=\"{geo}\"><a:avLst/></a:prstGeom>{fill}{ln}</p:spPr>");
        if (e.Forma != "linha" || !forma)
            sb.Append(Corpo(e, t, s, forma ? "ctr" : null));
        sb.Append("</p:sp>");
        return sb.ToString();
    }

    static string Corpo(Elemento e, Tema t, Slide s, string? ancoraForma)
    {
        var anc = e.AlinhV switch { "m" => "ctr", "b" => "b", _ => ancoraForma ?? "t" };
        if (e.Tipo == "forma" && e.AlinhV == "t" && ancoraForma != null) anc = "ctr";
        var sb = new StringBuilder($"<p:txBody><a:bodyPr wrap=\"square\" lIns=\"121920\" tIns=\"60960\" rIns=\"121920\" bIns=\"60960\" rtlCol=\"0\" anchor=\"{anc}\"><a:noAutofit/></a:bodyPr><a:lstStyle/>");
        var baseTam = e.Tam > 0 ? e.Tam : Apresentacao.TamPadrao(e.Papel);
        var baseCor = Hex(e.Tipo == "forma" && e.Cor == "" ? (e.Preench != "" && Apresentacao.Escura(e.Preench) ? "#FFFFFF" : "#111827") : t.TextoDe(s, e));
        var baseFonte = t.FonteDe(e);
        bool negritoBase = e.Papel == "titulo";
        foreach (var p in TextoRico.Ler(e.Html))
        {
            var algn = p.Alinh switch { "center" => "ctr", "right" => "r", "justify" => "just", _ => e.Tipo == "forma" ? "ctr" : "l" };
            sb.Append($"<a:p><a:pPr algn=\"{algn}\"");
            if (p.Lista != "")
            {
                long mar = (p.Nivel + 1) * 342900;
                sb.Append($" marL=\"{mar}\" indent=\"-342900\"{(p.Nivel > 0 ? $" lvl=\"{Math.Min(8, p.Nivel)}\"" : "")}><a:spcBef><a:spcPts val=\"{(int)(baseTam * 25)}\"/></a:spcBef>");
                sb.Append(p.Lista == "ol" ? "<a:buFont typeface=\"+mj-lt\"/><a:buAutoNum type=\"arabicPeriod\"/>" : $"<a:buClr><a:srgbClr val=\"{Hex(t.Cores[0])}\"/></a:buClr><a:buFont typeface=\"Arial\"/><a:buChar char=\"•\"/>");
                sb.Append("</a:pPr>");
            }
            else sb.Append(" marL=\"0\" indent=\"0\"><a:buNone/></a:pPr>");
            double ultTam = baseTam;
            foreach (var r in p.Trechos)
            {
                if (r.Texto.Length == 0) continue;
                var tam = r.Tam > 0 ? r.Tam : baseTam; ultTam = tam;
                sb.Append($"<a:r><a:rPr lang=\"pt-BR\" sz=\"{(int)Math.Round(tam * 100)}\"{(r.N || negritoBase ? " b=\"1\"" : "")}{(r.I ? " i=\"1\"" : "")}{(r.U ? " u=\"sng\"" : "")}{(r.S ? " strike=\"sngStrike\"" : "")} dirty=\"0\">");
                sb.Append($"<a:solidFill><a:srgbClr val=\"{(r.Cor != "" ? Hex(r.Cor) : baseCor)}\"/></a:solidFill>");
                var fonte = r.Fonte != "" ? r.Fonte : baseFonte;
                sb.Append($"<a:latin typeface=\"{X(fonte)}\"/><a:cs typeface=\"{X(fonte)}\"/></a:rPr><a:t>{X(r.Texto)}</a:t></a:r>");
            }
            sb.Append($"<a:endParaRPr lang=\"pt-BR\" sz=\"{(int)Math.Round(ultTam * 100)}\" dirty=\"0\"/></a:p>");
        }
        sb.Append("</p:txBody>");
        return sb.ToString();
    }

    static string Tabela(int id, Elemento e, Tema t, Slide s)
    {
        int linhas = e.Celulas.Count, cols = e.Celulas.Max(l => l.Count);
        long w = E(e.W / cols), h = E(e.H / linhas);
        var tam = e.Tam > 0 ? e.Tam : 18;
        var escuro = Apresentacao.Escura(t.FundoDe(s).f1);
        var sb = new StringBuilder($"<p:graphicFrame><p:nvGraphicFramePr><p:cNvPr id=\"{id}\" name=\"Tabela {id}\"/><p:cNvGraphicFramePr><a:graphicFrameLocks noGrp=\"1\"/></p:cNvGraphicFramePr><p:nvPr/></p:nvGraphicFramePr>");
        sb.Append($"<p:xfrm><a:off x=\"{E(e.X)}\" y=\"{E(e.Y)}\"/><a:ext cx=\"{w * cols}\" cy=\"{h * linhas}\"/></p:xfrm><a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/table\">");
        sb.Append($"<a:tbl><a:tblPr firstRow=\"{(e.CabDestaque ? 1 : 0)}\" bandRow=\"1\"/><a:tblGrid>");
        for (int c = 0; c < cols; c++) sb.Append($"<a:gridCol w=\"{w}\"/>");
        sb.Append("</a:tblGrid>");
        var borda = escuro ? "475569" : "D1D5DB";
        string Ln(string tag) => $"<a:{tag} w=\"9525\"><a:solidFill><a:srgbClr val=\"{borda}\"/></a:solidFill></a:{tag}>";
        for (int r = 0; r < linhas; r++)
        {
            sb.Append($"<a:tr h=\"{h}\">");
            for (int c = 0; c < cols; c++)
            {
                var txt = c < e.Celulas[r].Count ? e.Celulas[r][c] : "";
                bool cab = e.CabDestaque && r == 0;
                var fundo = cab ? Hex(t.Cores[0]) : r % 2 == 0 ? (escuro ? "1E293B" : "F3F4F6") : (escuro ? "0F172A" : "FFFFFF");
                var cor = cab ? (Apresentacao.Escura(t.Cores[0]) ? "FFFFFF" : "111827") : (escuro ? "E5E7EB" : "1F2937");
                sb.Append("<a:tc><a:txBody><a:bodyPr/><a:lstStyle/>");
                foreach (var linha in txt.Replace("\r", "").Split('\n'))
                    sb.Append(linha == "" ? $"<a:p><a:endParaRPr lang=\"pt-BR\" sz=\"{(int)(tam * 100)}\"/></a:p>"
                        : $"<a:p><a:r><a:rPr lang=\"pt-BR\" sz=\"{(int)(tam * 100)}\"{(cab ? " b=\"1\"" : "")} dirty=\"0\"><a:solidFill><a:srgbClr val=\"{cor}\"/></a:solidFill><a:latin typeface=\"{X(t.FonteCorpo)}\"/></a:rPr><a:t>{X(linha)}</a:t></a:r></a:p>");
                sb.Append($"</a:txBody><a:tcPr marL=\"91440\" marR=\"91440\" marT=\"45720\" marB=\"45720\" anchor=\"ctr\">{Ln("lnL")}{Ln("lnR")}{Ln("lnT")}{Ln("lnB")}<a:solidFill><a:srgbClr val=\"{fundo}\"/></a:solidFill></a:tcPr></a:tc>");
            }
            sb.Append("</a:tr>");
        }
        sb.Append("</a:tbl></a:graphicData></a:graphic></p:graphicFrame>");
        return sb.ToString();
    }

    static string GraficoXml(Elemento e, XlsxPasta.DadosGrafico d, Tema t, Slide s)
    {
        var texto = Hex(Apresentacao.Escura(t.FundoDe(s).f1) ? "#E5E7EB" : "#374151");
        string Lit(IEnumerable<string> xs) { var l = xs.ToList(); return $"<c:strLit><c:ptCount val=\"{l.Count}\"/>{string.Concat(l.Select((x, i) => $"<c:pt idx=\"{i}\"><c:v>{X(x)}</c:v></c:pt>"))}</c:strLit>"; }
        string Num(List<double?> v) => $"<c:numLit><c:formatCode>General</c:formatCode><c:ptCount val=\"{v.Count}\"/>{string.Concat(v.Select((x, i) => x is double dd ? $"<c:pt idx=\"{i}\"><c:v>{dd.ToString("R", Inv)}</c:v></c:pt>" : ""))}</c:numLit>";
        var ser = new StringBuilder();
        int nSeries = e.Graf == "pizza" ? 1 : d.Series.Count;
        for (int k = 0; k < nSeries; k++)
        {
            var cor = Hex(t.Cores[k % t.Cores.Length]);
            ser.Append($"<c:ser><c:idx val=\"{k}\"/><c:order val=\"{k}\"/><c:tx><c:v>{X(d.Series[k].nome)}</c:v></c:tx>");
            if (e.Graf == "linhas") ser.Append($"<c:spPr><a:ln w=\"34925\" cap=\"rnd\"><a:solidFill><a:srgbClr val=\"{cor}\"/></a:solidFill><a:round/></a:ln></c:spPr><c:marker><c:symbol val=\"circle\"/><c:size val=\"7\"/><c:spPr><a:solidFill><a:srgbClr val=\"{cor}\"/></a:solidFill></c:spPr></c:marker>");
            else if (e.Graf != "pizza") ser.Append($"<c:spPr><a:solidFill><a:srgbClr val=\"{cor}\"/></a:solidFill></c:spPr><c:invertIfNegative val=\"0\"/>");
            if (e.Graf == "pizza")
                for (int p = 0; p < d.Categorias.Count; p++)
                    ser.Append($"<c:dPt><c:idx val=\"{p}\"/><c:bubble3D val=\"0\"/><c:spPr><a:solidFill><a:srgbClr val=\"{Hex(t.Cores[p % t.Cores.Length])}\"/></a:solidFill><a:ln w=\"19050\"><a:solidFill><a:srgbClr val=\"{Hex(t.FundoDe(s).f1)}\"/></a:solidFill></a:ln></c:spPr></c:dPt>");
            ser.Append($"<c:cat>{Lit(d.Categorias)}</c:cat><c:val>{Num(d.Series[k].v)}</c:val>");
            if (e.Graf == "linhas") ser.Append("<c:smooth val=\"0\"/>");
            ser.Append("</c:ser>");
        }
        var plot = e.Graf switch
        {
            "pizza" => $"<c:pieChart><c:varyColors val=\"1\"/>{ser}<c:dLbls><c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr><c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"1400\" b=\"1\"><a:solidFill><a:srgbClr val=\"FFFFFF\"/></a:solidFill></a:defRPr></a:pPr><a:endParaRPr lang=\"pt-BR\"/></a:p></c:txPr><c:dLblPos val=\"ctr\"/><c:showLegendKey val=\"0\"/><c:showVal val=\"0\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/><c:showPercent val=\"1\"/><c:showBubbleSize val=\"0\"/><c:showLeaderLines val=\"0\"/></c:dLbls><c:firstSliceAng val=\"0\"/></c:pieChart>",
            "linhas" => $"<c:lineChart><c:grouping val=\"standard\"/><c:varyColors val=\"0\"/>{ser}<c:marker val=\"1\"/><c:axId val=\"500\"/><c:axId val=\"501\"/></c:lineChart>{Eixos(false)}",
            _ => $"<c:barChart><c:barDir val=\"{(e.Graf == "barras" ? "bar" : "col")}\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>{ser}<c:gapWidth val=\"70\"/><c:axId val=\"500\"/><c:axId val=\"501\"/></c:barChart>{Eixos(e.Graf == "barras")}",
        };
        var titulo = e.TituloGraf.Trim();
        string Eixos(bool h) => XlsxPasta.Eixos(h).Replace("E5E7EB", Apresentacao.Escura(t.FundoDe(s).f1) ? "334155" : "E5E7EB");
        return Cab + $"<c:chartSpace xmlns:c=\"{NsC}\" xmlns:a=\"{NsA}\" xmlns:r=\"{NsR}\"><c:roundedCorners val=\"0\"/><c:chart>" +
            (titulo.Length > 0 ? $"<c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"1800\" b=\"1\"/></a:pPr><a:r><a:rPr lang=\"pt-BR\" sz=\"1800\" b=\"1\"><a:solidFill><a:srgbClr val=\"{texto}\"/></a:solidFill></a:rPr><a:t>{X(titulo)}</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title><c:autoTitleDeleted val=\"0\"/>" : "<c:autoTitleDeleted val=\"1\"/>") +
            $"<c:plotArea><c:layout/>{plot}<c:spPr><a:noFill/></c:spPr></c:plotArea><c:legend><c:legendPos val=\"b\"/><c:overlay val=\"0\"/></c:legend><c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart>" +
            $"<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr><c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"1400\"><a:solidFill><a:srgbClr val=\"{texto}\"/></a:solidFill></a:defRPr></a:pPr><a:endParaRPr lang=\"pt-BR\"/></a:p></c:txPr></c:chartSpace>";
    }

    static string Transicao(string t) => t switch
    {
        "esmaecer" => "<p:transition spd=\"med\"><p:fade/></p:transition>",
        "empurrar" => "<p:transition spd=\"med\"><p:push dir=\"l\"/></p:transition>",
        "revelar" => "<p:transition spd=\"med\"><p:wipe dir=\"r\"/></p:transition>",
        "zoom" => "<p:transition spd=\"med\"><p:zoom/></p:transition>",
        _ => "",
    };

    static string Animacoes(List<(int id, string anim)> anims)
    {
        if (anims.Count == 0) return "";
        int n = 3;
        var cliques = new StringBuilder();
        foreach (var (id, anim) in anims)
        {
            var alvo = $"<p:tgtEl><p:spTgt spid=\"{id}\"/></p:tgtEl>";
            var (preset, sub) = anim switch { "esmaecer" => (10, 0), "subir" => (2, 4), "zoom" => (53, 16), _ => (1, 0) };
            int c1 = n++, c2 = n++, c3 = n++;
            var efeito = new StringBuilder($"<p:set><p:cBhvr><p:cTn id=\"{n++}\" dur=\"1\" fill=\"hold\"><p:stCondLst><p:cond delay=\"0\"/></p:stCondLst></p:cTn>{alvo}<p:attrNameLst><p:attrName>style.visibility</p:attrName></p:attrNameLst></p:cBhvr><p:to><p:strVal val=\"visible\"/></p:to></p:set>");
            string Anim(string attr, string de, string para) =>
                $"<p:anim calcmode=\"lin\" valueType=\"num\"><p:cBhvr additive=\"base\"><p:cTn id=\"{n++}\" dur=\"500\" fill=\"hold\"/>{alvo}<p:attrNameLst><p:attrName>{attr}</p:attrName></p:attrNameLst></p:cBhvr><p:tavLst><p:tav tm=\"0\"><p:val><p:strVal val=\"{de}\"/></p:val></p:tav><p:tav tm=\"100000\"><p:val><p:strVal val=\"{para}\"/></p:val></p:tav></p:tavLst></p:anim>";
            string Fade() => $"<p:animEffect transition=\"in\" filter=\"fade\"><p:cBhvr><p:cTn id=\"{n++}\" dur=\"500\"/>{alvo}</p:cBhvr></p:animEffect>";
            if (anim == "esmaecer") efeito.Append(Fade());
            if (anim == "subir") { efeito.Append(Anim("ppt_x", "#ppt_x", "#ppt_x")); efeito.Append(Anim("ppt_y", "1+#ppt_h/2", "#ppt_y")); }
            if (anim == "zoom") { efeito.Append(Anim("ppt_w", "0", "#ppt_w")); efeito.Append(Anim("ppt_h", "0", "#ppt_h")); efeito.Append(Fade()); }
            cliques.Append($"<p:par><p:cTn id=\"{c1}\" fill=\"hold\"><p:stCondLst><p:cond delay=\"indefinite\"/></p:stCondLst><p:childTnLst>" +
                $"<p:par><p:cTn id=\"{c2}\" fill=\"hold\"><p:stCondLst><p:cond delay=\"0\"/></p:stCondLst><p:childTnLst>" +
                $"<p:par><p:cTn id=\"{c3}\" presetID=\"{preset}\" presetClass=\"entr\" presetSubtype=\"{sub}\" fill=\"hold\" nodeType=\"clickEffect\"><p:stCondLst><p:cond delay=\"0\"/></p:stCondLst><p:childTnLst>{efeito}</p:childTnLst></p:cTn></p:par>" +
                "</p:childTnLst></p:cTn></p:par></p:childTnLst></p:cTn></p:par>");
        }
        return "<p:timing><p:tnLst><p:par><p:cTn id=\"1\" dur=\"indefinite\" restart=\"never\" nodeType=\"tmRoot\"><p:childTnLst>" +
            $"<p:seq concurrent=\"1\" nextAc=\"seek\"><p:cTn id=\"2\" dur=\"indefinite\" nodeType=\"mainSeq\"><p:childTnLst>{cliques}</p:childTnLst></p:cTn>" +
            "<p:prevCondLst><p:cond evt=\"onPrev\" delay=\"0\"><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:prevCondLst><p:nextCondLst><p:cond evt=\"onNext\" delay=\"0\"><p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:nextCondLst></p:seq>" +
            "</p:childTnLst></p:cTn></p:par></p:tnLst></p:timing>";
    }

    static string TemaXml(Tema t)
    {
        string C(string nome, string cor) => $"<a:{nome}><a:srgbClr val=\"{Hex(cor)}\"/></a:{nome}>";
        bool escuro = Apresentacao.Escura(t.Fundo);
        var dk1 = escuro ? t.Fundo : t.Texto; var lt1 = escuro ? t.Texto : t.Fundo;
        string Fonte(string tag, string f) => $"<a:{tag}><a:latin typeface=\"{X(f)}\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:{tag}>";
        return Cab + $"<a:theme xmlns:a=\"{NsA}\" name=\"QSmart {X(t.Nome)}\"><a:themeElements><a:clrScheme name=\"QSmart {X(t.Nome)}\">" +
            C("dk1", dk1) + C("lt1", lt1) + C("dk2", escuro ? "#1E293B" : t.Titulo) + C("lt2", escuro ? "#334155" : "#F3F4F6") +
            string.Concat(t.Cores.Select((c, i) => C($"accent{i + 1}", c))) + C("hlink", t.Cores[0]) + C("folHlink", t.Cores[4]) + "</a:clrScheme>" +
            $"<a:fontScheme name=\"QSmart\">{Fonte("majorFont", t.FonteTit)}{Fonte("minorFont", t.FonteCorpo)}</a:fontScheme>" +
            "<a:fmtScheme name=\"QSmart\"><a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:fillStyleLst>" +
            "<a:lnStyleLst><a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"12700\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln><a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln></a:lnStyleLst>" +
            "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst>" +
            "<a:bgFillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme>" +
            "</a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";
    }

    // =================================================================================================================
    // LER
    // =================================================================================================================
    sealed class Leitura
    {
        public ZipArchive Zip = null!;
        public double K = 1, Ox, Oy;
        public Dictionary<string, string> Cores = new();          // dk1, lt1, accent1...
        public Dictionary<string, string> Mapa = new() { ["bg1"] = "lt1", ["tx1"] = "dk1", ["bg2"] = "lt2", ["tx2"] = "dk2" };
        public double TamTit = 44, TamCorpo = 28, TamOutro = 18;
        /// <summary>Tema do Smart Screen, quando o arquivo foi feito por ele (aí dá para recuperar os detalhes).</summary>
        public Tema? Nosso;
    }

    static XNamespace P => NsP;
    static XNamespace A => NsA;
    static XNamespace R => NsR;
    static XNamespace Cn => NsC;

    static XDocument? Xml(ZipArchive z, string nome)
    {
        var e = z.GetEntry(nome.TrimStart('/'));
        if (e == null) return null;
        using var s = e.Open();
        return XDocument.Load(s);
    }

    static byte[]? Bytes(ZipArchive z, string nome)
    {
        var e = z.GetEntry(nome.TrimStart('/'));
        if (e == null) return null;
        using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray();
    }

    static string Junta(string baseParte, string alvo)
    {
        if (alvo.StartsWith("/")) return alvo.TrimStart('/');
        var dir = baseParte.Contains('/') ? baseParte[..baseParte.LastIndexOf('/')] : "";
        var partes = (dir == "" ? new List<string>() : dir.Split('/').ToList());
        foreach (var p in alvo.Split('/'))
        {
            if (p == "..") { if (partes.Count > 0) partes.RemoveAt(partes.Count - 1); }
            else if (p != ".") partes.Add(p);
        }
        return string.Join('/', partes);
    }

    static Dictionary<string, (string tipo, string alvo)> Rels(ZipArchive z, string parte)
    {
        var dir = parte.Contains('/') ? parte[..parte.LastIndexOf('/')] : "";
        var nome = parte[(parte.LastIndexOf('/') + 1)..];
        var d = new Dictionary<string, (string, string)>();
        var x = Xml(z, (dir == "" ? "" : dir + "/") + "_rels/" + nome + ".rels");
        if (x == null) return d;
        foreach (var r in x.Root!.Elements())
        {
            var mode = (string?)r.Attribute("TargetMode");
            var alvo = (string?)r.Attribute("Target") ?? "";
            d[(string?)r.Attribute("Id") ?? ""] = (((string?)r.Attribute("Type") ?? "").Split('/').Last(), mode == "External" ? alvo : Junta(parte, alvo));
        }
        return d;
    }

    public static Apresentacao Ler(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        ZipArchive zip;
        try { zip = new ZipArchive(ms, ZipArchiveMode.Read); } catch { throw new InvalidDataException("O arquivo não é um .pptx válido."); }
        using var _z = zip;
        var L = new Leitura { Zip = zip };
        var pres = Xml(zip, "ppt/presentation.xml") ?? throw new InvalidDataException("O arquivo não é uma apresentação do PowerPoint.");
        var sz = pres.Root!.Element(P + "sldSz");
        double cx = (double?)sz?.Attribute("cx") ?? Cx, cy = (double?)sz?.Attribute("cy") ?? Cy;
        L.K = Math.Min(960.0 / cx, 540.0 / cy);
        L.Ox = (960 - cx * L.K) / 2; L.Oy = (540 - cy * L.K) / 2;

        var ap = new Apresentacao { Tema = "simples" };
        var custom = Xml(zip, "docProps/custom.xml");
        var temaProp = custom?.Root?.Elements().FirstOrDefault(p => (string?)p.Attribute("name") == "QSmartTema")?.Value;
        if (temaProp != null && Apresentacao.Temas.Any(t => t.Chave == temaProp)) { ap.Tema = temaProp; L.Nosso = ap.T; }
        var layouts = (custom?.Root?.Elements().FirstOrDefault(p => (string?)p.Attribute("name") == "QSmartLayouts")?.Value ?? "").Split(',');

        var presRels = Rels(zip, "ppt/presentation.xml");
        bool temaLido = false;
        foreach (var id in pres.Root.Element(P + "sldIdLst")?.Elements(P + "sldId") ?? Enumerable.Empty<XElement>())
        {
            var rid = (string?)id.Attribute(R + "id") ?? "";
            if (!presRels.TryGetValue(rid, out var rel)) continue;
            try
            {
                var s = LerSlide(L, rel.alvo, ref temaLido);
                if (s == null) continue;
                if (L.Nosso != null) Recuperar(L.Nosso, s, ap.Slides.Count < layouts.Length ? layouts[ap.Slides.Count] : "");
                ap.Slides.Add(s);
            }
            catch { /* slide com algo que não entendemos: segue com os outros */ }
        }
        if (ap.Slides.Count == 0) ap.Slides.Add(Apresentacao.NovoSlide("branco"));
        return ap;
    }

    static Slide? LerSlide(Leitura L, string parte, ref bool temaLido)
    {
        var x = Xml(L.Zip, parte);
        if (x == null) return null;
        var rels = Rels(L.Zip, parte);
        var layout = rels.Values.FirstOrDefault(r => r.tipo == "slideLayout").alvo;
        var master = layout != null ? Rels(L.Zip, layout).Values.FirstOrDefault(r => r.tipo == "slideMaster").alvo : null;
        var xLay = layout != null ? Xml(L.Zip, layout) : null;
        var xMas = master != null ? Xml(L.Zip, master) : null;
        if (!temaLido && master != null)
        {
            temaLido = true;
            var tema = Rels(L.Zip, master).Values.FirstOrDefault(r => r.tipo == "theme").alvo;
            var xt = tema != null ? Xml(L.Zip, tema) : null;
            foreach (var c in xt?.Descendants(A + "clrScheme").FirstOrDefault()?.Elements() ?? Enumerable.Empty<XElement>())
            {
                var srgb = c.Element(A + "srgbClr")?.Attribute("val")?.Value ?? c.Element(A + "sysClr")?.Attribute("lastClr")?.Value;
                if (srgb != null) L.Cores[c.Name.LocalName] = "#" + srgb.ToUpperInvariant();
            }
            var cm = xMas?.Root?.Element(P + "clrMap");
            if (cm != null) foreach (var a in cm.Attributes()) L.Mapa[a.Name.LocalName] = a.Value;
            double? Sz(string estilo) => (double?)xMas?.Root?.Element(P + "txStyles")?.Element(P + estilo)?.Element(A + "lvl1pPr")?.Element(A + "defRPr")?.Attribute("sz") / 100;
            L.TamTit = Sz("titleStyle") ?? 44; L.TamCorpo = Sz("bodyStyle") ?? 28; L.TamOutro = Sz("otherStyle") ?? 18;
        }

        var s = new Slide { Layout = "branco" };
        if ((string?)x.Root!.Attribute("show") == "0") s.Oculto = true;

        // fundo: slide → layout → mestre
        foreach (var (doc, prt) in new[] { (x, parte), (xLay, layout), (xMas, master) })
        {
            var bg = doc?.Root?.Element(P + "cSld")?.Element(P + "bg");
            if (bg == null) continue;
            var bgPr = bg.Element(P + "bgPr");
            if (bgPr != null)
            {
                var blip = bgPr.Element(A + "blipFill")?.Element(A + "blip");
                if (blip != null && prt != null)
                {
                    var img = Imagem(L, Rels(L.Zip, prt), (string?)blip.Attribute(R + "embed"));
                    if (img != null) s.Elementos.Add(new Elemento { Tipo = "imagem", X = 0, Y = 0, W = 960, H = 540, Src = img });
                    s.Fundo = "#FFFFFF";
                }
                else s.Fundo = Preench(L, bgPr) ?? "#FFFFFF";
            }
            else
            {
                var r = bg.Element(P + "bgRef");
                s.Fundo = r != null ? CorDe(L, r) ?? "#FFFFFF" : "#FFFFFF";
            }
            break;
        }
        if (s.Fundo == "") s.Fundo = "#FFFFFF";

        // posições dos espaços reservados herdadas do layout/mestre
        var phs = new List<(string? tipo, string? idx, XElement sp)>();
        foreach (var doc in new[] { xLay, xMas })
            foreach (var sp in doc?.Root?.Element(P + "cSld")?.Element(P + "spTree")?.Elements(P + "sp") ?? Enumerable.Empty<XElement>())
            {
                var ph = sp.Element(P + "nvSpPr")?.Element(P + "nvPr")?.Element(P + "ph");
                if (ph != null) phs.Add(((string?)ph.Attribute("type"), (string?)ph.Attribute("idx"), sp));
            }

        var ids = new Dictionary<string, Elemento>();
        var arvore = x.Root.Element(P + "cSld")?.Element(P + "spTree");
        if (arvore != null) Arvore(L, s, arvore, rels, phs, ids, (0, 0, 1, 1));

        // layout de acordo com o que veio
        if (s.Elementos.Any(e => e.Papel == "subtitulo") && s.Elementos.Count(e => e.Papel != "") <= 2) s.Layout = "titulo";
        else if (s.Elementos.Any(e => e.Papel == "corpo")) s.Layout = "conteudo";
        else if (s.Elementos.Any(e => e.Papel == "titulo")) s.Layout = "so-titulo";

        // transição
        var tr = x.Root.Descendants().FirstOrDefault(d => d.Name.LocalName == "transition" && d.Name.NamespaceName == NsP && d.HasElements);
        if (tr != null)
        {
            var nome = tr.Elements().First().Name.LocalName;
            s.Transicao = nome switch { "fade" => "esmaecer", "push" or "cover" or "pull" or "split" or "randomBar" => "empurrar", "wipe" or "strips" or "wheel" or "circle" or "diamond" or "plus" or "wedge" => "revelar", "zoom" or "newsflash" => "zoom", _ => "esmaecer" };
        }

        // animações de entrada (na ordem dos cliques)
        foreach (var ctn in x.Root.Descendants(P + "cTn").Where(c => (string?)c.Attribute("presetClass") == "entr"))
        {
            var spid = ctn.Descendants(P + "spTgt").FirstOrDefault()?.Attribute("spid")?.Value;
            if (spid == null || !ids.TryGetValue(spid, out var el) || el.Anim != "") continue;
            el.Anim = ((string?)ctn.Attribute("presetID")) switch { "10" => "esmaecer", "2" or "47" or "42" => "subir", "53" or "23" or "21" => "zoom", "1" => "aparecer", _ => "esmaecer" };
            el.Ordem = s.Elementos.Count(x => x.Anim != "");
        }

        // anotações
        var notas = rels.Values.FirstOrDefault(r => r.tipo == "notesSlide").alvo;
        if (notas != null && Xml(L.Zip, notas) is { } xn)
        {
            var corpo = xn.Descendants(P + "sp").FirstOrDefault(sp => (string?)sp.Element(P + "nvSpPr")?.Element(P + "nvPr")?.Element(P + "ph")?.Attribute("type") == "body");
            if (corpo != null) s.Notas = string.Join("\n", corpo.Descendants(A + "p").Select(p => string.Concat(p.Descendants(A + "t").Select(t => t.Value)))).Trim();
        }
        return s;
    }

    static void Arvore(Leitura L, Slide s, XElement arv, Dictionary<string, (string tipo, string alvo)> rels,
        List<(string? tipo, string? idx, XElement sp)> phs, Dictionary<string, Elemento> ids, (double ox, double oy, double kx, double ky) g)
    {
        foreach (var el in arv.Elements())
        {
            var nome = el.Name.LocalName;
            if (nome == "AlternateContent") { var esc = el.Elements().FirstOrDefault(e => e.Name.LocalName == "Choice") ?? el.Elements().FirstOrDefault(); if (esc != null) Arvore(L, s, esc, rels, phs, ids, g); continue; }
            var cnv = el.Descendants(P + "cNvPr").FirstOrDefault();
            if (cnv != null && ((string?)cnv.Attribute("name") ?? "").StartsWith("QSmart Enfeite")) continue;
            if (cnv != null && (string?)cnv.Attribute("hidden") == "1") continue;
            Elemento? novo = null;
            switch (nome)
            {
                case "grpSp":
                {
                    var xf = el.Element(P + "grpSpPr")?.Element(A + "xfrm");
                    if (xf == null) { Arvore(L, s, el, rels, phs, ids, g); break; }
                    double V(string a, string b) => (double?)xf.Element(A + a)?.Attribute(b) ?? 0;
                    double offX = V("off", "x"), offY = V("off", "y"), extX = V("ext", "cx"), extY = V("ext", "cy");
                    double chX = V("chOff", "x"), chY = V("chOff", "y"), chW = V("chExt", "cx"), chH = V("chExt", "cy");
                    double kx = chW > 0 ? extX / chW : 1, ky = chH > 0 ? extY / chH : 1;
                    // ponto filho p → (off + (p - chOff)·k) no espaço do pai
                    Arvore(L, s, el, rels, phs, ids, (g.ox + (offX - chX * kx) * g.kx, g.oy + (offY - chY * ky) * g.ky, g.kx * kx, g.ky * ky));
                    break;
                }
                case "sp": case "cxnSp":
                    novo = Forma(L, s, el, phs, nome == "cxnSp");
                    break;
                case "pic":
                {
                    var blip = el.Descendants(A + "blip").FirstOrDefault();
                    var img = Imagem(L, rels, (string?)blip?.Attribute(R + "embed"));
                    if (img == null) break;
                    novo = new Elemento { Tipo = "imagem", Src = img };
                    if (!Posicao(el.Element(P + "spPr")?.Element(A + "xfrm"), novo)) { var ph = Ph(el); if (Herdada(phs, ph) is { } h) Posicao(h.Element(P + "spPr")?.Element(A + "xfrm"), novo); }
                    break;
                }
                case "graphicFrame":
                {
                    var gd = el.Descendants(A + "graphicData").FirstOrDefault();
                    var e = new Elemento();
                    Posicao(el.Element(P + "xfrm"), e);
                    var tbl = gd?.Element(A + "tbl");
                    if (tbl != null)
                    {
                        e.Tipo = "tabela";
                        e.Celulas = tbl.Elements(A + "tr").Select(tr => tr.Elements(A + "tc").Select(tc => string.Join("\n", tc.Descendants(A + "p").Select(p => string.Concat(p.Descendants(A + "t").Select(t => t.Value))))).ToList()).ToList();
                        var sz = tbl.Descendants(A + "rPr").Select(r => (double?)r.Attribute("sz")).FirstOrDefault(v => v != null);
                        e.Tam = sz != null ? sz.Value / 100 : 18;
                        e.CabDestaque = (string?)tbl.Element(A + "tblPr")?.Attribute("firstRow") == "1";
                        novo = e; break;
                    }
                    var ch = gd?.Element(Cn + "chart");
                    if (ch != null && rels.TryGetValue((string?)ch.Attribute(R + "id") ?? "", out var rc) && Xml(L.Zip, rc.alvo) is { } xc)
                    {
                        e.Tipo = "grafico";
                        if (LerGrafico(xc, e)) novo = e;
                    }
                    break;
                }
            }
            if (novo == null) continue;
            // aplica a transformação do grupo e a escala do slide
            novo.X = L.Ox + (g.ox + novo.X * g.kx) * L.K;
            novo.Y = L.Oy + (g.oy + novo.Y * g.ky) * L.K;
            novo.W = Math.Max(4, novo.W * g.kx * L.K);
            novo.H = Math.Max(novo.Tipo == "forma" && novo.Forma == "linha" ? 0 : 4, novo.H * g.ky * L.K);
            if (novo.Tam > 0) novo.Tam *= L.K * 12700;
            s.Elementos.Add(novo);
            if (cnv != null) ids[(string?)cnv.Attribute("id") ?? ""] = novo;
        }
    }

    /// <summary>Arquivo feito pelo Smart Screen: volta o layout, tira as cores/fontes que são só as do tema (para trocar de tema depois).</summary>
    static void Recuperar(Tema t, Slide s, string info)
    {
        var partes = info.Split(':');
        if (partes[0] != "" && Apresentacao.Layouts.Any(l => l.chave == partes[0])) s.Layout = partes[0];
        s.Fundo = partes.Length > 1 && partes[1] != "" ? "#" + partes[1] : "";
        foreach (var e in s.Elementos.Where(e => e.Tipo is "texto" or "forma" && e.Html != ""))
        {
            var corBase = e.Tipo == "forma" ? (e.Preench != "" && Apresentacao.Escura(e.Preench) ? "#FFFFFF" : "#111827") : t.TextoDe(s, e);
            var fonte = t.FonteDe(e);
            var ps = TextoRico.Ler(e.Html);
            foreach (var p in ps)
            {
                if (e.Tipo == "forma" && p.Alinh == "center") p.Alinh = "";
                foreach (var r in p.Trechos)
                {
                    if (r.Cor.Equals(corBase, StringComparison.OrdinalIgnoreCase)) r.Cor = "";
                    if (r.Fonte == fonte) r.Fonte = "";
                    if (e.Papel == "titulo") r.N = false;
                }
            }
            e.Html = TextoRico.Html(ps);
            if (e.Papel != "" && e.Tam == Apresentacao.TamPadrao(e.Papel)) e.Tam = 0;
        }
    }

    static XElement? Ph(XElement el) => el.Descendants(P + "nvPr").FirstOrDefault()?.Element(P + "ph");

    static XElement? Herdada(List<(string? tipo, string? idx, XElement sp)> phs, XElement? ph)
    {
        if (ph == null) return null;
        var tipo = (string?)ph.Attribute("type"); var idx = (string?)ph.Attribute("idx");
        string Norm(string? t) => t switch { "ctrTitle" => "title", null => "body", "obj" => "body", _ => t };
        return phs.FirstOrDefault(p => idx != null && p.idx == idx && p.sp.Element(P + "spPr")?.Element(A + "xfrm") != null).sp
            ?? phs.FirstOrDefault(p => Norm(p.tipo) == Norm(tipo) && p.sp.Element(P + "spPr")?.Element(A + "xfrm") != null).sp
            ?? (Norm(tipo) == "title" ? phs.FirstOrDefault(p => p.tipo is "title" or "ctrTitle").sp : null);
    }

    /// <summary>Posição em EMU (antes da escala do slide).</summary>
    static bool Posicao(XElement? xf, Elemento e)
    {
        if (xf?.Element(A + "off") is not { } off || xf.Element(A + "ext") is not { } ext) return false;
        // em EMU; a escala do slide (L.K, px por EMU) é aplicada depois
        e.X = (double?)off.Attribute("x") ?? 0; e.Y = (double?)off.Attribute("y") ?? 0;
        e.W = (double?)ext.Attribute("cx") ?? 0; e.H = (double?)ext.Attribute("cy") ?? 0;
        e.Rot = ((double?)xf.Attribute("rot") ?? 0) / 60000;
        if ((string?)xf.Attribute("flipH") == "1" || (string?)xf.Attribute("flipV") == "1") { }
        return true;
    }

    static Elemento? Forma(Leitura L, Slide s, XElement sp, List<(string? tipo, string? idx, XElement sp)> phs, bool conector)
    {
        var ph = Ph(sp);
        var tipoPh = (string?)ph?.Attribute("type");
        if (tipoPh is "dt" or "ftr" or "sldNum" or "hdr") return null;
        var e = new Elemento();
        var spPr = sp.Element(P + "spPr");
        var herd = Herdada(phs, ph);
        if (!Posicao(spPr?.Element(A + "xfrm"), e) && !Posicao(herd?.Element(P + "spPr")?.Element(A + "xfrm"), e)) return null;

        e.Papel = ph == null ? "" : tipoPh switch { "title" or "ctrTitle" => "titulo", "subTitle" => "subtitulo", "body" or "obj" or null => "corpo", _ => "" };
        if (L.Nosso != null && ph == null)
        {
            var nm = (string?)sp.Descendants(P + "cNvPr").FirstOrDefault()?.Attribute("name") ?? "";
            e.Papel = nm.StartsWith("Título ") ? "titulo" : nm.StartsWith("Subtítulo ") ? "subtitulo" : nm.StartsWith("Texto ") ? "corpo" : "";
        }
        var geo = (string?)spPr?.Element(A + "prstGeom")?.Attribute("prst") ?? (conector ? "line" : "rect");
        var preench = spPr == null ? null : spPr.Element(A + "noFill") != null ? "" : Preench(L, spPr);
        if (preench == null && sp.Element(P + "style")?.Element(A + "fillRef") is { } fr && (string?)fr.Attribute("idx") != "0" && ph == null) preench = CorDe(L, fr);
        var ln = spPr?.Element(A + "ln");
        string? borda = ln == null ? null : ln.Element(A + "noFill") != null ? "" : Preench(L, ln);
        if (borda == null && ln?.Element(A + "noFill") == null && sp.Element(P + "style")?.Element(A + "lnRef") is { } lr && (string?)lr.Attribute("idx") != "0" && ph == null) borda = CorDe(L, lr);
        bool ehForma = conector || geo != "rect" || !string.IsNullOrEmpty(preench) || !string.IsNullOrEmpty(borda);
        var mapaGeo = Geo.ToDictionary(k => k.Value, k => k.Key);
        mapaGeo["roundRect"] = "arred"; mapaGeo["straightConnector1"] = "linha"; mapaGeo["bentConnector3"] = "linha"; mapaGeo["flowChartProcess"] = "ret";
        mapaGeo["flowChartAlternateProcess"] = "arred"; mapaGeo["star4"] = "estrela"; mapaGeo["star6"] = "estrela"; mapaGeo["flowChartDecision"] = "losango";
        mapaGeo["chevron"] = "seta"; mapaGeo["homePlate"] = "seta"; mapaGeo["leftArrow"] = "seta"; mapaGeo["flowChartConnector"] = "elipse"; mapaGeo["snip1Rect"] = "ret"; mapaGeo["round2SameRect"] = "arred";
        if (ehForma)
        {
            e.Tipo = "forma";
            e.Forma = mapaGeo.GetValueOrDefault(geo, "ret");
            e.Preench = preench ?? "";
            e.Borda = borda ?? "";
            e.BordaL = e.Borda != "" ? Math.Max(1, ((double?)ln?.Attribute("w") ?? 12700) / 12700) : 0;
            if (e.Forma == "linha") { e.BordaL = Math.Max(1, ((double?)ln?.Attribute("w") ?? 12700) / 12700); if (e.Borda == "") e.Borda = "#000000"; }
        }
        else e.Tipo = "texto";

        var corpo = sp.Element(P + "txBody");
        if (corpo != null)
        {
            var bp = corpo.Element(A + "bodyPr");
            var anc = (string?)bp?.Attribute("anchor") ?? (string?)herd?.Element(P + "txBody")?.Element(A + "bodyPr")?.Attribute("anchor");
            e.AlinhV = anc switch { "ctr" => "m", "b" => "b", _ => e.Tipo == "forma" ? "m" : (e.Papel == "titulo" && anc == null ? "m" : "t") };
            double tamBase = e.Papel == "titulo" ? L.TamTit : e.Papel is "corpo" or "subtitulo" ? (e.Papel == "subtitulo" ? Math.Min(L.TamCorpo, 24) : L.TamCorpo) : L.TamOutro;
            // tamanho definido no espaço reservado do layout
            var lstSz = (double?)herd?.Element(P + "txBody")?.Element(A + "lstStyle")?.Element(A + "lvl1pPr")?.Element(A + "defRPr")?.Attribute("sz");
            if (lstSz != null) tamBase = lstSz.Value / 100;
            var algnHerdado = (string?)corpo.Element(A + "lstStyle")?.Element(A + "lvl1pPr")?.Attribute("algn")
                ?? (string?)herd?.Element(P + "txBody")?.Element(A + "lstStyle")?.Element(A + "lvl1pPr")?.Attribute("algn")
                ?? (tipoPh is "ctrTitle" or "subTitle" ? "ctr" : null);
            var escala = (double?)bp?.Element(A + "normAutofit")?.Attribute("fontScale") / 100000 ?? 1;
            var ps = new List<Paragrafo>();
            foreach (var p in corpo.Elements(A + "p"))
            {
                var pPr = p.Element(A + "pPr");
                var par = new Paragrafo();
                var algn = (string?)pPr?.Attribute("algn") ?? algnHerdado;
                par.Alinh = algn switch { "ctr" => "center", "r" => "right", "just" => "justify", _ => "" };
                par.Nivel = (int?)pPr?.Attribute("lvl") ?? 0;
                if (pPr?.Element(A + "buNone") != null) par.Lista = "";
                else if (pPr?.Element(A + "buAutoNum") != null) par.Lista = "ol";
                else if (pPr?.Element(A + "buChar") != null) par.Lista = "ul";
                else par.Lista = e.Papel == "corpo" ? "ul" : "";
                foreach (var r in p.Elements())
                {
                    if (r.Name == A + "br") { ps.Add(par); par = new Paragrafo { Alinh = par.Alinh, Lista = "", Nivel = par.Nivel }; continue; }
                    if (r.Name != A + "r" && r.Name != A + "fld") continue;
                    var rp = r.Element(A + "rPr");
                    var t = new Trecho { Texto = r.Element(A + "t")?.Value ?? "" };
                    if (t.Texto == "") continue;
                    t.N = (string?)rp?.Attribute("b") == "1"; t.I = (string?)rp?.Attribute("i") == "1";
                    t.U = rp?.Attribute("u") is { } u && u.Value != "none"; t.S = rp?.Attribute("strike") is { } st && st.Value != "noStrike";
                    var sz = (double?)rp?.Attribute("sz");
                    t.Tam = Math.Round((sz != null ? sz.Value / 100 : tamBase) * escala * L.K * 12700, 1);
                    if (rp != null && Preench(L, rp) is { } cor) t.Cor = cor;
                    var fonte = (string?)rp?.Element(A + "latin")?.Attribute("typeface");
                    if (fonte != null && !fonte.StartsWith("+")) t.Fonte = fonte;
                    par.Trechos.Add(t);
                }
                ps.Add(par);
            }
            // um parágrafo vazio de lista no fim não serve para nada
            while (ps.Count > 1 && ps[^1].Trechos.Count == 0) ps.RemoveAt(ps.Count - 1);
            if (ps.All(p => p.Trechos.Count == 0) && e.Tipo == "texto" && ph == null) return null;
            // tamanho base = o mais comum (para o editor); cada trecho guarda o seu
            var tams = ps.SelectMany(p => p.Trechos).Select(t => t.Tam).ToList();
            e.Tam = tams.Count > 0 ? tams.GroupBy(t => t).OrderByDescending(gr => gr.Count()).First().Key : Math.Round(tamBase * L.K * 12700, 1);
            foreach (var t in ps.SelectMany(p => p.Trechos)) if (t.Tam == e.Tam) t.Tam = 0;
            e.Tam /= L.K * 12700;   // volta para pt — a escala geral (pt → px) é aplicada depois
            foreach (var t in ps.SelectMany(p => p.Trechos)) if (t.Tam > 0) t.Tam = Math.Round(t.Tam, 1);
            e.Html = TextoRico.Html(ps);
        }
        else if (e.Tipo == "texto") return null;
        return e;
    }

    static string? Imagem(Leitura L, Dictionary<string, (string tipo, string alvo)> rels, string? rid)
    {
        if (rid == null || !rels.TryGetValue(rid, out var r) || r.tipo != "image") return null;
        var ext = Path.GetExtension(r.alvo).TrimStart('.').ToLowerInvariant();
        var mime = ext switch { "png" => "image/png", "jpg" or "jpeg" => "image/jpeg", "gif" => "image/gif", "webp" => "image/webp", "bmp" => "image/bmp", "svg" => "image/svg+xml", _ => null };
        if (mime == null) return null;
        var b = Bytes(L.Zip, r.alvo);
        return b == null ? null : $"data:{mime};base64,{Convert.ToBase64String(b)}";
    }

    /// <summary>Cor de um solidFill/gradFill filho (ou do próprio elemento de cor).</summary>
    static string? Preench(Leitura L, XElement pai)
    {
        var sf = pai.Element(A + "solidFill");
        if (sf != null) return CorDe(L, sf);
        var gf = pai.Element(A + "gradFill");
        if (gf != null) return CorDe(L, gf.Descendants(A + "gs").FirstOrDefault() ?? gf);
        return null;
    }

    static string? CorDe(Leitura L, XElement el)
    {
        var c = el.Elements().FirstOrDefault(x => x.Name.LocalName is "srgbClr" or "schemeClr" or "sysClr" or "prstClr" or "scrgbClr");
        if (c == null) return null;
        string? hex = c.Name.LocalName switch
        {
            "srgbClr" => "#" + ((string?)c.Attribute("val") ?? "000000").ToUpperInvariant(),
            "sysClr" => "#" + ((string?)c.Attribute("lastClr") ?? "000000").ToUpperInvariant(),
            "prstClr" => ((string?)c.Attribute("val")) switch { "white" => "#FFFFFF", "black" => "#000000", "red" => "#FF0000", _ => "#808080" },
            "schemeClr" => L.Cores.GetValueOrDefault(L.Mapa.GetValueOrDefault((string?)c.Attribute("val") ?? "", (string?)c.Attribute("val") ?? "")) ?? L.Cores.GetValueOrDefault((string?)c.Attribute("val") ?? ""),
            _ => null,
        };
        if (hex == null || hex.Length != 7) return null;
        // ajustes de brilho mais comuns
        int v = int.Parse(hex[1..], NumberStyles.HexNumber);
        double r = (v >> 16) & 255, g = (v >> 8) & 255, b = v & 255;
        foreach (var m in c.Elements())
        {
            double val = ((double?)m.Attribute("val") ?? 100000) / 100000;
            switch (m.Name.LocalName)
            {
                case "lumMod": r *= val; g *= val; b *= val; break;
                case "lumOff": r += 255 * val; g += 255 * val; b += 255 * val; break;
                case "tint": r = 255 - (255 - r) * val; g = 255 - (255 - g) * val; b = 255 - (255 - b) * val; break;
                case "shade": r *= val; g *= val; b *= val; break;
            }
        }
        int Cl(double d) => (int)Math.Clamp(Math.Round(d), 0, 255);
        return $"#{Cl(r):X2}{Cl(g):X2}{Cl(b):X2}";
    }

    static bool LerGrafico(XDocument xc, Elemento e)
    {
        var plot = xc.Descendants(Cn + "plotArea").FirstOrDefault();
        var tipo = plot?.Elements().FirstOrDefault(el => el.Name.LocalName.EndsWith("Chart"));
        if (tipo == null) return false;
        e.Graf = tipo.Name.LocalName switch
        {
            "pieChart" or "pie3DChart" or "doughnutChart" => "pizza",
            "lineChart" or "line3DChart" or "areaChart" or "area3DChart" or "scatterChart" => "linhas",
            "barChart" or "bar3DChart" => (string?)tipo.Element(Cn + "barDir")?.Attribute("val") == "bar" ? "barras" : "colunas",
            _ => "colunas",
        };
        e.TituloGraf = string.Concat(xc.Root!.Element(Cn + "chart")?.Element(Cn + "title")?.Descendants(A + "t").Select(t => t.Value) ?? Enumerable.Empty<string>());
        List<string> Pts(XElement? el) => el == null ? new() : el.Descendants(Cn + "pt").OrderBy(p => (int?)p.Attribute("idx") ?? 0).Select(p => p.Element(Cn + "v")?.Value ?? "").ToList();
        foreach (var ser in tipo.Elements(Cn + "ser"))
        {
            var nome = string.Concat(ser.Element(Cn + "tx")?.Descendants(Cn + "v").Select(v => v.Value) ?? Enumerable.Empty<string>());
            var cats = Pts(ser.Element(Cn + "cat"));
            var valEl = ser.Element(Cn + "val") ?? ser.Element(Cn + "yVal");
            var pts = valEl?.Descendants(Cn + "pt").ToList() ?? new();
            int n = Math.Max(cats.Count, pts.Count == 0 ? 0 : pts.Max(p => (int?)p.Attribute("idx") ?? 0) + 1);
            var vals = Enumerable.Repeat<double?>(null, n).ToList();
            foreach (var p in pts)
                if (double.TryParse(p.Element(Cn + "v")?.Value, NumberStyles.Float, Inv, out var d)) vals[(int?)p.Attribute("idx") ?? 0] = d;
            if (e.Cats.Count == 0) e.Cats = cats.Count > 0 ? cats : Enumerable.Range(1, n).Select(i => i.ToString()).ToList();
            e.Series.Add(new Serie { Nome = nome == "" ? $"Série {e.Series.Count + 1}" : nome, V = vals });
        }
        return e.Series.Count > 0;
    }
}
