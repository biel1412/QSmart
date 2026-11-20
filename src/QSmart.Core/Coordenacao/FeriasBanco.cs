using System.Globalization;
using System.Text.Json.Nodes;
using QSmart.Core.Planilhas;

namespace QSmart.Core.Coordenacao;

/// <summary>Um período de férias cadastrado na plataforma.</summary>
public sealed class PeriodoFer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public DateOnly Inicio { get; set; }
    /// <summary>Data que a pessoa pediu (a base para medir o quanto o choque diminuiu).</summary>
    public DateOnly Pedido { get; set; }
    public int Dias { get; set; } = 30;
    public bool Vende { get; set; }
    /// <summary>Pedido, Aprovado ou Já tirou.</summary>
    public string Status { get; set; } = "Pedido";
    public string Obs { get; set; } = "";
    /// <summary>A coordenação decidiu manter a data mesmo em choque.</summary>
    public bool Mantido { get; set; }

    public DateOnly Fim => Inicio.AddDays(Math.Max(1, Dias) - 1);
    public bool Mudou => Inicio != Pedido;
}

/// <summary>Férias de uma pessoa do One a One.</summary>
public sealed class FeriasPessoa
{
    public DateOnly? Admissao { get; set; }
    public List<PeriodoFer> Periodos { get; set; } = new();
}

/// <summary>Férias no banco do QSmart: converte para o motor de choques e sugestões (FeriasAuto) e exporta.</summary>
public static class FeriasBanco
{
    public static readonly string[] StatusPossiveis = { "Pedido", "Aprovado", "Já tirou" };

    static string D(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
    static DateOnly? LD(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    static string LS(JsonNode? n, string def = "") => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : def;

    public static string ParaJson(FeriasPessoa f)
    {
        var a = new JsonArray();
        foreach (var p in f.Periodos)
            a.Add(new JsonObject
            {
                ["id"] = p.Id, ["inicio"] = D(p.Inicio), ["pedido"] = D(p.Pedido), ["dias"] = p.Dias, ["vende"] = p.Vende,
                ["status"] = p.Status, ["obs"] = p.Obs, ["mantido"] = p.Mantido,
            });
        return new JsonObject { ["admissao"] = D(f.Admissao), ["periodos"] = a }.ToJsonString();
    }

    public static FeriasPessoa DeJson(string json)
    {
        var f = new FeriasPessoa();
        try
        {
            var n = JsonNode.Parse(json);
            f.Admissao = LD(n?["admissao"]);
            if (n?["periodos"] is JsonArray a)
                foreach (var x in a)
                {
                    if (LD(x?["inicio"]) is not { } ini) continue;
                    f.Periodos.Add(new PeriodoFer
                    {
                        Id = LS(x?["id"], Guid.NewGuid().ToString("N")[..10]), Inicio = ini, Pedido = LD(x?["pedido"]) ?? ini,
                        Dias = x?["dias"] is JsonValue dv && dv.TryGetValue<int>(out var di) ? di : 30,
                        Vende = x?["vende"] is JsonValue vv && vv.TryGetValue<bool>(out var vb) && vb,
                        Status = LS(x?["status"], "Pedido"), Obs = LS(x?["obs"]),
                        Mantido = x?["mantido"] is JsonValue mv && mv.TryGetValue<bool>(out var mb) && mb,
                    });
                }
        }
        catch { }
        return f;
    }

    /// <summary>Monta o motor de férias (times = lideranças do One a One). Devolve também o mapa período do motor → período do banco.</summary>
    public static (BaseAuto b, Dictionary<int, (string pessoa, PeriodoFer per)> mapa, Dictionary<FuncAuto, string> pessoas)
        Montar(IEnumerable<PessoaOO> pessoasOO, IReadOnlyDictionary<string, FeriasPessoa> dados)
    {
        var b = new BaseAuto { Arquivo = "QSmart" };
        var mapa = new Dictionary<int, (string, PeriodoFer)>();
        var pessoas = new Dictionary<FuncAuto, string>();
        int id = 1;
        foreach (var p in pessoasOO.Where(p => !p.Arquivado).OrderBy(p => p.Lideranca).ThenBy(p => p.Nome))
        {
            dados.TryGetValue(p.Id, out var fp);
            var f = new FuncAuto { Aba = p.Lideranca, Nome = p.Nome, Admissao = fp?.Admissao };
            b.Funcs.Add(f);
            pessoas[f] = p.Id;
            if (fp == null) continue;
            foreach (var per in fp.Periodos.OrderBy(x => x.Inicio))
            {
                var pa = new PeriodoAuto
                {
                    Id = id, Func = f, Inicio = per.Inicio, InicioOriginal = per.Pedido, Dias = Math.Max(1, per.Dias),
                    Vende = per.Vende ? "Sim" : "Não", Status = per.Status, Obs = per.Obs,
                };
                b.Periodos.Add(pa);
                if (per.Mantido) b.Mantidos.Add(id);
                mapa[id] = (p.Id, per);
                id++;
            }
        }
        return (b, mapa, pessoas);
    }

    /// <summary>Choque do time com as datas pedidas (antes das mudanças).</summary>
    public static int ChoquePedido(BaseAuto b, string? aba = null)
    {
        var guarda = b.Periodos.ToDictionary(p => p, p => p.Inicio);
        foreach (var p in b.Periodos) p.Inicio = p.InicioOriginal;
        var t = FeriasAuto.ChoqueTotal(b, aba);
        foreach (var kv in guarda) kv.Key.Inicio = kv.Value;
        return t;
    }

    public static byte[] Exportar(BaseAuto b)
    {
        var wb = new XlsxWriter();
        var s = wb.Planilha("Férias");
        s.Cabecalho("Liderança", "Nome", "Admissão", "Fim do concessivo", "Início", "Fim", "Retorno", "Dias", "Vende 10 dias", "Situação", "Data pedida", "Data mudou?", "Dias de choque", "Fora junto com", "Observações");
        s.NegritoLinha(0);
        int n = 1;
        foreach (var f in b.Funcs.OrderBy(f => f.Aba).ThenBy(f => f.Nome))
        {
            var ps = b.Periodos.Where(p => p.Func == f).OrderBy(p => p.Inicio).ToList();
            if (ps.Count == 0) { s.Linha(f.Aba, f.Nome, f.Admissao, f.FimConcessivo, null, null, null, null, "", "Sem férias marcadas", null, "", null, "", ""); n++; continue; }
            foreach (var p in ps)
            {
                var com = string.Join(", ", FeriasAuto.ComQuem(b, p).Select(c => $"{c.nome} ({c.dias} d)"));
                s.Linha(f.Aba, f.Nome, f.Admissao, f.FimConcessivo, p.Inicio, p.Fim, p.Retorno, p.Dias, p.Vende, p.Status, p.InicioOriginal,
                    p.Alterado ? "Sim" : "Não", FeriasAuto.Choque(b, p), com, p.Obs);
                n++;
            }
        }
        foreach (var c in new[] { 2, 3, 4, 5, 6, 10 }) s.FormatoData(c, 1, Math.Max(1, n - 1));
        s.Congelar(1, 2);
        foreach (var (c, w) in new[] { (0, 20.0), (1, 34.0), (2, 12.0), (3, 14.0), (4, 12.0), (5, 12.0), (6, 12.0), (7, 7.0), (8, 12.0), (9, 13.0), (10, 12.0), (11, 11.0), (12, 12.0), (13, 40.0), (14, 40.0) }) s.Largura(c, w);
        return wb.Salvar();
    }
}
