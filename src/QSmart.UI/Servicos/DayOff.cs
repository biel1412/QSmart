using System.Text.Json.Nodes;

namespace QSmart.UI.Servicos;

public sealed record PedidoDO(int Id, DateOnly Data, string Status, string Nome, DateOnly? Aniversario, string Obs, string Lideranca, string Coordenacao,
    string Motivo, string DecididoPor, string CriadoEm, string? DecididoEm, bool Meu, bool Decide, List<(DateOnly De, DateOnly Para)> Hist)
{
    public bool Pendente => Status == "pendente";
    public bool Aprovado => Status == "aprovado";
    public bool Reprovado => Status == "reprovado";
    /// <summary>O day off cai no mês do aniversário da pessoa.</summary>
    public bool MesAniversario => Aniversario is DateOnly a && a.Month == Data.Month;
    /// <summary>Dias entre o day off e o aniversário (no ano do day off).</summary>
    public int? DistAniv => Aniversario is DateOnly a ? Math.Abs(Data.DayNumber - DayOff.NoAno(a, Data.Year).DayNumber) : null;
}

public sealed record VisaoDO(List<PedidoDO> Itens, Dictionary<DateOnly, int> Ocupacao, int Limite, List<PedidoDO> Aguardando, List<PedidoDO> Meus, string Coordenacao, DateOnly Hoje);

/// <summary>Day off: a liderança pede, a coordenação aprova ou reprova; o calendário aprovado é de todos.</summary>
public static class DayOff
{
    public static DateOnly NoAno(DateOnly a, int ano) => new(ano, a.Month, Math.Min(a.Day, DateTime.DaysInMonth(ano, a.Month)));

    static string S(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
    static DateOnly? D(string s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? d : null;
    static bool B(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<bool>(out var x) && x;

    static PedidoDO Ler(JsonNode x) => new(
        x["id"]?.GetValue<int>() ?? 0, D(S(x, "data")) ?? default, S(x, "status"), S(x, "nome"), D(S(x, "aniversario")), S(x, "obs"),
        S(x, "lideranca"), S(x, "coordenacao"), S(x, "motivo"), S(x, "decididoPor"), S(x, "criadoEm"), S(x, "decididoEm") is { Length: > 0 } de ? de : null,
        B(x, "meu"), B(x, "decide"),
        (x["hist"] as JsonArray ?? new()).Select(h => (D(S(h, "de")) ?? default, D(S(h, "para")) ?? default)).ToList());

    static List<PedidoDO> Lista(JsonNode? a) => (a as JsonArray ?? new()).Where(x => x != null).Select(x => Ler(x!)).ToList();

    public static async Task<(VisaoDO? v, string erro)> Carregar(Conta c, DateOnly de, DateOnly ate)
    {
        var r = await c.Chamar("GET", $"dayoff?de={de:yyyy-MM-dd}&ate={ate:yyyy-MM-dd}");
        if (!r.Ok || r.Json == null) return (null, r.Erro);
        var oc = new Dictionary<DateOnly, int>();
        if (r.Json["ocupacao"] is JsonObject o) foreach (var (k, v) in o) if (D(k) is DateOnly d) oc[d] = v?.GetValue<int>() ?? 0;
        return (new VisaoDO(Lista(r.Json["itens"]), oc, r.Json["limite"]?.GetValue<int>() ?? 5, Lista(r.Json["aguardando"]), Lista(r.Json["meus"]),
            S(r.Json, "coordenacao"), D(S(r.Json, "hoje")) ?? DateOnly.FromDateTime(DateTime.Today)), "");
    }

    public static Task<RespostaApi> Pedir(Conta c, string nome, DateOnly aniversario, DateOnly data, string obs) =>
        c.Chamar("POST", "dayoff", new JsonObject { ["nome"] = nome, ["aniversario"] = aniversario.ToString("yyyy-MM-dd"), ["data"] = data.ToString("yyyy-MM-dd"), ["obs"] = obs });

    public static Task<RespostaApi> Decidir(Conta c, int id, bool aprovar, string motivo) =>
        c.Chamar("POST", "dayoff/decidir", new JsonObject { ["id"] = id, ["aprovar"] = aprovar, ["motivo"] = motivo });

    public static Task<RespostaApi> Remarcar(Conta c, int id, DateOnly data) =>
        c.Chamar("POST", "dayoff/remarcar", new JsonObject { ["id"] = id, ["data"] = data.ToString("yyyy-MM-dd") });

    public static Task<RespostaApi> Cancelar(Conta c, int id) => c.Chamar("DELETE", $"dayoff?id={id}");

    // ---------- calendário ----------
    public static readonly string[] Semana = { "Dom", "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb" };

    /// <summary>As semanas do mês (de domingo a sábado), com os dias de fora do mês para completar.</summary>
    public static List<DateOnly[]> Semanas(int ano, int mes)
    {
        var ini = new DateOnly(ano, mes, 1);
        var d = ini.AddDays(-(int)ini.DayOfWeek);
        var res = new List<DateOnly[]>();
        while (d <= ini.AddMonths(1).AddDays(-1))
        {
            res.Add(Enumerable.Range(0, 7).Select(i => d.AddDays(i)).ToArray());
            d = d.AddDays(7);
        }
        return res;
    }

    public static string MesNome(DateOnly d) => $"{Core.Texto.MesesPt[d.Month - 1]} de {d.Year}";
    public static string DiaSemana(DateOnly d) => new[] { "domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado" }[(int)d.DayOfWeek];

    public static string StatusNome(string s) => s switch { "aprovado" => "Aprovado", "reprovado" => "Reprovado", _ => "Aguardando" };
    public static string StatusCls(string s) => s switch { "aprovado" => "ok", "reprovado" => "nao", _ => "esp" };

    public static string Curto(string n) { var p = n.Split(' ', StringSplitOptions.RemoveEmptyEntries); return p.Length > 1 ? $"{p[0]} {p[^1][0]}." : n; }
    public static string Iniciais(string n) { var p = n.Split(' ', StringSplitOptions.RemoveEmptyEntries); return p.Length == 0 ? "?" : p.Length == 1 ? p[0][..1].ToUpperInvariant() : $"{p[0][0]}{p[^1][0]}".ToUpperInvariant(); }

    static readonly string[] Paleta = { "#0891B2", "#7C3AED", "#059669", "#DB2777", "#D97706", "#2563EB", "#0D9488", "#9333EA", "#DC2626", "#65A30D" };
    public static string Cor(string s) { if (s == "") return "#64748B"; int h = 0; foreach (var ch in Core.Coordenacao.Ferias.Norm(s)) h = unchecked(h * 31 + ch); return Paleta[(int)((uint)h % Paleta.Length)]; }
}
