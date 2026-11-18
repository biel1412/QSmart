using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using QSmart.Core.Planilhas;

namespace QSmart.Core.Coordenacao;

/// <summary>Um colaborador da planilha de controle de férias.</summary>
public sealed class FuncAuto
{
    public string Aba { get; set; } = "";
    public string Nome { get; set; } = "";
    public DateOnly? Admissao { get; set; }
    public int Linha { get; set; }
    public string Chave => Ferias.Norm(Aba) + "|" + Ferias.Norm(Nome);
    /// <summary>Fim do período aquisitivo (admissão + 1 ano − 1 dia).</summary>
    public DateOnly? FimAquisitivo => Admissao?.AddYears(1).AddDays(-1);
    /// <summary>Fim do período concessivo (12 meses depois do aquisitivo).</summary>
    public DateOnly? FimConcessivo => FimAquisitivo?.AddDays(1).AddYears(1).AddDays(-1);
}

/// <summary>Um período de férias (uma linha da planilha com data de início).</summary>
public sealed class PeriodoAuto
{
    public int Id { get; set; }
    public FuncAuto Func { get; set; } = new();
    public DateOnly Inicio { get; set; }
    public DateOnly InicioOriginal { get; set; }
    public int Dias { get; set; }
    public string Vende { get; set; } = "";
    public string Status { get; set; } = "";
    public string Obs { get; set; } = "";
    /// <summary>Linha do Excel (1 = primeira) e colunas de início e observações, para gravar de volta.</summary>
    public int Linha { get; set; }
    public string ColInicio { get; set; } = "K";
    public string? ColObs { get; set; }
    public DateOnly Fim => Inicio.AddDays(Math.Max(1, Dias) - 1);
    public DateOnly Retorno => Inicio.AddDays(Math.Max(1, Dias));
    public bool Alterado => Inicio != InicioOriginal;
    public string Aba => Func.Aba;
    public string Nome => Func.Nome;
}

/// <summary>Sugestão de nova data para um período em choque.</summary>
public sealed record SugestaoAuto(PeriodoAuto Periodo, DateOnly NovoInicio, int ChoqueAntes, int ChoqueDepois, int TimeAntes, int TimeDepois,
    List<(string nome, int dias)> ComQuem, int ProximoDepois)
{
    public DateOnly NovoFim => NovoInicio.AddDays(Math.Max(1, Periodo.Dias) - 1);
    public int Deslocamento => NovoInicio.DayNumber - Periodo.Inicio.DayNumber;
    public double Reducao => TimeAntes == 0 ? 0 : (TimeAntes - TimeDepois) * 100.0 / TimeAntes;
}

public sealed class BaseAuto
{
    public string Arquivo { get; set; } = "";
    public byte[] Original { get; set; } = Array.Empty<byte>();
    public List<FuncAuto> Funcs { get; } = new();
    public List<PeriodoAuto> Periodos { get; } = new();
    public List<string> Problemas { get; } = new();
    /// <summary>Aba → caminho do XML da aba dentro do arquivo.</summary>
    public Dictionary<string, string> CaminhoAba { get; } = new();
    /// <summary>Dias de choque somados (todas as abas) logo depois de anexar.</summary>
    public int ChoqueInicial { get; set; }
    /// <summary>Períodos que a pessoa decidiu manter mesmo em choque.</summary>
    public HashSet<int> Mantidos { get; } = new();
    public List<string> Abas => Funcs.Select(f => f.Aba).Distinct().ToList();
}

/// <summary>Controle automatizado de férias: lê a planilha de controle, acha choques e sugere datas melhores.</summary>
public static class FeriasAuto
{
    public const int ChoqueLongo = 10;
    static readonly DateOnly Base1900 = new(1899, 12, 30);

    // ======================== leitura ========================

    public static BaseAuto Ler(byte[] dados, string nomeArquivo)
    {
        var b = new BaseAuto { Arquivo = nomeArquivo, Original = dados };
        using var ms = new MemoryStream(dados);
        ZipArchive zip;
        try { zip = new ZipArchive(ms, ZipArchiveMode.Read); }
        catch { throw new InvalidDataException("O arquivo não é uma planilha .xlsx válida."); }
        using (zip)
        {
            var shared = LerShared(zip.GetEntry("xl/sharedStrings.xml"));
            foreach (var (nome, caminho) in LerAbas(zip))
            {
                var e = zip.GetEntry(caminho);
                if (e == null) continue;
                var celulas = LerCelulas(e, shared);
                if (LerAba(b, nome, celulas)) b.CaminhoAba[nome] = caminho;
            }
        }
        if (b.Funcs.Count == 0)
            throw new InvalidDataException("Não achei o cabeçalho do controle de férias (Nome do Funcionario, Data de Admissao, Data Inicio Ferias, Dias de Ferias). Use o modelo em branco desta aba.");
        int id = 1;
        foreach (var p in b.Periodos) p.Id = id++;
        b.ChoqueInicial = ChoqueTotal(b);
        return b;
    }

    internal static List<(string nome, string caminho)> LerAbas(ZipArchive zip)
    {
        var rels = new Dictionary<string, string>();
        var re = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (re != null)
            using (var r = XmlReader.Create(re.Open()))
                while (r.Read())
                    if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship")
                    {
                        var t = r.GetAttribute("Target") ?? "";
                        t = t.StartsWith("/") ? t.TrimStart('/') : "xl/" + t;
                        rels[r.GetAttribute("Id") ?? ""] = t;
                    }
        var l = new List<(string, string)>();
        var we = zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("Planilha sem workbook.xml.");
        using (var r = XmlReader.Create(we.Open()))
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "sheet")
                {
                    var rid = r.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships") ?? "";
                    if (rels.TryGetValue(rid, out var c)) l.Add((r.GetAttribute("name") ?? "", c));
                }
        return l;
    }

    internal static List<string> LerShared(ZipArchiveEntry? e)
    {
        var l = new List<string>();
        if (e == null) return l;
        using var r = XmlReader.Create(e.Open());
        StringBuilder? sb = null;
        bool emRph = false;
        bool ler = true;
        while (!ler || r.Read())
        {
            ler = true;
            if (r.NodeType == XmlNodeType.Element)
            {
                if (r.LocalName == "si") sb = new StringBuilder();
                else if (r.LocalName == "rPh") emRph = !r.IsEmptyElement;
                else if (r.LocalName == "t" && sb != null && !emRph && !r.IsEmptyElement) { sb.Append(r.ReadElementContentAsString()); ler = false; }
            }
            else if (r.NodeType == XmlNodeType.EndElement)
            {
                if (r.LocalName == "si" && sb != null) { l.Add(sb.ToString()); sb = null; }
                else if (r.LocalName == "rPh") emRph = false;
            }
        }
        return l;
    }

    /// <summary>Células da aba: (linha, coluna 0-based) → (texto, número).</summary>
    internal static Dictionary<(int, int), (string? txt, double? num)> LerCelulas(ZipArchiveEntry e, List<string> shared)
    {
        var d = new Dictionary<(int, int), (string?, double?)>();
        using var r = XmlReader.Create(e.Open());
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.LocalName != "c") continue;
            var refc = r.GetAttribute("r") ?? "";
            var t = r.GetAttribute("t");
            var (lin, col) = Ref(refc);
            if (r.IsEmptyElement || lin < 0) continue;
            string? v = null, inl = null;
            var sub = r.ReadSubtree();
            bool ler = true;
            while (!ler || sub.Read())
            {
                ler = true;
                if (sub.NodeType != XmlNodeType.Element) continue;
                if (sub.LocalName == "v") { v = sub.ReadElementContentAsString(); ler = false; }
                else if (sub.LocalName == "t") { inl = (inl ?? "") + sub.ReadElementContentAsString(); ler = false; }
            }
            if (t == "s" && int.TryParse(v, out var si) && si < shared.Count) d[(lin, col)] = (shared[si], null);
            else if (t == "inlineStr") d[(lin, col)] = (inl, null);
            else if (t is "str" or "e") d[(lin, col)] = (v, null);
            else if (t == "b") d[(lin, col)] = (v == "1" ? "TRUE" : "FALSE", null);
            else if (v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) d[(lin, col)] = (null, n);
        }
        return d;
    }

    public static (int linha, int col) Ref(string r)
    {
        int i = 0, col = 0;
        while (i < r.Length && char.IsLetter(r[i])) { col = col * 26 + (char.ToUpperInvariant(r[i]) - 'A' + 1); i++; }
        return int.TryParse(r[i..], out var lin) ? (lin, col - 1) : (-1, -1);
    }

    public static string Letra(int col)
    {
        var s = ""; col++;
        while (col > 0) { int m = (col - 1) % 26; s = (char)('A' + m) + s; col = (col - 1) / 26; }
        return s;
    }

    internal static DateOnly? Data((string? txt, double? num) v)
    {
        if (v.num is double n && n > 20000 && n < 80000) return Base1900.AddDays((int)Math.Floor(n));
        if (!string.IsNullOrWhiteSpace(v.txt) && DateOnly.TryParseExact(v.txt.Trim(), new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        return null;
    }

    static int? Inteiro((string? txt, double? num) v)
        => v.num is double n ? (int)Math.Round(n) : int.TryParse(v.txt?.Trim(), out var i) ? i : null;

    internal static string Txt((string? txt, double? num) v) => (v.txt ?? (v.num?.ToString(CultureInfo.InvariantCulture) ?? "")).Trim();

    static bool LerAba(BaseAuto b, string aba, Dictionary<(int, int), (string? txt, double? num)> cel)
    {
        // acha a linha do cabeçalho: a que tem "Nome do Funcionario" e "Data Inicio"
        int cab = -1;
        foreach (var lin in cel.Keys.Select(k => k.Item1).Distinct().OrderBy(x => x).Take(40))
        {
            var textos = cel.Where(k => k.Key.Item1 == lin).Select(k => Ferias.Norm(k.Value.txt)).ToList();
            if (textos.Any(t => t.StartsWith("nome")) && textos.Any(t => t.Contains("inicio") && t.Contains("ferias"))) { cab = lin; break; }
        }
        if (cab < 0) return false;
        var cols = cel.Where(k => k.Key.Item1 == cab).ToDictionary(k => k.Key.Item2, k => Ferias.Norm(k.Value.txt));
        int Col(Func<string, bool> f) => cols.Where(k => f(k.Value)).Select(k => k.Key).DefaultIfEmpty(-1).First();
        int cNome = Col(t => t.StartsWith("nome")), cAdm = Col(t => t.Contains("admiss")),
            cIni = Col(t => t.Contains("inicio") && t.Contains("ferias")), cDias = Col(t => t.Contains("dias de") || (t.StartsWith("dias") && t.Contains("ferias"))),
            cVende = Col(t => t.Contains("vender")), cStatus = Col(t => t.Contains("agendamento") || t.StartsWith("status ferias")), cObs = Col(t => t.StartsWith("observ"));
        if (cNome < 0 || cIni < 0 || cDias < 0) return false;
        (string?, double?) V(int l, int c) => c >= 0 && cel.TryGetValue((l, c), out var v) ? v : (null, null);

        int ultima = cel.Keys.Max(k => k.Item1);
        FuncAuto? atual = null;
        var vistos = new HashSet<string>();
        for (int l = cab + 1; l <= ultima; l++)
        {
            var nome = Txt(V(l, cNome));
            var ini = Data(V(l, cIni));
            if (nome != "")
            {
                atual = new FuncAuto { Aba = aba, Nome = nome, Admissao = Data(V(l, cAdm)), Linha = l };
                if (!vistos.Add(Ferias.Norm(nome))) b.Problemas.Add($"{aba}, linha {l}: “{nome}” aparece mais de uma vez.");
                b.Funcs.Add(atual);
                if (atual.Admissao == null && cAdm >= 0) b.Problemas.Add($"{aba}, linha {l}: {nome} sem data de admissão (não dá para calcular os prazos).");
            }
            if (ini == null) continue;
            if (atual == null) { b.Problemas.Add($"{aba}, linha {l}: férias sem nome do funcionário."); continue; }
            var dias = Inteiro(V(l, cDias));
            if (dias is null or < 1 or > 60) { b.Problemas.Add($"{aba}, linha {l}: {atual.Nome} com total de dias inválido."); continue; }
            b.Periodos.Add(new PeriodoAuto
            {
                Func = atual, Inicio = ini.Value, InicioOriginal = ini.Value, Dias = dias.Value,
                Vende = Txt(V(l, cVende)).ToUpperInvariant(), Status = Txt(V(l, cStatus)), Obs = Txt(V(l, cObs)),
                Linha = l, ColInicio = Letra(cIni), ColObs = cObs >= 0 ? Letra(cObs) : null,
            });
        }
        return true;
    }

    // ======================== choques ========================

    static int Sobrepoe(DateOnly a0, DateOnly a1, DateOnly b0, DateOnly b1)
    {
        var de = a0 > b0 ? a0 : b0; var ate = a1 < b1 ? a1 : b1;
        return ate >= de ? ate.DayNumber - de.DayNumber + 1 : 0;
    }

    /// <summary>Colegas do mesmo time (mesma aba), outra pessoa.</summary>
    static IEnumerable<PeriodoAuto> Colegas(BaseAuto b, PeriodoAuto p)
        => b.Periodos.Where(o => o != p && o.Aba == p.Aba && o.Func.Chave != p.Func.Chave);

    /// <summary>Dias em que este período fica fora junto com alguém do mesmo time (somando cada colega).</summary>
    public static int Choque(BaseAuto b, PeriodoAuto p, DateOnly? inicio = null)
    {
        var i0 = inicio ?? p.Inicio; var i1 = i0.AddDays(Math.Max(1, p.Dias) - 1);
        return Colegas(b, p).Sum(o => Sobrepoe(i0, i1, o.Inicio, o.Fim));
    }

    public static List<(string nome, int dias)> ComQuem(BaseAuto b, PeriodoAuto p, DateOnly? inicio = null)
    {
        var i0 = inicio ?? p.Inicio; var i1 = i0.AddDays(Math.Max(1, p.Dias) - 1);
        return Colegas(b, p).Select(o => (o.Nome, Sobrepoe(i0, i1, o.Inicio, o.Fim))).Where(x => x.Item2 > 0)
            .GroupBy(x => x.Nome).Select(g => (g.Key, g.Sum(x => x.Item2))).OrderByDescending(x => x.Item2).ToList();
    }

    /// <summary>Menor intervalo (dias livres) entre este período e o de um colega; 999 se não há ninguém perto.</summary>
    static int Intervalo(BaseAuto b, PeriodoAuto p, DateOnly i0)
    {
        var i1 = i0.AddDays(Math.Max(1, p.Dias) - 1);
        int m = 999;
        foreach (var o in Colegas(b, p))
        {
            if (Sobrepoe(i0, i1, o.Inicio, o.Fim) > 0) continue;
            int g = o.Inicio > i1 ? o.Inicio.DayNumber - i1.DayNumber - 1 : i0.DayNumber - o.Fim.DayNumber - 1;
            m = Math.Min(m, g);
        }
        return m;
    }

    /// <summary>Total de dias de choque somando todos os pares (cada par conta uma vez).</summary>
    public static int ChoqueTotal(BaseAuto b, string? aba = null)
    {
        int t = 0;
        var ps = b.Periodos.Where(p => aba == null || p.Aba == aba).ToList();
        for (int i = 0; i < ps.Count; i++)
            for (int j = i + 1; j < ps.Count; j++)
                if (ps[i].Aba == ps[j].Aba && ps[i].Func.Chave != ps[j].Func.Chave) t += Sobrepoe(ps[i].Inicio, ps[i].Fim, ps[j].Inicio, ps[j].Fim);
        return t;
    }

    /// <summary>Pares em choque (A começa antes ou junto).</summary>
    public static List<(PeriodoAuto a, PeriodoAuto b, int dias)> Pares(BaseAuto b)
    {
        var r = new List<(PeriodoAuto, PeriodoAuto, int)>();
        var ps = b.Periodos.OrderBy(p => p.Inicio).ToList();
        for (int i = 0; i < ps.Count; i++)
            for (int j = i + 1; j < ps.Count; j++)
                if (ps[i].Aba == ps[j].Aba && ps[i].Func.Chave != ps[j].Func.Chave && Sobrepoe(ps[i].Inicio, ps[i].Fim, ps[j].Inicio, ps[j].Fim) is int d && d > 0)
                    r.Add((ps[i], ps[j], d));
        return r.OrderByDescending(x => x.Item3).ToList();
    }

    // ======================== regras de data ========================

    public static bool Feriado(DateOnly d)
    {
        int y = d.Year;
        var fixos = new[] { (1, 1), (4, 21), (5, 1), (9, 7), (10, 12), (11, 2), (11, 15), (11, 20), (12, 25) };
        if (fixos.Any(f => f.Item1 == d.Month && f.Item2 == d.Day)) return true;
        // Sexta-feira Santa (Páscoa − 2)
        int a = y % 19, bb = y / 100, c = y % 100, dd = bb / 4, e = bb % 4, f2 = (bb + 8) / 25, g = (bb - f2 + 1) / 3, h = (19 * a + bb - dd - g + 15) % 30,
            i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451, mes = (h + l - 7 * m + 114) / 31, dia = (h + l - 7 * m + 114) % 31 + 1;
        var pascoa = new DateOnly(y, mes, dia);
        return d == pascoa.AddDays(-2);
    }

    /// <summary>Pode começar neste dia? Não pode no fim de semana, em feriado, nem nos 2 dias antes do descanso ou de feriado.</summary>
    public static bool InicioPermitido(DateOnly d)
        => d.DayOfWeek is not (DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday) && !Feriado(d) && !Feriado(d.AddDays(1)) && !Feriado(d.AddDays(2));

    // ======================== sugestão ========================

    /// <summary>Períodos que ainda podem mudar (não começaram e não estão como “Já tirou”).</summary>
    public static bool Ajustavel(PeriodoAuto p, DateOnly hoje) => p.Inicio > hoje && !Ferias.Norm(p.Status).StartsWith("ja tirou");

    /// <summary>
    /// Procura a data de início mais perto da original que tire (ou diminua) o choque com o time,
    /// respeitando: início em dia permitido, depois de hoje, sem bater com outro período da mesma pessoa
    /// e terminando dentro do período concessivo. Evita também ficar colado (menos de 3 dias) em outra pessoa.
    /// </summary>
    public static SugestaoAuto? Sugerir(BaseAuto b, PeriodoAuto p, DateOnly hoje) => Opcoes(b, p, hoje).FirstOrDefault();

    /// <summary>Até 3 opções: a melhor, a melhor antes e a melhor depois da data pedida (datas próximas).</summary>
    public static List<SugestaoAuto> Opcoes(BaseAuto b, PeriodoAuto p, DateOnly hoje)
    {
        var r = new List<SugestaoAuto>();
        int antes = Choque(b, p);
        if (antes == 0 || !Ajustavel(p, hoje)) return r;
        var limite = p.Func.FimConcessivo;
        var proprios = b.Periodos.Where(o => o != p && o.Func.Chave == p.Func.Chave).ToList();
        var cands = new List<(DateOnly d, int desl, int ch, double n)>();
        for (int desl = -60; desl <= 150; desl++)
        {
            if (desl == 0) continue;
            var i0 = p.Inicio.AddDays(desl);
            if (i0 <= hoje.AddDays(1) || !InicioPermitido(i0)) continue;
            var i1 = i0.AddDays(Math.Max(1, p.Dias) - 1);
            if (limite != null && i1 > limite && p.Fim <= limite) continue;           // não joga para fora do prazo
            if (proprios.Any(o => Sobrepoe(i0, i1.AddDays(1), o.Inicio.AddDays(-1), o.Fim) > 0)) continue;
            int ch = Choque(b, p, i0);
            if (ch >= antes) continue;
            int gap = Intervalo(b, p, i0);
            double n = ch * 60 + (gap < 3 ? (3 - gap) * 60 : 0) + (gap < 7 ? (7 - gap) * 8 : 0) + Math.Abs(desl) * (desl < 0 ? 1.3 : 1.0);
            cands.Add((i0, desl, ch, n));
        }
        if (cands.Count == 0) return r;
        var escolha = new List<DateOnly> { cands.MinBy(c => c.n).d };
        foreach (var lado in new[] { cands.Where(c => c.desl < 0), cands.Where(c => c.desl > 0) })
        {
            var l = lado.ToList();
            if (l.Count == 0) continue;
            // a mais perto que já reduz bem o choque (no máximo metade do atual), senão a melhor do lado
            var bom = l.Where(c => c.ch <= antes / 2).OrderBy(c => Math.Abs(c.desl)).ThenBy(c => c.n).FirstOrDefault();
            var d = bom != default ? bom.d : l.MinBy(c => c.n).d;
            if (!escolha.Contains(d)) escolha.Add(d);
        }
        int timeAntes = ChoqueTotal(b, p.Aba);
        var guarda = p.Inicio;
        foreach (var d in escolha)
        {
            p.Inicio = d;
            int timeDepois = ChoqueTotal(b, p.Aba);
            int ch = Choque(b, p);
            int gapDepois = Intervalo(b, p, d);
            p.Inicio = guarda;
            r.Add(new SugestaoAuto(p, d, antes, ch, timeAntes, timeDepois, ComQuem(b, p), gapDepois));
        }
        return r;
    }

    // ======================== prazos ========================

    public enum Prazo { SemAdmissao, Aquisitivo, PodeMarcar, ConcessivoPerto, Vencido }

    public static (Prazo tipo, int dias) SituacaoPrazo(FuncAuto f, DateOnly hoje)
    {
        if (f.FimAquisitivo is not DateOnly fa || f.FimConcessivo is not DateOnly fc) return (Prazo.SemAdmissao, 0);
        if (fa > hoje) return (Prazo.Aquisitivo, fa.DayNumber - hoje.DayNumber);
        int falta = fc.DayNumber - hoje.DayNumber;
        return falta < 0 ? (Prazo.Vencido, -falta) : falta <= 180 ? (Prazo.ConcessivoPerto, falta) : (Prazo.PodeMarcar, falta);
    }

    /// <summary>Lembretes das regras de fracionamento (Anotações da planilha).</summary>
    public static List<string> Regras(BaseAuto b, DateOnly? hoje = null)
    {
        var r = new List<string>();
        foreach (var g in b.Periodos.GroupBy(p => p.Func.Chave))
        {
            var ps = g.OrderBy(p => p.Inicio).ToList(); var f = ps[0].Func;
            foreach (var p in ps)
            {
                if (hoje != null && p.Fim < hoje) continue;                         // já passou: não adianta avisar
                if (p.Dias < 5) r.Add($"{f.Nome}: período de {p.Dias} dias em {p.Inicio:dd/MM/yyyy}. Cada período deve ter pelo menos 5 dias.");
                if (!InicioPermitido(p.Inicio)) r.Add($"{f.Nome}: começa em {p.Inicio:dd/MM/yyyy} ({Texto.DiaSemana(p.Inicio).ToLowerInvariant()}), no fim de semana, feriado ou nos 2 dias antes.");
                if (f.FimConcessivo is DateOnly fc && p.Fim > fc) r.Add($"{f.Nome}: as férias de {p.Inicio:dd/MM/yyyy} terminam depois do fim do período concessivo ({fc:dd/MM/yyyy}).");
            }
            if (ps.Count > 1 && ps.All(p => p.Dias < 14)) r.Add($"{f.Nome}: dividiu as férias e nenhum período tem 14 dias ou mais.");
            if (ps.Count > 3) r.Add($"{f.Nome}: {ps.Count} períodos. A CLT permite dividir em até 3.");
        }
        return r;
    }

    // ======================== gravar planilha ajustada ========================

    static string Xe(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>Devolve a planilha original com as datas alteradas (e uma nota nas observações). O resto fica igual.</summary>
    public static byte[] GerarAjustada(BaseAuto b)
    {
        var alt = b.Periodos.Where(p => p.Alterado).ToList();
        using var entrada = new MemoryStream(b.Original);
        using var zin = new ZipArchive(entrada, ZipArchiveMode.Read);
        using var saida = new MemoryStream();
        using (var zout = new ZipArchive(saida, ZipArchiveMode.Create, true))
        {
            foreach (var e in zin.Entries)
            {
                if (e.FullName == "xl/calcChain.xml") continue;
                string? texto = null;
                var aba = b.CaminhoAba.FirstOrDefault(k => k.Value == e.FullName).Key;
                if (aba != null && alt.Any(p => p.Aba == aba))
                {
                    using var sr = new StreamReader(e.Open(), Encoding.UTF8);
                    texto = sr.ReadToEnd();
                    foreach (var p in alt.Where(p => p.Aba == aba))
                    {
                        texto = LimparCalculados(texto, p.Linha);
                        texto = TrocarCelula(texto, p.ColInicio + p.Linha, $"<v>{p.Inicio.DayNumber - Base1900.DayNumber}</v>", null);
                        if (p.ColObs != null)
                        {
                            var nota = $"QSmart: férias mudadas de {p.InicioOriginal:dd/MM/yyyy} para {p.Inicio:dd/MM/yyyy}";
                            var obs = string.IsNullOrWhiteSpace(p.Obs) ? nota : p.Obs + " | " + nota;
                            texto = TrocarCelula(texto, p.ColObs + p.Linha, $"<is><t xml:space=\"preserve\">{Xe(obs)}</t></is>", "inlineStr");
                        }
                    }
                }
                else if (e.FullName == "xl/workbook.xml")
                {
                    using var sr = new StreamReader(e.Open(), Encoding.UTF8);
                    texto = sr.ReadToEnd();
                    texto = Regex.IsMatch(texto, "<calcPr\\b")
                        ? Regex.Replace(texto, "<calcPr\\b([^>]*?)(/?)>", m => "<calcPr" + Regex.Replace(m.Groups[1].Value, "\\s*fullCalcOnLoad=\"[^\"]*\"", "") + " fullCalcOnLoad=\"1\"" + m.Groups[2].Value + ">")
                        : texto.Replace("</sheets>", "</sheets><calcPr fullCalcOnLoad=\"1\"/>");
                }
                else if (e.FullName == "[Content_Types].xml")
                {
                    using var sr = new StreamReader(e.Open(), Encoding.UTF8);
                    texto = Regex.Replace(sr.ReadToEnd(), "<Override[^>]*calcChain[^>]*/>", "");
                }
                else if (e.FullName == "xl/_rels/workbook.xml.rels")
                {
                    using var sr = new StreamReader(e.Open(), Encoding.UTF8);
                    texto = Regex.Replace(sr.ReadToEnd(), "<Relationship[^>]*calcChain[^>]*/>", "");
                }
                var ne = zout.CreateEntry(e.FullName, CompressionLevel.Optimal);
                using var os = ne.Open();
                if (texto != null) { var by = new UTF8Encoding(false).GetBytes(texto); os.Write(by); }
                else using (var isr = e.Open()) isr.CopyTo(os);
            }
        }
        return saida.ToArray();
    }

    /// <summary>Tira o valor já calculado das fórmulas da linha, para o Excel recalcular (fim, retorno, mês…).</summary>
    static string LimparCalculados(string xml, int linha)
    {
        var rr = new Regex($"<row\\b(?=[^>]*\\br=\"{linha}\")[^>]*>(.*?)</row>", RegexOptions.Singleline);
        var m = rr.Match(xml);
        if (!m.Success) return xml;
        var corpo = Regex.Replace(m.Groups[1].Value, "(<f\\b[^>]*(?:/>|>.*?</f>))<v>.*?</v>", "$1", RegexOptions.Singleline);
        return xml[..m.Groups[1].Index] + corpo + xml[(m.Groups[1].Index + m.Groups[1].Length)..];
    }

    /// <summary>Troca o conteúdo da célula (mantendo o estilo); cria a célula na linha se ela não existir.</summary>
    static string TrocarCelula(string xml, string refc, string conteudo, string? tipo)
    {
        var re = new Regex($"<c\\b(?=[^>]*\\br=\"{refc}\")([^>]*?)(/>|>.*?</c>)", RegexOptions.Singleline);
        var tAttr = tipo != null ? $" t=\"{tipo}\"" : "";
        var m = re.Match(xml);
        if (m.Success)
        {
            var attrs = Regex.Replace(m.Groups[1].Value, "\\s+t=\"[^\"]*\"", "");
            return xml[..m.Index] + $"<c{attrs}{tAttr}>{conteudo}</c>" + xml[(m.Index + m.Length)..];
        }
        var (lin, col) = Ref(refc);
        var rr = new Regex($"<row\\b(?=[^>]*\\br=\"{lin}\")[^>]*?(/>|>(.*?)</row>)", RegexOptions.Singleline);
        var mr = rr.Match(xml);
        var nova = $"<c r=\"{refc}\"{tAttr}>{conteudo}</c>";
        if (!mr.Success) return xml;                                  // linha inexistente: não mexe
        if (mr.Groups[1].Value == "/>")
        {
            var abre = mr.Value[..^2] + ">";
            return xml[..mr.Index] + abre + nova + "</row>" + xml[(mr.Index + mr.Length)..];
        }
        var corpo = mr.Groups[2].Value;
        int pos = 0;
        foreach (Match c in Regex.Matches(corpo, "<c\\b[^>]*\\br=\"([A-Z]+)\\d+\"[^>]*?(?:/>|>.*?</c>)", RegexOptions.Singleline))
        {
            if (Ref(c.Groups[1].Value + "1").col > col) break;
            pos = c.Index + c.Length;
        }
        var ini = mr.Groups[2].Index;
        return xml[..(ini + pos)] + nova + xml[(ini + pos)..];
    }

    // ======================== modelo em branco ========================

    public static readonly string[] Cabecalho =
    {
        "Nome do Funcionario", "Data de Admissao", "Inicio Periodo Aquisitivo", "Fim Periodo Aquisitivo", "Dias p/ Fechar Aquisitivo", "Status Aquisitivo",
        "Inicio Periodo Concessivo", "Fim Periodo Concessivo", "Dias p/ Vencer Concessivo", "Status Concessivo", "Data Inicio\nFerias", "Dias de\nFerias",
        "Data Fim\nFerias", "Data\nRetorno", "Vai Vender\n(S/N)", "Mes das\nFerias", "Agendamento\nSenior", "Observações",
    };

    /// <summary>Planilha de controle de férias em branco, igual ao modelo da coordenação (fórmulas, listas e cores).</summary>
    public static byte[] Modelo(int linhas = 120)
    {
        var wb = new XlsxWriter();
        var s = wb.Planilha("Férias - Time");
        s.Linha("CONTROLE DE FERIAS");
        s.NegritoLinha(0);
        s.Linha("AZUL CLARO = Celulas de input (preencha aqui)   |   AMARELO = Aquisitivo fechando em ate 180 dias   |   VERMELHO = Concessivo vencendo em ate 180 dias   |   VERDE = Ferias agendadas");
        s.Linha(Cabecalho.Cast<object?>().ToArray());
        for (int c = 0; c < Cabecalho.Length; c++) s.Preencher(2, c, "1F3864", true);
        for (int i = 0; i < linhas; i++)
        {
            int r = i + 4;
            s.Linha(null, null,
                new Formula($"IF(B{r}=\"\",\"\",B{r})"),
                new Formula($"IF(B{r}=\"\",\"\",DATE(YEAR(B{r})+1,MONTH(B{r}),DAY(B{r}))-1)"),
                new Formula($"IF(D{r}=\"\",\"\",D{r}-TODAY())"),
                new Formula($"IF(B{r}=\"\",\"Aguardando admissao\",IF(E{r}<=0,\"PODE MARCAR FERIAS\",IF(E{r}<=180,\"FECHA EM \"&E{r}&\" DIAS\",E{r}&\" dias restantes\")))"),
                new Formula($"IF(D{r}=\"\",\"\",D{r}+1)"),
                new Formula($"IF(G{r}=\"\",\"\",DATE(YEAR(G{r})+1,MONTH(G{r}),DAY(G{r}))-1)"),
                new Formula($"IF(H{r}=\"\",\"\",H{r}-TODAY())"),
                new Formula($"IF(H{r}=\"\",\"\",IF(I{r}<0,\"VENCIDO \"&ABS(I{r})&\" DIAS\",IF(I{r}<=180,\"VENCE EM \"&I{r}&\" DIAS\",I{r}&\" dias p/ vencer\")))"),
                null, null,
                new Formula($"IF(OR(K{r}=\"\",L{r}=\"\"),\"\",K{r}+L{r}-1)"),
                new Formula($"IF(OR(K{r}=\"\",L{r}=\"\"),\"\",K{r}+L{r})"),
                null,
                new Formula($"IF(K{r}=\"\",\"\",TEXT(K{r},\"MMMM\"))"),
                null, null);
            foreach (var c in new[] { 0, 1, 10, 11, 14, 16, 17 }) s.Preencher(r - 1, c, "DDEBF7", false);
        }
        int fim = linhas + 3;
        foreach (var c in new[] { 1, 2, 3, 6, 7, 10, 12, 13 }) s.FormatoData(c, 3, fim - 1);
        s.ValidacaoLista($"O4:O{fim}", new[] { "SIM", "NAO" }, "Vai vender?", "SIM ou NAO");
        s.ValidacaoLista($"Q4:Q{fim}", new[] { "Agendado", "Pendente", "Já tirou" }, "Agendamento Senior", "Agendado, Pendente ou Já tirou");
        s.ValidacaoData($"K4:K{fim}", new DateOnly(2000, 1, 1), new DateOnly(2099, 12, 31), "Início das férias", "Primeiro dia das férias (dd/mm/aaaa). Para dividir, use a linha de baixo deixando o nome em branco.");
        s.ValidacaoInteiro($"L4:L{fim}", 1, 30, "Dias de férias", "De 5 a 30 dias.");
        s.CondicionalCor($"F4:F{fim}", "AND(ISNUMBER($E4),$E4>0,$E4<=180)", "FFF2CC");
        s.CondicionalCor($"J4:J{fim}", "AND(ISNUMBER($I4),$I4<0)", "F8CBAD", true);
        s.CondicionalCor($"J4:J{fim}", "AND(ISNUMBER($I4),$I4>=0,$I4<=180)", "FCE4D6");
        s.CondicionalCor($"K4:N{fim}", "$K4<>\"\"", "E2EFDA");
        s.CondicionalCor($"Q4:Q{fim}", "$Q4=\"Agendado\"", "C6EFCE");
        s.CondicionalCor($"Q4:Q{fim}", "$Q4=\"Pendente\"", "FFEB9C");
        s.Congelar(3, 1);
        var larg = new double[] { 34, 13, 13, 13, 11, 22, 13, 13, 11, 22, 13, 9, 13, 13, 10, 12, 13, 40 };
        for (int c = 0; c < larg.Length; c++) s.Largura(c, larg[c]);

        var a = wb.Planilha("Anotações");
        a.Linha("Fracionamento", "");
        a.NegritoLinha(0);
        a.Linha("Um período não pode ser inferior a 14 dias", "Independente da ordem que for tirar");
        a.Linha("Outros 2 não podem ser inferiores a 5 dias", "");
        a.Linha("Não pode começar 2 dias antes de feriado", "");
        a.Linha("Ou de dia de repouso remunerado (final de semana)", "");
        a.Linha("Fim das férias deve ser antes do vencimento do período concessivo do profissional", "");
        a.Linha("", "");
        a.Linha("Como usar no QSmart", "Uma aba por time (ex.: “Férias - Lilian”). Para dividir as férias, use a linha de baixo deixando o nome em branco.");
        a.Largura(0, 70); a.Largura(1, 80);
        return wb.Salvar();
    }
}
