using System.Text.Json.Nodes;

namespace QSmart.Core;

/// <summary>Um feriado nacional.</summary>
public sealed record FeriadoDia(DateOnly Data, string Nome);

/// <summary>
/// Feriados nacionais. A lista vem da BrasilAPI (atualizada todo ano); sem internet, usa a lista calculada aqui
/// (datas fixas da lei + Sexta-feira Santa).
/// </summary>
public static class Feriados
{
    public const string Url = "https://brasilapi.com.br/api/feriados/v1/";

    static readonly (int m, int d, string nome)[] Fixos =
    {
        (1, 1, "Confraternização Universal"), (4, 21, "Tiradentes"), (5, 1, "Dia do Trabalho"), (9, 7, "Independência do Brasil"),
        (10, 12, "Nossa Senhora Aparecida"), (11, 2, "Finados"), (11, 15, "Proclamação da República"), (11, 20, "Dia da Consciência Negra"),
        (12, 25, "Natal"),
    };

    public static DateOnly Pascoa(int y)
    {
        int a = y % 19, b = y / 100, c = y % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30,
            i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        return new DateOnly(y, (h + l - 7 * m + 114) / 31, (h + l - 7 * m + 114) % 31 + 1);
    }

    /// <summary>Lista calculada (para quando a BrasilAPI não responde).</summary>
    public static List<FeriadoDia> Locais(int ano)
    {
        var l = Fixos.Select(f => new FeriadoDia(new DateOnly(ano, f.m, f.d), f.nome)).ToList();
        l.Add(new FeriadoDia(Pascoa(ano).AddDays(-2), "Sexta-feira Santa"));
        return l.OrderBy(f => f.Data).ToList();
    }

    /// <summary>Lê a resposta da BrasilAPI: [{ "date": "2026-11-20", "name": "...", "type": "national" }].</summary>
    public static List<FeriadoDia> LerBrasilApi(string json)
    {
        var l = new List<FeriadoDia>();
        foreach (var n in JsonNode.Parse(json)?.AsArray() ?? new JsonArray())
        {
            var d = n?["date"]?.GetValue<string>();
            var nome = n?["name"]?.GetValue<string>() ?? "Feriado";
            if (d != null && DateOnly.TryParseExact(d, "yyyy-MM-dd", out var dt)) l.Add(new FeriadoDia(dt, nome));
        }
        return l.OrderBy(f => f.Data).ToList();
    }

    /// <summary>O próximo feriado a partir de hoje (inclusive).</summary>
    public static FeriadoDia? Proximo(IEnumerable<FeriadoDia> l, DateOnly hoje) => l.Where(f => f.Data >= hoje).OrderBy(f => f.Data).FirstOrDefault();

    static readonly string[] Semana = { "domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado" };
    static readonly string[] SemanaCurta = { "dom", "seg", "ter", "qua", "qui", "sex", "sáb" };

    public static string DiaCurto(DateOnly d) => SemanaCurta[(int)d.DayOfWeek];

    /// <summary>Texto curto do aviso: só quando o feriado está a até <paramref name="dias"/> dias.</summary>
    public static string? Aviso(IEnumerable<FeriadoDia> l, DateOnly hoje, int dias = 7)
    {
        var p = Proximo(l, hoje);
        if (p == null) return null;
        int faltam = p.Data.DayNumber - hoje.DayNumber;
        if (faltam > dias) return null;
        return faltam switch
        {
            0 => $"Hoje é feriado · {p.Nome}",
            1 => $"Amanhã é feriado · {p.Nome}",
            _ => $"Feriado {(p.Data.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "no" : "na")} {Semana[(int)p.Data.DayOfWeek]} ({p.Data:dd/MM}) · {p.Nome}",
        };
    }
}
