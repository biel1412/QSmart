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
            var sharedCel = compartilhadas.Select(Texto).ToArray();
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
                await prog.Talvez();
                using (var s = e.Open())
                    folhas.Add(await LeitorFolhaXlsx.Ler(s, nome, sharedCel, estilos, data1904, op, prog));
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
        bool jaPosicionado = false;      // ReadElementContentAsString já avança para o nó seguinte
        while (jaPosicionado || r.Read())
        {
            jaPosicionado = false;
            if (r.NodeType == XmlNodeType.Element)
            {
                if (r.LocalName == "si") { atual = new StringBuilder(); if (r.IsEmptyElement) { lista.Add(""); atual = null; } }
                else if (r.LocalName == "rPh" && !r.IsEmptyElement) nivelRph++;
                else if (r.LocalName == "t" && atual != null && nivelRph == 0 && !r.IsEmptyElement)
                {
                    atual.Append(r.ReadElementContentAsString());
                    jaPosicionado = true;
                }
            }
            else if (r.NodeType == XmlNodeType.EndElement)
            {
                if (r.LocalName == "si" && atual != null) { lista.Add(atual.ToString()); atual = null; }
                else if (r.LocalName == "rPh") nivelRph = Math.Max(0, nivelRph - 1);
            }
        }
        return lista;
    }

    internal enum Formato : byte { Geral, Data, Hora }

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

    internal static string Decodificar(ReadOnlySpan<byte> b)
    {
        if (b.Length == 0) return "";
        var s = Encoding.UTF8.GetString(b);
        if (b.IndexOfAny((byte)'&', (byte)'_', (byte)'\r') < 0) return s;
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

    internal static Celula CelTexto(string? s) => Texto(s);

    static Celula Texto(string? s)
    {
        if (s == null || (s.Length <= 8 && ValoresNa.Contains(s))) return Celula.Vazia;
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
}
