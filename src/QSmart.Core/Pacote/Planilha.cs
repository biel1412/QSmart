using System.Globalization;
using System.Text;

namespace QSmart.Core.Pacote;

/// <summary>Estilo de uma célula do Smart Math.</summary>
public sealed class EstiloCel
{
    public bool N, I, S;
    public string Cor = "", Fundo = "", Alinh = "";
    /// <summary>geral, num, int, moeda, pct, data, texto</summary>
    public string Formato = "geral";
    public int Casas = 2;

    public EstiloCel Clonar() => (EstiloCel)MemberwiseClone();
    public bool Padrao => !N && !I && !S && Cor == "" && Fundo == "" && Alinh == "" && Formato == "geral";
    public string Chave => $"{N}{I}{S}|{Cor}|{Fundo}|{Alinh}|{Formato}|{Casas}";
}

public sealed class Celula
{
    public string Entrada = "";
    public EstiloCel E = new();
    public Celula Clonar() => new() { Entrada = Entrada, E = E.Clonar() };
}

public sealed class Grafico
{
    public string Id = Guid.NewGuid().ToString("N")[..8];
    /// <summary>colunas, barras, linhas, pizza</summary>
    public string Tipo = "colunas";
    public string Faixa = "";
    public string Titulo = "";
}

public sealed class Filtro
{
    public int Linha, C1, C2;
    /// <summary>Valores escondidos por coluna (texto exibido).</summary>
    public Dictionary<int, HashSet<string>> Escondidos = new();
}

public sealed class Folha
{
    public string Nome = "Planilha1";
    public Dictionary<long, Celula> Cel = new();
    public Dictionary<int, double> Larg = new();
    public List<Grafico> Graficos = new();
    public Filtro? Filtro;

    public static long K(int r, int c) => (long)r * 20000 + c;
    public static (int r, int c) DeK(long k) => ((int)(k / 20000), (int)(k % 20000));

    public Celula? Get(int r, int c) => Cel.GetValueOrDefault(K(r, c));
    public Celula Pegar(int r, int c) { var k = K(r, c); if (!Cel.TryGetValue(k, out var x)) Cel[k] = x = new Celula(); return x; }
    public void Limpar(int r, int c) => Cel.Remove(K(r, c));

    public int UltLinha => Cel.Count == 0 ? -1 : Cel.Where(x => x.Value.Entrada != "").Select(x => DeK(x.Key).r).DefaultIfEmpty(-1).Max();
    public int UltColuna => Cel.Count == 0 ? -1 : Cel.Where(x => x.Value.Entrada != "").Select(x => DeK(x.Key).c).DefaultIfEmpty(-1).Max();
    public double Largura(int c) => Larg.TryGetValue(c, out var w) ? w : 96;

    public Folha Clonar() => new()
    {
        Nome = Nome, Cel = Cel.ToDictionary(x => x.Key, x => x.Value.Clonar()), Larg = new(Larg),
        Graficos = Graficos.Select(g => new Grafico { Id = g.Id, Tipo = g.Tipo, Faixa = g.Faixa, Titulo = g.Titulo }).ToList(),
        Filtro = Filtro == null ? null : new Filtro { Linha = Filtro.Linha, C1 = Filtro.C1, C2 = Filtro.C2, Escondidos = Filtro.Escondidos.ToDictionary(x => x.Key, x => new HashSet<string>(x.Value)) },
    };
}

public sealed class Pasta
{
    public List<Folha> Folhas = new() { new Folha() };
    public Folha? Folha(string nome) => Folhas.FirstOrDefault(f => string.Equals(f.Nome, nome, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Erro de fórmula (#DIV/0!, #VALOR!, #REF!, #NOME?, #N/D, #NÚM!, #CIRC!).</summary>
public sealed record ErroF(string Cod)
{
    public override string ToString() => Cod;
    public static readonly ErroF Div0 = new("#DIV/0!"), Valor = new("#VALOR!"), Ref = new("#REF!"), Nome = new("#NOME?"), ND = new("#N/D"), Num = new("#NÚM!"), Circ = new("#CIRC!");
}

/// <summary>Intervalo de células avaliado (usado pelas funções).</summary>
public sealed record FaixaV(Folha F, int R1, int C1, int R2, int C2)
{
    public int Linhas => R2 - R1 + 1;
    public int Colunas => C2 - C1 + 1;
}

/// <summary>Endereços: A1 ↔ (linha, coluna), começando em 0.</summary>
public static class Ender
{
    public static string Col(int c)
    {
        var s = ""; c++;
        while (c > 0) { int m = (c - 1) % 26; s = (char)('A' + m) + s; c = (c - 1) / 26; }
        return s;
    }
    public static int ColDe(string s)
    {
        int c = 0;
        foreach (var ch in s.ToUpperInvariant()) c = c * 26 + (ch - 'A' + 1);
        return c - 1;
    }
    public static string Cel(int r, int c) => Col(c) + (r + 1);
    public static string Faixa(int r1, int c1, int r2, int c2) => r1 == r2 && c1 == c2 ? Cel(r1, c1) : $"{Cel(r1, c1)}:{Cel(r2, c2)}";

    public static bool Ler(string s, out int r, out int c)
    {
        r = c = -1;
        s = s.Trim().Replace("$", "");
        int i = 0;
        while (i < s.Length && char.IsLetter(s[i])) i++;
        if (i == 0 || i > 3 || i == s.Length) return false;
        if (!int.TryParse(s[i..], out var n) || n < 1) return false;
        c = ColDe(s[..i]); r = n - 1;
        return true;
    }

    public static bool LerFaixa(string s, out int r1, out int c1, out int r2, out int c2)
    {
        r1 = c1 = r2 = c2 = -1;
        var p = s.Split(':');
        if (p.Length == 1) { if (!Ler(p[0], out r1, out c1)) return false; r2 = r1; c2 = c1; return true; }
        if (p.Length != 2 || !Ler(p[0], out r1, out c1) || !Ler(p[1], out r2, out c2)) return false;
        if (r2 < r1) (r1, r2) = (r2, r1);
        if (c2 < c1) (c1, c2) = (c2, c1);
        return true;
    }
}

/// <summary>Números, datas e textos no formato brasileiro.</summary>
public static class FormatoPt
{
    public static readonly CultureInfo Pt = CriarPt();
    static CultureInfo CriarPt()
    {
        var c = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        c.NumberFormat.NumberDecimalSeparator = ","; c.NumberFormat.NumberGroupSeparator = ".";
        c.NumberFormat.PercentDecimalSeparator = ","; c.NumberFormat.PercentGroupSeparator = ".";
        c.NumberFormat.CurrencyDecimalSeparator = ","; c.NumberFormat.CurrencyGroupSeparator = ".";
        // nomes de dias e meses em português (o navegador roda sem dados de cultura)
        var f = c.DateTimeFormat;
        f.DayNames = new[] { "domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado" };
        f.AbbreviatedDayNames = new[] { "dom", "seg", "ter", "qua", "qui", "sex", "sáb" };
        f.ShortestDayNames = new[] { "D", "S", "T", "Q", "Q", "S", "S" };
        f.MonthNames = new[] { "janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro", "" };
        f.AbbreviatedMonthNames = new[] { "jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez", "" };
        f.MonthGenitiveNames = f.MonthNames; f.AbbreviatedMonthGenitiveNames = f.AbbreviatedMonthNames;
        f.ShortDatePattern = "dd/MM/yyyy";
        return c;
    }

    /// <summary>Converte o que a pessoa digitou: número, %, R$, data ou verdadeiro/falso. Senão, é texto.</summary>
    public static object? Valor(string entrada, out string formatoSugerido)
    {
        formatoSugerido = "";
        var s = entrada.Trim();
        if (s.Length == 0) return null;
        if (s.StartsWith("'")) return entrada[1..];
        var up = s.ToUpperInvariant();
        if (up is "VERDADEIRO" or "TRUE") return true;
        if (up is "FALSO" or "FALSE") return false;
        bool pct = false, moeda = false;
        var t = s;
        if (t.EndsWith("%")) { pct = true; t = t[..^1].Trim(); }
        if (t.StartsWith("R$", StringComparison.OrdinalIgnoreCase)) { moeda = true; t = t[2..].Trim(); }
        if (Numero(t, out var d))
        {
            if (pct) { formatoSugerido = "pct"; return d / 100; }
            if (moeda) formatoSugerido = "moeda";
            return d;
        }
        if (Data(s, out var dt)) { formatoSugerido = "data"; return dt; }
        return entrada;
    }

    static readonly System.Text.RegularExpressions.Regex NumPt = new(@"^[-+]?((\d{1,3}(\.\d{3})+|\d+)(,\d+)?|,\d+)([eE][-+]?\d+)?$");
    static readonly System.Text.RegularExpressions.Regex NumEn = new(@"^[-+]?\d*\.\d+([eE][-+]?\d+)?$");

    public static bool Numero(string t, out double d)
    {
        d = 0;
        t = t.Trim();
        if (t.Length == 0) return false;
        if (NumPt.IsMatch(t)) return double.TryParse(t.Replace(".", "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        if (NumEn.IsMatch(t)) return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        return false;
    }

    public static bool Data(string s, out double serial)
    {
        serial = 0;
        string[] fmts = { "d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy", "yyyy-MM-dd", "d/M/yyyy H:mm", "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss" };
        if (DateTime.TryParseExact(s.Trim(), fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)) { serial = dt.ToOADate(); return true; }
        return false;
    }

    public static string Num(double d, int casas, bool milhar) =>
        d.ToString((milhar ? "#,##0" : "0") + (casas > 0 ? "." + new string('0', casas) : ""), Pt);

    public static string Geral(double d)
    {
        if (double.IsNaN(d) || double.IsInfinity(d)) return "#NÚM!";
        if (d != 0 && (Math.Abs(d) >= 1e11 || Math.Abs(d) < 1e-9)) return d.ToString("0.#####E+00", Pt);
        var r = Math.Round(d, 10);
        return r.ToString("0.##########", Pt);
    }

    /// <summary>Texto exibido de um valor com o formato da célula.</summary>
    public static string Exibir(object? v, EstiloCel e)
    {
        switch (v)
        {
            case null: return "";
            case ErroF er: return er.Cod;
            case bool b: return b ? "VERDADEIRO" : "FALSO";
            case string s: return s;
            case double d:
                return e.Formato switch
                {
                    "num" => Num(d, e.Casas, true),
                    "int" => Num(d, 0, true),
                    "moeda" => (d < 0 ? "-R$ " : "R$ ") + Num(Math.Abs(d), e.Casas, true),
                    "pct" => Num(d * 100, e.Casas, false) + "%",
                    "data" => d >= -657434 && d <= 2958465 ? DateTime.FromOADate(d).ToString(DateTime.FromOADate(d).TimeOfDay.TotalSeconds > 0.5 ? "dd/MM/yyyy HH:mm" : "dd/MM/yyyy", CultureInfo.InvariantCulture) : "#####",
                    _ => Geral(d),
                };
            case FaixaV: return ErroF.Valor.Cod;
            default: return v.ToString() ?? "";
        }
    }
}
