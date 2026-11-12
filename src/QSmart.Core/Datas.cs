using System.Globalization;
using QSmart.Core.Planilhas;

namespace QSmart.Core;

/// <summary>Leitura de datas com as mesmas regras do pandas usadas no original (formato fixo, formato inferido, dia primeiro).</summary>
public static class Datas
{
    /// <summary>strptime simplificado: %d %m %Y %y %H %M %S %f e caracteres literais. Aceita dia/mês com 1 ou 2 dígitos.</summary>
    public static DateTime? Strptime(string? s, string formato)
    {
        if (s == null) return null;
        s = s.Trim();
        int i = 0;
        int dia = 1, mes = 1, ano = 1900, h = 0, mi = 0, se = 0, micro = 0;
        for (int k = 0; k < formato.Length; k++)
        {
            var f = formato[k];
            if (f == '%' && k + 1 < formato.Length)
            {
                var d = formato[++k];
                int max = d switch { 'Y' => 4, 'f' => 6, _ => 2 };
                int ini = i;
                while (i < s.Length && i - ini < max && char.IsAsciiDigit(s[i])) i++;
                if (i == ini) return null;
                if (d == 'Y' && i - ini != 4) return null;
                var v = int.Parse(s.AsSpan(ini, i - ini), CultureInfo.InvariantCulture);
                switch (d)
                {
                    case 'd': dia = v; break;
                    case 'm': mes = v; break;
                    case 'Y': ano = v; break;
                    case 'y': ano = v < 69 ? 2000 + v : 1900 + v; break;
                    case 'H': h = v; break;
                    case 'M': mi = v; break;
                    case 'S': se = v; break;
                    case 'f': micro = (int)(v * Math.Pow(10, 6 - (i - ini))); break;
                    default: return null;
                }
            }
            else
            {
                if (i >= s.Length) return null;
                if (f == ' ') { if (!char.IsWhiteSpace(s[i])) return null; while (i < s.Length && char.IsWhiteSpace(s[i])) i++; continue; }
                if (s[i] != f) return null;
                i++;
            }
        }
        if (i != s.Length) return null;
        try
        {
            if (h > 23 || mi > 59 || se > 59) return null;
            return new DateTime(ano, mes, dia, h, mi, se).AddTicks(micro * 10L);
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    static readonly string[] Horas = { "", " %H:%M:%S", " %H:%M", " %H:%M:%S.%f" };

    /// <summary>ISO 8601 (AAAA-MM-DD[ T]hh:mm[:ss[.ffffff]]), como o parser rápido do pandas.</summary>
    public static DateTime? Iso(string? s)
    {
        if (s == null) return null;
        s = s.Trim();
        if (s.Length < 10 || s[4] != '-') return null;
        var t = s.Replace('T', ' ');
        foreach (var hr in Horas)
        {
            var d = Strptime(t, "%Y-%m-%d" + hr);
            if (d != null) return d;
        }
        return null;
    }

    /// <summary>Tenta descobrir o formato a partir do 1º valor (pandas >= 2). Devolve null se não reconhecer.</summary>
    public static string? AdivinharFormato(string s, bool diaPrimeiro)
    {
        s = s.Trim();
        if (s.Length >= 10 && s[4] == '-')
        {
            var t = s.Replace('T', ' ');
            foreach (var hr in Horas)
                if (Strptime(t, "%Y-%m-%d" + hr) != null) return "ISO|%Y-%m-%d" + hr;
        }
        foreach (var sep in new[] { '/', '.', '-' })
        {
            var partes = s.Split(' ')[0].Split(sep);
            if (partes.Length != 3 || !partes.All(p => p.Length > 0 && p.All(char.IsAsciiDigit))) continue;
            string anoF = partes[2].Length == 4 ? "%Y" : partes[2].Length == 2 ? "%y" : "";
            if (anoF == "")
            {
                if (partes[0].Length == 4) return null;
                continue;
            }
            int a = int.Parse(partes[0]), b = int.Parse(partes[1]);
            bool dmy;
            if (diaPrimeiro) dmy = !(b > 12 && a <= 12);
            else dmy = a > 12 && b <= 12;
            var baseF = dmy ? $"%d{sep}%m{sep}{anoF}" : $"%m{sep}%d{sep}{anoF}";
            foreach (var hr in Horas)
                if (Strptime(s, baseF + hr) != null) return baseF + hr;
            return null;
        }
        return null;
    }

    /// <summary>Leitura flexível de um valor (dateutil), dia primeiro ou mês primeiro.</summary>
    public static DateTime? Flexivel(string? s, bool diaPrimeiro)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var iso = Iso(s);
        if (iso != null) return iso;
        var f = AdivinharFormato(s, diaPrimeiro);
        if (f != null && !f.StartsWith("ISO")) return Strptime(s, f);
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        return null;
    }

    /// <summary>pd.to_datetime(serie, errors="coerce"[, dayfirst]) para textos: formato inferido pelo 1º valor.</summary>
    public static DateTime?[] ToDatetime(IReadOnlyList<string?> valores, bool diaPrimeiro = false)
    {
        var res = new DateTime?[valores.Count];
        static bool Nulo(string? v) => string.IsNullOrWhiteSpace(v) || v is "nan" or "NaN" or "NaT" or "None";
        string? primeiro = valores.FirstOrDefault(v => !Nulo(v));
        if (primeiro == null) return res;
        var fmt = AdivinharFormato(primeiro, diaPrimeiro);
        for (int i = 0; i < valores.Count; i++)
        {
            var v = valores[i];
            if (Nulo(v)) continue;
            res[i] = fmt != null && fmt.StartsWith("ISO") ? Strptime(v!.Trim().Replace('T', ' '), fmt[4..]) : fmt != null ? Strptime(v, fmt) : Flexivel(v, diaPrimeiro);
        }
        return res;
    }

    /// <summary>pd.to_datetime para células (dtype=object): datas reais entram direto; textos pelo formato inferido.</summary>
    public static DateTime?[] ToDatetime(IReadOnlyList<Celula> valores, bool diaPrimeiro = false)
    {
        var textos = valores.Select(c => c.Tipo == TipoCelula.Texto ? c.Texto : null).ToList();
        var conv = ToDatetime(textos, diaPrimeiro);
        var res = new DateTime?[valores.Count];
        for (int i = 0; i < valores.Count; i++)
        {
            var c = valores[i];
            if (c.Tipo == TipoCelula.Data) res[i] = c.Data;
            else if (c.Tipo == TipoCelula.Texto) res[i] = conv[i];
            else if (c.Tipo is TipoCelula.Inteiro or TipoCelula.Numero) res[i] = null;
        }
        return res;
    }

    /// <summary>Data (dd/mm/aaaa) a partir da coluna de data/hora do volume, tolerante a formatos (_datas_dia).</summary>
    public static string[] DatasDia(IReadOnlyList<string?> serie)
    {
        // o original faz serie.astype(str): vazio vira "nan" (que não é data)
        var d = serie.Select(s => Strptime(s, "%d/%m/%Y %H:%M:%S")).ToArray();
        int nulos = d.Count(x => x == null);
        if (serie.Count > 0 && nulos / (double)serie.Count > 0.5)
            d = ToDatetime(serie, diaPrimeiro: true);
        return d.Select(x => x?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "").ToArray();
    }

    /// <summary>Data de uma célula, lida sem ambiguidade: AAAA-MM-DD, DD/MM/AAAA ou número de série (data_celula).</summary>
    public static DateOnly? DataCelula(Celula c)
    {
        if (c.EhVazia) return null;
        if (c.Tipo == TipoCelula.Data) return DateOnly.FromDateTime(c.Data);
        return DataCelula(c.Texto);
    }

    static readonly System.Text.RegularExpressions.Regex ReIso = new(@"^(\d{4})-(\d{1,2})-(\d{1,2})");
    static readonly System.Text.RegularExpressions.Regex ReDma = new(@"^(\d{1,2})[/.-](\d{1,2})[/.-](\d{2,4})");
    static readonly System.Text.RegularExpressions.Regex ReSerial = new(@"^(\d{5})(?:\.0+)?$");

    public static DateOnly? DataCelula(string? s)
    {
        if (s == null) return null;
        s = s.Trim();
        try
        {
            var m = ReIso.Match(s);
            if (m.Success) return new DateOnly(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
            m = ReDma.Match(s);
            if (m.Success)
            {
                int ano = int.Parse(m.Groups[3].Value) + (m.Groups[3].Value.Length == 2 ? 2000 : 0);
                return new DateOnly(ano, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
            }
            m = ReSerial.Match(s);
            if (m.Success)
            {
                var n = int.Parse(m.Groups[1].Value);
                if (n > 20000 && n < 80000) return new DateOnly(1899, 12, 30).AddDays(n);
            }
        }
        catch (ArgumentOutOfRangeException) { return null; }
        return null;
    }
}
