using System.Text.Json;
using System.Text.Json.Serialization;
using QSmart.Core.Planilhas;

namespace QSmart.Core.Coordenacao;

/// <summary>Um período de férias de um colaborador.</summary>
public sealed class PeriodoFerias
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Colaborador { get; set; } = "";
    public string Equipe { get; set; } = "";
    public DateOnly Inicio { get; set; }
    public DateOnly Fim { get; set; }
    /// <summary>Interesse em vender dias (abono pecuniário).</summary>
    public bool Vender { get; set; }
    public int DiasVender { get; set; }
    /// <summary>Planejado ou Aprovado.</summary>
    public string Status { get; set; } = "Planejado";
    public string Obs { get; set; } = "";

    [JsonIgnore] public int Dias => Fim.DayNumber - Inicio.DayNumber + 1;
    /// <summary>Primeiro dia útil depois das férias.</summary>
    [JsonIgnore] public DateOnly Retorno => Ferias.ProximoDiaUtil(Fim.AddDays(1));
    public PeriodoFerias Copia() => (PeriodoFerias)MemberwiseClone();
}

public sealed class DadosFerias
{
    public int Versao { get; set; } = 1;
    public List<PeriodoFerias> Periodos { get; set; } = new();
    /// <summary>Intervalo mínimo (dias) entre férias de colaboradores do mesmo grupo; abaixo disso: “datas próximas”.</summary>
    public int DistanciaMinima { get; set; } = 7;
    /// <summary>Compara só colaboradores da mesma equipe (sem equipe = todos juntos).</summary>
    public bool PorEquipe { get; set; } = true;
}

public enum NivelFerias { Ok = 0, Proximo = 1, Choque = 2 }

/// <summary>Alerta entre dois períodos: choque (dias em comum) ou datas próximas (intervalo menor que o mínimo).</summary>
public sealed record AlertaFerias(NivelFerias Nivel, PeriodoFerias A, PeriodoFerias B, int Dias, DateOnly? De, DateOnly? Ate)
{
    public bool MesmaPessoa => Ferias.Norm(A.Colaborador) == Ferias.Norm(B.Colaborador);
    public string Titulo => Nivel == NivelFerias.Choque ? (MesmaPessoa ? "Períodos sobrepostos" : "Choque de datas") : "Datas muito próximas";
    public string Texto => Nivel == NivelFerias.Choque
        ? (MesmaPessoa
            ? $"{A.Colaborador} tem dois períodos que se sobrepõem em {Ferias.Plural(Dias, "dia")} ({Core.Texto.Dm(De!.Value)} a {Core.Texto.Dm(Ate!.Value)})."
            : $"{A.Colaborador} e {B.Colaborador} ficam fora ao mesmo tempo por {Ferias.Plural(Dias, "dia")} ({Core.Texto.Dm(De!.Value)} a {Core.Texto.Dm(Ate!.Value)}).")
        : Dias == 0
            ? $"{B.Colaborador} sai no dia seguinte à volta de {A.Colaborador} — sem nenhum dia de intervalo."
            : $"Só {Ferias.Plural(Dias, "dia")} entre a volta de {A.Colaborador} ({Core.Texto.Dm(A.Fim)}) e a saída de {B.Colaborador} ({Core.Texto.Dm(B.Inicio)}).";
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DadosFerias))]
internal partial class JsonFerias : JsonSerializerContext { }

public static class Ferias
{
    public const int MinimoClt = 5, MaiorParteClt = 14, DiasPorAno = 30, MaxVenda = 10;

    public static string Norm(string? s) => Core.Texto.NormalizarBusca(s ?? "");
    public static string Plural(int n, string palavra) => $"{n} {palavra}{(n == 1 ? "" : "s")}";

    public static DateOnly ProximoDiaUtil(DateOnly d)
    {
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    static bool MesmoGrupo(PeriodoFerias a, PeriodoFerias b, bool porEquipe)
        => !porEquipe || Norm(a.Equipe) == Norm(b.Equipe);

    /// <summary>Todos os choques e proximidades entre os períodos (cada par aparece uma vez, A sempre começa antes).</summary>
    public static List<AlertaFerias> Alertas(IEnumerable<PeriodoFerias> periodos, int distanciaMinima, bool porEquipe)
    {
        var ps = periodos.Where(p => p.Fim >= p.Inicio).OrderBy(p => p.Inicio).ThenBy(p => p.Fim).ThenBy(p => p.Colaborador, StringComparer.Ordinal).ToList();
        var r = new List<AlertaFerias>();
        for (int i = 0; i < ps.Count; i++)
            for (int j = i + 1; j < ps.Count; j++)
            {
                var a = ps[i]; var b = ps[j];
                bool mesma = Norm(a.Colaborador) == Norm(b.Colaborador);
                if (!mesma && !MesmoGrupo(a, b, porEquipe)) continue;
                var de = a.Inicio > b.Inicio ? a.Inicio : b.Inicio;
                var ate = a.Fim < b.Fim ? a.Fim : b.Fim;
                if (ate >= de)
                {
                    r.Add(new AlertaFerias(NivelFerias.Choque, a, b, ate.DayNumber - de.DayNumber + 1, de, ate));
                    continue;
                }
                if (mesma) continue;                                   // a mesma pessoa pode emendar períodos
                var antes = a.Fim < b.Inicio ? a : b;
                var depois = antes == a ? b : a;
                int intervalo = depois.Inicio.DayNumber - antes.Fim.DayNumber - 1;
                if (intervalo < distanciaMinima) r.Add(new AlertaFerias(NivelFerias.Proximo, antes, depois, intervalo, null, null));
            }
        return r.OrderByDescending(x => x.Nivel).ThenBy(x => x.A.Inicio).ToList();
    }

    public static NivelFerias Nivel(PeriodoFerias p, IEnumerable<AlertaFerias> alertas)
    {
        var n = NivelFerias.Ok;
        foreach (var a in alertas)
            if ((a.A.Id == p.Id || a.B.Id == p.Id) && a.Nivel > n) n = a.Nivel;
        return n;
    }

    /// <summary>Lembretes da CLT para um período (não impedem salvar).</summary>
    public static List<string> AvisosClt(PeriodoFerias p, IEnumerable<PeriodoFerias> todos)
    {
        var r = new List<string>();
        if (p.Fim < p.Inicio) { r.Add("A data de fim é anterior ao início."); return r; }
        if (p.Dias < MinimoClt) r.Add($"Pela CLT, cada período deve ter pelo menos {MinimoClt} dias.");
        var mesmos = todos.Where(x => x.Id != p.Id && Norm(x.Colaborador) == Norm(p.Colaborador) && x.Inicio.Year == p.Inicio.Year).Append(p).ToList();
        int dias = mesmos.Sum(x => x.Dias), venda = mesmos.Where(x => x.Vender).Sum(x => x.DiasVender);
        if (dias + venda > DiasPorAno) r.Add($"{p.Colaborador} soma {dias} dias de férias + {venda} vendidos em {p.Inicio.Year} (o normal é {DiasPorAno}).");
        if (mesmos.Count > 3) r.Add("A CLT permite dividir as férias em até 3 períodos.");
        if (mesmos.Count > 1 && mesmos.All(x => x.Dias < MaiorParteClt)) r.Add($"Ao dividir, um dos períodos precisa ter pelo menos {MaiorParteClt} dias.");
        if (p.Vender && p.DiasVender > MaxVenda) r.Add($"Só é possível vender até {MaxVenda} dias (1/3 das férias).");
        if (p.Inicio.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday)
            r.Add("As férias não podem começar nos 2 dias antes de feriado ou do descanso semanal (sexta, sábado ou domingo).");
        return r;
    }

    public static string Gravar(DadosFerias d) => JsonSerializer.Serialize(d, JsonFerias.Default.DadosFerias);

    public static DadosFerias Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new DadosFerias();
        var d = JsonSerializer.Deserialize(json, JsonFerias.Default.DadosFerias) ?? throw new InvalidDataException("Arquivo de férias vazio.");
        d.Periodos ??= new();
        d.DistanciaMinima = Math.Clamp(d.DistanciaMinima, 0, 365);
        return d;
    }

    /// <summary>Planilha com os períodos e os alertas.</summary>
    public static byte[] Exportar(DadosFerias d)
    {
        var al = Alertas(d.Periodos, d.DistanciaMinima, d.PorEquipe);
        var wb = new XlsxWriter();
        var s = wb.Planilha("Férias");
        s.Cabecalho("Colaborador", "Equipe", "Início", "Fim", "Dias", "Retorno", "Vender?", "Dias a vender", "Status", "Situação", "Observação");
        foreach (var p in d.Periodos.OrderBy(x => x.Inicio).ThenBy(x => x.Colaborador, StringComparer.Ordinal))
        {
            var n = Nivel(p, al);
            s.Linha(p.Colaborador, p.Equipe, Core.Texto.Dmy(p.Inicio), Core.Texto.Dmy(p.Fim), p.Dias, Core.Texto.Dmy(p.Retorno),
                p.Vender ? "Sim" : "Não", p.Vender ? p.DiasVender : null, p.Status,
                n == NivelFerias.Choque ? "Choque" : n == NivelFerias.Proximo ? "Datas próximas" : "OK", p.Obs);
            if (n != NivelFerias.Ok) s.Preencher(s.N - 1, 9, n == NivelFerias.Choque ? "FDECEC" : "FEF3E2", true);
        }
        s.Congelar(1, 1);
        s.AutoLargura(10, 40);
        var a = wb.Planilha("Alertas");
        a.Cabecalho("Tipo", "Colaborador 1", "Colaborador 2", "Dias", "Detalhe");
        foreach (var x in al)
        {
            a.Linha(x.Titulo, x.A.Colaborador, x.B.Colaborador, x.Dias, x.Texto);
            a.Preencher(a.N - 1, 0, x.Nivel == NivelFerias.Choque ? "FDECEC" : "FEF3E2", true);
        }
        a.AutoLargura(10, 90);
        var so = wb.Planilha("Sobre");
        so.Cabecalho("Item", "Valor");
        so.Linha("Intervalo mínimo entre férias (dias)", d.DistanciaMinima);
        so.Linha("Comparar só dentro da mesma equipe", d.PorEquipe ? "Sim" : "Não");
        so.Linha("Gerado em", Core.Texto.Agora());
        so.AutoLargura(10, 50);
        return wb.Salvar();
    }
}
