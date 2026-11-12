using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QSmart.Core;

/// <summary>Utilitários de texto e formatação (equivalentes às funções auxiliares do programa original).</summary>
public static class Texto
{
    public static readonly string[] MesesPt = { "Janeiro", "Fevereiro", "Março", "Abril", "Maio", "Junho",
        "Julho", "Agosto", "Setembro", "Outubro", "Novembro", "Dezembro" };
    public static readonly string[] DiasSemana = { "seg", "ter", "qua", "qui", "sex", "sáb", "dom" };

    static readonly HashSet<string> Siglas = new() { "CPF", "CNPJ", "RG", "CNH", "PF", "PJ", "IR", "CCB", "SCR", "GRI", "PDF", "CEP", "UF" };
    static readonly Dictionary<string, string> Acentos = new()
    {
        ["APROVACAO"] = "aprovação", ["REPROVACAO"] = "reprovação", ["INVALIDO"] = "inválido", ["INVALIDA"] = "inválida",
        ["AUTORIZACAO"] = "autorização", ["ATENCAO"] = "atenção", ["DESCRICAO"] = "descrição", ["EXECUCAO"] = "execução",
        ["INSPECAO"] = "inspeção", ["NAO"] = "não", ["REVERSAO"] = "reversão", ["ULTIMA"] = "última", ["NUMERO"] = "número",
    };
    static readonly Dictionary<string, string> NomesSegmento = new()
    {
        ["FONTE_RENDA"] = "Fonte de Renda", ["ENDERECO"] = "Endereço", ["CERTIDAO"] = "Certidão",
        ["TRIBUTACAO"] = "Tributação", ["NAO INFORMADO"] = "Não informado",
    };

    static readonly Regex Espacos = new(@"\s+", RegexOptions.Compiled);

    public static string Upper(string s) => s.ToUpperInvariant();
    public static string Lower(string s) => s.ToLowerInvariant();
    public static string CaseFold(string s) => s.ToLowerInvariant();

    /// <summary>strip() do Python: remove espaços em branco das pontas.</summary>
    public static string Strip(string s) => s.Trim();

    public static string Strip(string s, string chars) => s.Trim(chars.ToCharArray());

    /// <summary>Texto em formato de frase ('Tipo de renda'), mantendo siglas como CPF/CNPJ em maiúsculas.</summary>
    public static string Rotulo(string? texto)
    {
        var t = Espacos.Replace((texto ?? "").Trim(), " ");
        if (t.Length == 0) return t;
        var palavras = t.Split(' ').Select(w =>
        {
            var up = w.ToUpperInvariant();
            if (Siglas.Contains(up.Trim(".,;:()".ToCharArray()))) return up;
            return Acentos.TryGetValue(up, out var a) ? a : w.ToLowerInvariant();
        }).ToArray();
        palavras[0] = palavras[0].Length > 0 ? palavras[0][..1].ToUpperInvariant() + palavras[0][1..] : palavras[0];
        return string.Join(" ", palavras);
    }

    // ----- remoção de acentos (equivalente a NFD + remover marcas combinantes) -----
    static readonly Dictionary<char, string> MapaAcento = CriarMapa();

    static Dictionary<char, string> CriarMapa()
    {
        var d = new Dictionary<char, string>();
        void Add(string de, string para) { foreach (var c in de) d[c] = para; }
        Add("ÀÁÂÃÄÅĀĂĄǍ", "A"); Add("àáâãäåāăąǎ", "a");
        Add("ÇĆĈĊČ", "C"); Add("çćĉċč", "c");
        Add("ĎḌ", "D"); Add("ďḍ", "d");
        Add("ÈÉÊËĒĔĖĘĚẼ", "E"); Add("èéêëēĕėęěẽ", "e");
        Add("ĜĞĠĢǦ", "G"); Add("ĝğġģǧ", "g");
        Add("ĤḤ", "H"); Add("ĥḥ", "h");
        Add("ÌÍÎÏĨĪĬĮİǏ", "I"); Add("ìíîïĩīĭįǐ", "i");
        Add("Ĵ", "J"); Add("ĵǰ", "j");
        Add("ĶǨ", "K"); Add("ķǩ", "k");
        Add("ĹĻĽ", "L"); Add("ĺļľ", "l");
        Add("ÑŃŅŇǸ", "N"); Add("ñńņňǹ", "n");
        Add("ÒÓÔÕÖŌŎŐƠǑ", "O"); Add("òóôõöōŏőơǒ", "o");
        Add("ŔŖŘ", "R"); Add("ŕŗř", "r");
        Add("ŚŜŞŠȘ", "S"); Add("śŝşšș", "s");
        Add("ŢŤȚ", "T"); Add("ţťț", "t");
        Add("ÙÚÛÜŨŪŬŮŰŲƯǓǕǗǙǛ", "U"); Add("ùúûüũūŭůűųưǔǖǘǚǜ", "u");
        Add("Ŵ", "W"); Add("ŵ", "w");
        Add("ÝŸŶỲ", "Y"); Add("ýÿŷỳ", "y");
        Add("ŹŻŽ", "Z"); Add("źżž", "z");
        return d;
    }

    static bool Combinante(char c) => (c >= '̀' && c <= 'ͯ') || (c >= '᪰' && c <= '᫿') ||
                                       (c >= '᷀' && c <= '᷿') || (c >= '⃐' && c <= '⃿') || (c >= '︠' && c <= '︯');

    public static string SemAcento(string? s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        StringBuilder? sb = null;
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            string? rep = null;
            bool remove = false;
            if (c >= 'À' && MapaAcento.TryGetValue(c, out var r)) rep = r;
            else if (Combinante(c)) remove = true;
            if (rep != null || remove)
            {
                sb ??= new StringBuilder(s, 0, i, s.Length);
                if (rep != null) sb.Append(rep);
            }
            else sb?.Append(c);
        }
        return sb?.ToString() ?? s;
    }

    /// <summary>Composição NFC simples (letra + acento combinante → letra acentuada), suficiente para texto em português.</summary>
    public static string Nfc(string s)
    {
        if (s.All(c => !Combinante(c))) return s;
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i + 1 < s.Length && Combinante(s[i + 1]))
            {
                var comp = Compor(c, s[i + 1]);
                if (comp != null) { sb.Append(comp.Value); i++; continue; }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    static char? Compor(char b, char m)
    {
        string bases = "AaEeIiOoUuCcNnYy";
        if (!bases.Contains(b)) return null;
        var tabela = m switch
        {
            '́' => "ÁáÉéÍíÓóÚú__ŃńÝý",
            '̀' => "ÀàÈèÌìÒòÙù__Ǹǹ__",
            '̂' => "ÂâÊêÎîÔôÛû______",
            '̃' => "ÃãẼẽĨĩÕõŨũ__Ññ__",
            '̈' => "ÄäËëÏïÖöÜü____Ÿÿ",
            '̧' => "__________Çç____",
            _ => null,
        };
        if (tabela == null) return null;
        var ch = tabela[bases.IndexOf(b)];
        return ch == '_' ? null : ch;
    }

    /// <summary>minúsculas, sem acento, '.', '_' e '-' viram espaço (NATALIA.BRONDANI -> 'natalia brondani').</summary>
    public static string NormalizarBusca(string? s)
        => Regex.Replace(SemAcento(s ?? "").ToLowerInvariant(), @"[\s._-]+", " ").Trim();

    public static string NomeSegmento(string seg)
        => NomesSegmento.TryGetValue(seg, out var n) ? n : TitleCase(seg.Replace("_", " "));

    /// <summary>str.title() do Python.</summary>
    public static string TitleCase(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool anteriorLetra = false;
        foreach (var c in s)
        {
            bool letra = char.IsLetter(c);
            sb.Append(letra ? (anteriorLetra ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)) : c);
            anteriorLetra = letra;
        }
        return sb.ToString();
    }

    static readonly Regex TipoRe = new(@"^([^:\n]{3,70}):", RegexOptions.Compiled);

    /// <summary>Título da justificativa: texto antes do primeiro ':'.</summary>
    public static string ExtrairTipo(string? justificativa)
    {
        if (justificativa == null || justificativa.Trim().Length == 0) return "SEM JUSTIFICATIVA";
        var m = TipoRe.Match(justificativa.Trim());
        if (!m.Success) return "OUTROS";
        return Espacos.Replace(m.Groups[1].Value, " ").Trim().ToUpperInvariant();
    }

    public static string ColapsarEspacos(string s) => Espacos.Replace(s, " ");

    /// <summary>Número inteiro com ponto de milhar (1.234).</summary>
    public static string Fmt(double n) => Fmt((long)n);

    public static string Fmt(long n)
    {
        var s = Math.Abs(n).ToString(CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && (s.Length - i) % 3 == 0) sb.Append('.');
            sb.Append(s[i]);
        }
        return (n < 0 ? "-" : "") + sb;
    }

    public static string Fmt(int n) => Fmt((long)n);

    /// <summary>Formata com casas decimais e ponto (formato do Python: f"{x:.1f}").</summary>
    public static string F(double x, int casas = 1)
        => PyRound(x, casas).ToString("F" + casas, CultureInfo.InvariantCulture);

    /// <summary>Formata com vírgula decimal (o original troca '.' por ',').</summary>
    public static string Fv(double x, int casas = 1) => F(x, casas).Replace(".", ",");

    public static string Pct(double x) => F(x, 1) + "%";

    /// <summary>Arredondamento "meio para o par" como o Python (round / formatação).</summary>
    public static double PyRound(double x, int casas)
    {
        if (double.IsNaN(x) || double.IsInfinity(x) || x == 0) return x;
        long bits = BitConverter.DoubleToInt64Bits(x);
        bool neg = bits < 0;
        int exp = (int)((bits >> 52) & 0x7FF);
        long mant = bits & 0xFFFFFFFFFFFFFL;
        if (exp == 0) exp++; else mant |= 1L << 52;
        exp -= 1075;                       // x = mant * 2^exp
        if (exp >= 0) return x;            // inteiro: nada a arredondar
        var num = new System.Numerics.BigInteger(mant) * System.Numerics.BigInteger.Pow(10, casas);
        var den = System.Numerics.BigInteger.One << -exp;
        var q = System.Numerics.BigInteger.DivRem(num, den, out var r);
        var dobro = r * 2;
        if (dobro > den || (dobro == den && !q.IsEven)) q += 1;
        var txt = q.ToString(CultureInfo.InvariantCulture).PadLeft(casas + 1, '0');
        if (casas > 0) txt = txt[..^casas] + "." + txt[^casas..];
        var v = double.Parse(txt, CultureInfo.InvariantCulture);
        return neg ? -v : v;
    }

    /// <summary>Formato {x:g} do Python para números simples (7.0 → 7; 7.5 → 7.5).</summary>
    public static string G(double x)
    {
        if (Math.Abs(x - Math.Round(x)) < 1e-9) return ((long)Math.Round(x)).ToString(CultureInfo.InvariantCulture);
        return x.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    /// <summary>Minutos em texto curto: 45 min · 1h05 · 12h30.</summary>
    public static string FmtMin(double m)
    {
        var v = (long)PyRound(m, 0);
        if (v < 60) return $"{v} min";
        return $"{v / 60}h{v % 60:00}";
    }

    public static string NomeMes(string mes, bool curto = false)
    {
        var partes = mes.Split('-');
        var nome = MesesPt[int.Parse(partes[1]) - 1];
        return curto ? $"{nome[..3]}/{partes[0][2..]}" : $"{nome}/{partes[0]}";
    }

    /// <summary>Ordena dias no formato dd/mm/aaaa (ignora vazios).</summary>
    public static List<string> OrdenarDias(IEnumerable<string> dias)
        => dias.Where(d => !string.IsNullOrEmpty(d)).OrderBy(d => d.Substring(6) + d.Substring(3, 2) + d[..2], StringComparer.Ordinal).ToList();

    public static string DiaSemana(DateOnly d) => DiasSemana[((int)d.DayOfWeek + 6) % 7];

    public static string Dm(DateOnly d) => d.ToString("dd/MM", CultureInfo.InvariantCulture);
    public static string Dmy(DateOnly d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public static string Dmy(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public static string Hm(DateTime d) => d.ToString("HH:mm", CultureInfo.InvariantCulture);
    public static string Hms(DateTime d) => d.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public static string Agora() => DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Corta o texto para caber (usado em rótulos).</summary>
    public static string Cortar(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    public static string Escape(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    /// <summary>repr de float do Python (aproximação: menor representação que volta ao mesmo número).</summary>
    public static string PyFloat(double v)
    {
        if (double.IsNaN(v)) return "nan";
        if (double.IsPositiveInfinity(v)) return "inf";
        if (double.IsNegativeInfinity(v)) return "-inf";
        if (v == Math.Floor(v) && Math.Abs(v) < 1e16) return v.ToString("0", CultureInfo.InvariantCulture) + ".0";
        var r = v.ToString("R", CultureInfo.InvariantCulture);
        if (r.Contains('E'))
        {
            var p = r.Split('E');
            var exp = int.Parse(p[1], CultureInfo.InvariantCulture);
            if (exp >= -4 && exp < 16) return v.ToString("0.################", CultureInfo.InvariantCulture);
            return p[0] + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00");
        }
        return r;
    }
}
