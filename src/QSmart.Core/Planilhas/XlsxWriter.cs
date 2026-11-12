using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace QSmart.Core.Planilhas;

/// <summary>Gravador de .xlsx simples (negrito, preenchimento, congelar painéis, larguras), sem dependências.</summary>
public sealed class XlsxWriter
{
    public sealed class Aba
    {
        internal readonly XlsxWriter Wb;
        public string Nome { get; }
        internal readonly List<object?[]> Linhas = new();
        internal readonly Dictionary<long, int> Estilos = new();       // (linha<<20 | col) -> estilo
        internal readonly Dictionary<int, int> EstiloLinha = new();
        internal readonly Dictionary<int, double> Larguras = new();
        internal (int linhas, int cols)? Congela;
        internal Aba(XlsxWriter wb, string nome) { Wb = wb; Nome = nome; }

        public int N => Linhas.Count;
        public void Linha(params object?[] valores) => Linhas.Add(valores);
        public void Cabecalho(params object?[] valores) => Linhas.Add(valores);
        public void NegritoLinha(int linha) => EstiloLinha[linha] = Wb.Estilo(true, null);
        public void Congelar(int linhas, int cols) => Congela = (linhas, cols);
        public void Largura(int col, double largura) => Larguras[col] = largura;

        public void Preencher(int linha, int col, string corHex, bool negrito = false)
            => Estilos[((long)linha << 20) | (uint)col] = Wb.Estilo(negrito, corHex);

        public void PreencherLinha(int linha, string corHex)
        {
            int n = linha < Linhas.Count ? Linhas[linha].Length : 0;
            for (int c = 0; c < n; c++) Preencher(linha, c, corHex);
        }

        /// <summary>Largura automática: min(max, max(min, maior texto + 2)), olhando as primeiras 200 linhas.</summary>
        public void AutoLargura(double min, double max, int amostra = 200)
        {
            int nCol = Linhas.Count == 0 ? 0 : Linhas.Max(l => l.Length);
            for (int c = 0; c < nCol; c++)
            {
                int maior = 0;
                foreach (var l in Linhas.Take(amostra))
                    if (c < l.Length) maior = Math.Max(maior, Texto(l[c]).Length);
                Larguras[c] = Math.Min(max, Math.Max(min, maior + 2));
            }
        }

        static string Texto(object? v) => v switch
        {
            null => "",
            double d => d.ToString(CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };
    }

    readonly List<Aba> _abas = new();
    readonly List<(bool negrito, string? cor, bool data)> _estilos = new() { (false, null, false) };
    readonly Dictionary<string, int> _shared = new(StringComparer.Ordinal);
    readonly List<string> _sharedLista = new();

    public IReadOnlyList<Aba> Planilhas => _abas;

    public Aba Planilha(string nome)
    {
        nome = NomeValido(nome);
        var a = new Aba(this, nome);
        _abas.Add(a);
        return a;
    }

    string NomeValido(string n)
    {
        var b = new string(n.Select(c => "[]*?/\\:".Contains(c) ? '_' : c).ToArray());
        if (b.Length > 31) b = b[..31];
        if (b.Length == 0) b = "Planilha";
        var k = b;
        int i = 2;
        while (_abas.Any(a => string.Equals(a.Nome, k, StringComparison.OrdinalIgnoreCase)))
        {
            var suf = $"_{i++}";
            k = (b.Length + suf.Length > 31 ? b[..(31 - suf.Length)] : b) + suf;
        }
        return k;
    }

    internal int Estilo(bool negrito, string? cor, bool data = false)
    {
        var chave = (negrito, cor?.ToUpperInvariant(), data);
        var i = _estilos.IndexOf(chave);
        if (i >= 0) return i;
        _estilos.Add(chave);
        return _estilos.Count - 1;
    }

    int Shared(string s)
    {
        if (!_shared.TryGetValue(s, out var i))
        {
            i = _sharedLista.Count;
            _shared[s] = i;
            _sharedLista.Add(s);
        }
        return i;
    }

    static string Esc(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default:
                    if (c < 0x20 && c != '\t' && c != '\n' && c != '\r') sb.Append($"_x{(int)c:X4}_");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    public static string ColunaLetra(int col)
    {
        var s = "";
        col++;
        while (col > 0)
        {
            int r = (col - 1) % 26;
            s = (char)('A' + r) + s;
            col = (col - 1) / 26;
        }
        return s;
    }

    byte[] FolhaXml(Aba a)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        if (a.Congela is { } cg && (cg.linhas > 0 || cg.cols > 0))
        {
            var cel = ColunaLetra(cg.cols) + (cg.linhas + 1);
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ");
            if (cg.cols > 0) sb.Append($"xSplit=\"{cg.cols}\" ");
            if (cg.linhas > 0) sb.Append($"ySplit=\"{cg.linhas}\" ");
            var painel = cg.linhas > 0 && cg.cols > 0 ? "bottomRight" : cg.linhas > 0 ? "bottomLeft" : "topRight";
            sb.Append($"topLeftCell=\"{cel}\" activePane=\"{painel}\" state=\"frozen\"/><selection pane=\"{painel}\" activeCell=\"{cel}\" sqref=\"{cel}\"/></sheetView></sheetViews>");
        }
        if (a.Larguras.Count > 0)
        {
            sb.Append("<cols>");
            foreach (var kv in a.Larguras.OrderBy(k => k.Key))
                sb.Append($"<col min=\"{kv.Key + 1}\" max=\"{kv.Key + 1}\" width=\"{kv.Value.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            sb.Append("</cols>");
        }
        sb.Append("<sheetData>");
        for (int r = 0; r < a.Linhas.Count; r++)
        {
            var l = a.Linhas[r];
            sb.Append("<row r=\"").Append(r + 1).Append("\">");
            a.EstiloLinha.TryGetValue(r, out var estLinha);
            for (int c = 0; c < l.Length; c++)
            {
                var v = l[c];
                int est = a.Estilos.TryGetValue(((long)r << 20) | (uint)c, out var e) ? e : estLinha;
                if (v == null || (v is string vs0 && vs0.Length == 0) || (v is double dd0 && double.IsNaN(dd0)))
                {
                    if (est != 0) sb.Append($"<c r=\"{ColunaLetra(c)}{r + 1}\" s=\"{est}\"/>");
                    continue;
                }
                var refc = ColunaLetra(c) + (r + 1);
                var sAttr = est != 0 ? $" s=\"{est}\"" : "";
                switch (v)
                {
                    case string s:
                        sb.Append($"<c r=\"{refc}\" t=\"s\"{sAttr}><v>{Shared(s)}</v></c>");
                        break;
                    case bool b:
                        sb.Append($"<c r=\"{refc}\" t=\"b\"{sAttr}><v>{(b ? 1 : 0)}</v></c>");
                        break;
                    case int or long or short:
                        sb.Append($"<c r=\"{refc}\"{sAttr}><v>{Convert.ToInt64(v).ToString(CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case double or float or decimal:
                        var d = Convert.ToDouble(v);
                        sb.Append($"<c r=\"{refc}\"{sAttr}><v>{d.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case DateTime dt:
                        var serial = (dt - new DateTime(1899, 12, 30)).TotalDays;
                        var estD = Estilo(est != 0 && _estilos[est].negrito, est != 0 ? _estilos[est].cor : null, true);
                        sb.Append($"<c r=\"{refc}\" s=\"{estD}\"><v>{serial.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                        break;
                    case Celula cel:
                        if (cel.Tipo is TipoCelula.Inteiro or TipoCelula.Numero)
                            sb.Append($"<c r=\"{refc}\"{sAttr}><v>{cel.Numero.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                        else if (cel.Tipo == TipoCelula.Bool)
                            sb.Append($"<c r=\"{refc}\" t=\"b\"{sAttr}><v>{(cel.Numero != 0 ? 1 : 0)}</v></c>");
                        else if (cel.Tem)
                            sb.Append($"<c r=\"{refc}\" t=\"s\"{sAttr}><v>{Shared(cel.Texto!)}</v></c>");
                        break;
                    default:
                        sb.Append($"<c r=\"{refc}\" t=\"s\"{sAttr}><v>{Shared(v.ToString() ?? "")}</v></c>");
                        break;
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");
        sb.Append("<pageMargins left=\"0.7\" right=\"0.7\" top=\"0.75\" bottom=\"0.75\" header=\"0.3\" footer=\"0.3\"/></worksheet>");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    public byte[] Salvar()
    {
        if (_abas.Count == 0) Planilha("Planilha");
        // normaliza células do tipo Celula com data para DateTime
        foreach (var a in _abas)
            foreach (var l in a.Linhas)
                for (int c = 0; c < l.Length; c++)
                    if (l[c] is Celula cel && cel.Tipo == TipoCelula.Data) l[c] = cel.Data;

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var folhas = _abas.Select(FolhaXml).ToList();
            void Add(string nome, string conteudo) => Add2(nome, Encoding.UTF8.GetBytes(conteudo));
            void Add2(string nome, byte[] conteudo)
            {
                var e = zip.CreateEntry(nome, CompressionLevel.Fastest);
                using var s = e.Open();
                s.Write(conteudo);
            }
            var sbCt = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/xl/sharedStrings.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml\"/><Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");
            for (int i = 0; i < _abas.Count; i++)
                sbCt.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            sbCt.Append("</Types>");
            Add("[Content_Types].xml", sbCt.ToString());
            Add("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>");
            var agora = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            Add("docProps/core.xml", $"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"><dc:creator>QSmart · Insi</dc:creator><dcterms:created xsi:type=\"dcterms:W3CDTF\">{agora}</dcterms:created></cp:coreProperties>");
            var sbWb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><bookViews><workbookView/></bookViews><sheets>");
            var sbRels = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
            for (int i = 0; i < _abas.Count; i++)
            {
                sbWb.Append($"<sheet name=\"{Esc(_abas[i].Nome)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
                sbRels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
            }
            sbWb.Append("</sheets></workbook>");
            int n = _abas.Count;
            sbRels.Append($"<Relationship Id=\"rId{n + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
            sbRels.Append($"<Relationship Id=\"rId{n + 2}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings\" Target=\"sharedStrings.xml\"/></Relationships>");
            Add("xl/workbook.xml", sbWb.ToString());
            Add("xl/_rels/workbook.xml.rels", sbRels.ToString());
            for (int i = 0; i < folhas.Count; i++) Add2($"xl/worksheets/sheet{i + 1}.xml", folhas[i]);
            Add("xl/styles.xml", EstilosXml());
            var sbS = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"{_sharedLista.Count}\" uniqueCount=\"{_sharedLista.Count}\">");
            foreach (var s in _sharedLista)
            {
                var esp = s.Length > 0 && (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])) ? " xml:space=\"preserve\"" : "";
                sbS.Append($"<si><t{esp}>{Esc(s)}</t></si>");
            }
            sbS.Append("</sst>");
            Add("xl/sharedStrings.xml", sbS.ToString());
        }
        return ms.ToArray();
    }

    string EstilosXml()
    {
        var cores = _estilos.Where(e => e.cor != null).Select(e => e.cor!).Distinct().ToList();
        var sb = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append("<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy hh:mm:ss\"/></numFmts>");
        sb.Append("<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/><family val=\"2\"/></font></fonts>");
        sb.Append($"<fills count=\"{2 + cores.Count}\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>");
        foreach (var c in cores) sb.Append($"<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF{c}\"/><bgColor indexed=\"64\"/></patternFill></fill>");
        sb.Append("</fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>");
        sb.Append("<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>");
        sb.Append($"<cellXfs count=\"{_estilos.Count}\">");
        foreach (var (neg, cor, data) in _estilos)
        {
            int fill = cor == null ? 0 : 2 + cores.IndexOf(cor);
            sb.Append($"<xf numFmtId=\"{(data ? 164 : 0)}\" fontId=\"{(neg ? 1 : 0)}\" fillId=\"{fill}\" borderId=\"0\" xfId=\"0\"" +
                      $"{(neg ? " applyFont=\"1\"" : "")}{(cor != null ? " applyFill=\"1\"" : "")}{(data ? " applyNumberFormat=\"1\"" : "")}/>");
        }
        sb.Append("</cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
        return sb.ToString();
    }
}
