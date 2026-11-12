using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace QSmart.Core;

/// <summary>Um mês gravado no histórico (mesmo formato JSON do historico_qualidade.json do programa original).</summary>
public sealed class RegistroHist
{
    public string Mes { get; set; } = "";
    public string Arquivo { get; set; } = "";
    public string GravadoEm { get; set; } = "";
    public List<string>? Periodo { get; set; }
    public List<string> Abas { get; set; } = new();
    public long Avaliacoes { get; set; }
    public long Desvios { get; set; }
    public List<string> Origens { get; set; } = new();
    public Dictionary<string, long> PorOrigem { get; set; } = new();
    public List<LinhaOrigem> PorAut { get; set; } = new();
    public List<LinhaOrigem> PorInspetor { get; set; } = new();
    public List<(string erro, long qtd)> PorErro { get; set; } = new();
    public List<LinhaOrigem> PorColab { get; set; } = new();
    public List<(string colab, string erro, long n)> ColabErro { get; set; } = new();
    public List<CuboInsp> Cubo { get; set; } = new();

    public JsonObject ParaJson()
    {
        JsonObject Ori(LinhaOrigem l, string chave, bool avalDesv)
        {
            var o = new JsonObject { [chave] = l.Nome };
            if (avalDesv) { o["AVAL"] = l.Aval; o["DESV"] = l.Desv; }
            foreach (var org in Origens) o[org] = l.O(org);
            return o;
        }
        var j = new JsonObject
        {
            ["mes"] = Mes,
            ["arquivo"] = Arquivo,
            ["gravado_em"] = GravadoEm,
            ["periodo"] = Periodo == null ? null : new JsonArray(Periodo.Select(p => (JsonNode?)p).ToArray()),
            ["abas"] = new JsonArray(Abas.Select(a => (JsonNode?)a).ToArray()),
            ["avaliacoes"] = Avaliacoes,
            ["desvios"] = Desvios,
            ["origens"] = new JsonArray(Origens.Select(a => (JsonNode?)a).ToArray()),
        };
        var po = new JsonObject();
        foreach (var o in Origens) po[o] = PorOrigem.TryGetValue(o, out var v) ? v : 0;
        j["por_origem"] = po;
        j["por_aut"] = new JsonArray(PorAut.Select(l => (JsonNode?)Ori(l, "AUT", false)).ToArray());
        j["por_inspetor"] = new JsonArray(PorInspetor.Select(l => (JsonNode?)Ori(l, "INSPETOR", true)).ToArray());
        j["por_erro"] = new JsonArray(PorErro.Select(e => (JsonNode?)new JsonObject { ["ERRO"] = e.erro, ["QTD"] = e.qtd }).ToArray());
        j["por_colab"] = new JsonArray(PorColab.Select(l => (JsonNode?)Ori(l, "COLABORADOR", true)).ToArray());
        j["colab_erro"] = new JsonArray(ColabErro.Select(c => (JsonNode?)new JsonArray(c.colab, c.erro, c.n)).ToArray());
        j["cubo"] = new JsonArray(Cubo.Select(c => (JsonNode?)new JsonArray(c.Aba, c.Origem, c.Aut, c.Erro, c.Desvio ? 1 : 0, c.N)).ToArray());
        return j;
    }

    static long L(JsonNode? n)
    {
        if (n == null) return 0;
        var v = n.AsValue();
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<double>(out var d)) return (long)d;
        if (v.TryGetValue<bool>(out var b)) return b ? 1 : 0;
        return 0;
    }

    static string S(JsonNode? n) => n == null ? "" : n.GetValueKind() == JsonValueKind.String ? n.GetValue<string>() : n.ToJsonString();

    public static RegistroHist? DeJson(JsonNode? n)
    {
        if (n is not JsonObject o || o["mes"] == null) return null;
        var r = new RegistroHist
        {
            Mes = S(o["mes"]), Arquivo = S(o["arquivo"]), GravadoEm = S(o["gravado_em"]),
            Periodo = o["periodo"] is JsonArray pa ? pa.Select(S).ToList() : null,
            Abas = (o["abas"] as JsonArray)?.Select(S).ToList() ?? new(),
            Avaliacoes = L(o["avaliacoes"]), Desvios = L(o["desvios"]),
            Origens = (o["origens"] as JsonArray)?.Select(S).ToList() ?? new(),
        };
        if (o["por_origem"] is JsonObject po) foreach (var kv in po) r.PorOrigem[kv.Key] = L(kv.Value);
        List<LinhaOrigem> Linhas(JsonNode? arr, string chave)
        {
            var res = new List<LinhaOrigem>();
            if (arr is not JsonArray a) return res;
            foreach (var x in a.OfType<JsonObject>())
            {
                var l = new LinhaOrigem { Nome = S(x[chave]), Aval = L(x["AVAL"]), Desv = L(x["DESV"]) };
                foreach (var kv in x)
                    if (kv.Key != chave && kv.Key != "AVAL" && kv.Key != "DESV") l.PorOrigem[kv.Key] = L(kv.Value);
                l.Exibicao = chave == "AUT" ? Texto.NomeSegmento(l.Nome) : l.Nome;
                res.Add(l);
            }
            return res;
        }
        r.PorAut = Linhas(o["por_aut"], "AUT");
        r.PorInspetor = Linhas(o["por_inspetor"], "INSPETOR");
        r.PorColab = Linhas(o["por_colab"], "COLABORADOR");
        if (o["por_erro"] is JsonArray pe)
            r.PorErro = pe.OfType<JsonObject>().Select(x => (S(x["ERRO"]), L(x["QTD"]))).ToList();
        if (o["colab_erro"] is JsonArray ce)
            r.ColabErro = ce.OfType<JsonArray>().Where(x => x.Count >= 3).Select(x => (S(x[0]), S(x[1]), L(x[2]))).ToList();
        if (o["cubo"] is JsonArray cb)
            r.Cubo = cb.OfType<JsonArray>().Where(x => x.Count >= 6)
                .Select(x => new CuboInsp(S(x[0]), S(x[1]), S(x[2]), S(x[3]), L(x[4]) != 0, L(x[5]))).ToList();
        return r;
    }
}

public sealed class FiltrosHist
{
    public HashSet<string> Origem { get; set; } = new();
    public HashSet<string> Aut { get; set; } = new();
    public HashSet<string> Insp { get; set; } = new();
    public HashSet<string> Erro { get; set; } = new();
    public bool Algum => Origem.Count + Aut.Count + Insp.Count + Erro.Count > 0;
    public HashSet<string> Get(string k) => k switch { "origem" => Origem, "aut" => Aut, "insp" => Insp, _ => Erro };
    public void Set(string k, HashSet<string> v)
    {
        switch (k) { case "origem": Origem = v; break; case "aut": Aut = v; break; case "insp": Insp = v; break; default: Erro = v; break; }
    }
    public FiltrosHist Copia() => new() { Origem = new(Origem), Aut = new(Aut), Insp = new(Insp), Erro = new(Erro) };
}

public static class Historico
{
    public static List<RegistroHist> Ler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        var n = JsonNode.Parse(json);
        var regs = n is JsonObject o ? o["registros"] as JsonArray : null;
        if (regs == null) return new();
        return regs.Select(RegistroHist.DeJson).Where(r => r != null && r.Mes != "").Select(r => r!)
            .OrderBy(r => r.Mes, StringComparer.Ordinal).ToList();
    }

    public static string Gravar(List<RegistroHist> regs)
    {
        var j = new JsonObject { ["versao"] = 1, ["registros"] = new JsonArray(regs.Select(r => (JsonNode?)r.ParaJson()).ToArray()) };
        return j.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>registro_do_evolucao: converte o resultado da Evolução em algo gravável.</summary>
    public static RegistroHist RegistroDe(ResultadoInspecao e, string mes)
    {
        var origens = e.Origens.ToList();
        LinhaOrigem Copia(LinhaOrigem l) => new()
        {
            Nome = l.Nome, Exibicao = l.Exibicao, Aval = l.Aval, Desv = l.Desv,
            PorOrigem = origens.ToDictionary(o => o, o => l.O(o)),
        };
        return new RegistroHist
        {
            Mes = mes,
            Arquivo = e.Arquivo,
            GravadoEm = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            Periodo = e.Periodo == null ? null : new List<string>
                { e.Periodo.Value.ini.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), e.Periodo.Value.fim.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            Abas = e.Abas.ToList(),
            Avaliacoes = e.Avaliacoes,
            Desvios = e.Desvios,
            Origens = origens,
            PorOrigem = origens.ToDictionary(o => o, e.Q),
            PorAut = e.PorAut.Select(Copia).ToList(),
            PorInspetor = e.PorInspetor.Select(Copia).ToList(),
            PorErro = e.PorErro.ToList(),
            PorColab = e.PorColab.Select(Copia).ToList(),
            ColabErro = e.PorColabErro.ToList(),
            Cubo = e.Cubo.ToList(),
        };
    }

    public static readonly (string chave, string nome)[] NomesFiltro =
        { ("origem", "Origem"), ("aut", "Autorização"), ("insp", "Inspetor"), ("erro", "Erro") };

    sealed record LinhaCubo(string Mes, string Insp, string Origem, string Aut, string Erro, bool Desvio, long N);

    static (List<LinhaCubo>, List<string>) CuboHistorico(List<RegistroHist> regs)
    {
        var linhas = new List<LinhaCubo>();
        var sem = new List<string>();
        foreach (var r in regs)
        {
            if (r.Cubo.Count == 0) { sem.Add(r.Mes); continue; }
            foreach (var c in r.Cubo) linhas.Add(new LinhaCubo(r.Mes, c.Aba, c.Origem, c.Aut, c.Erro, c.Desvio, c.N));
        }
        return (linhas, sem);
    }

    /// <summary>Valores disponíveis para cada filtro (só dos meses que têm detalhe gravado).</summary>
    public static Dictionary<string, List<string>> Opcoes(List<RegistroHist> regs)
    {
        var (df, _) = CuboHistorico(regs);
        var ori = df.Select(x => x.Origem).ToHashSet();
        return new()
        {
            ["origem"] = Inspecao.OrigensPadrao.Where(ori.Contains).Concat(ori.Except(Inspecao.OrigensPadrao).OrderBy(x => x, StringComparer.Ordinal)).ToList(),
            ["aut"] = df.Select(x => x.Aut).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
            ["insp"] = df.Select(x => x.Insp).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
            ["erro"] = df.Where(x => x.Desvio).Select(x => x.Erro).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
        };
    }

    static (DateTime, DateTime)? PeriodoDe(IEnumerable<RegistroHist> regs)
    {
        var datas = regs.Where(r => r.Periodo != null).SelectMany(r => r.Periodo!)
            .Select(x => DateTime.TryParse(x, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null)
            .Where(d => d != null).Select(d => d!.Value).ToList();
        return datas.Count > 0 ? (datas.Min(), datas.Max()) : null;
    }

    /// <summary>agregar_historico: soma os meses escolhidos (com filtros, usa o detalhe gravado de cada mês).</summary>
    public static ResultadoInspecao Agregar(List<RegistroHist> regs, FiltrosHist? filtros = null)
    {
        if (filtros != null && filtros.Algum) return AgregarFiltrado(regs, filtros);
        var origens = Inspecao.OrigensPadrao.ToList();
        foreach (var r in regs) foreach (var o in r.Origens) if (!origens.Contains(o)) origens.Add(o);

        var aut = new Dictionary<string, Dictionary<string, long>>();
        var ordemAut = new List<string>();
        foreach (var r in regs)
            foreach (var l in r.PorAut)
            {
                if (!aut.TryGetValue(l.Nome, out var d)) { aut[l.Nome] = d = origens.ToDictionary(o => o, _ => 0L); ordemAut.Add(l.Nome); }
                foreach (var o in r.Origens) d[o] = d.GetValueOrDefault(o) + l.O(o);
            }
        var insp = new Dictionary<string, LinhaOrigem>();
        var ordemInsp = new List<string>();
        foreach (var r in regs)
            foreach (var l in r.PorInspetor)
            {
                if (!insp.TryGetValue(l.Nome, out var d))
                {
                    insp[l.Nome] = d = new LinhaOrigem { Nome = l.Nome, Exibicao = l.Nome, PorOrigem = origens.ToDictionary(o => o, _ => 0L) };
                    ordemInsp.Add(l.Nome);
                }
                d.Aval += l.Aval;
                d.Desv += l.Desv;
                foreach (var o in r.Origens) d.PorOrigem[o] = d.PorOrigem.GetValueOrDefault(o) + l.O(o);
            }
        var erros = new Dictionary<string, long>();
        foreach (var r in regs) foreach (var (e, q) in r.PorErro) erros[e] = erros.GetValueOrDefault(e) + q;

        return new ResultadoInspecao
        {
            Arquivo = $"{regs.Count} mês(es) gravado(s)",
            Abas = ordemInsp,
            Avaliacoes = regs.Sum(r => r.Avaliacoes),
            Desvios = regs.Sum(r => r.Desvios),
            Origens = origens,
            PorOrigem = origens.ToDictionary(o => o, o => regs.Sum(r => r.PorOrigem.GetValueOrDefault(o))),
            PorAut = ordemAut.Select(a => new LinhaOrigem { Nome = a, Exibicao = Texto.NomeSegmento(a), PorOrigem = aut[a] })
                .OrderByDescending(x => x.Total).ThenBy(x => x.Nome, StringComparer.Ordinal).ToList(),
            PorInspetor = ordemInsp.Select(i => insp[i]).OrderByDescending(x => x.Desv).ThenByDescending(x => x.Aval)
                .ThenBy(x => x.Nome, StringComparer.Ordinal).ToList(),
            PorErro = erros.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList(),
            Periodo = PeriodoDe(regs),
            Meses = regs.Select(r => new MesAgregado { Mes = r.Mes, Desvios = r.Desvios, Avaliacoes = r.Avaliacoes, PorOrigem = new(r.PorOrigem) }).ToList(),
        };
    }

    static ResultadoInspecao AgregarFiltrado(List<RegistroHist> regs, FiltrosHist f)
    {
        var (df, sem) = CuboHistorico(regs);
        var regsOk = regs.Where(r => r.Cubo.Count > 0).ToList();
        var bas = df.Where(x => (f.Origem.Count == 0 || f.Origem.Contains(x.Origem)) && (f.Aut.Count == 0 || f.Aut.Contains(x.Aut))
                                && (f.Insp.Count == 0 || f.Insp.Contains(x.Insp))).ToList();
        var desv = bas.Where(x => x.Desvio && (f.Erro.Count == 0 || f.Erro.Contains(x.Erro))).ToList();
        var todas = Inspecao.OrigensPadrao.Concat(df.Select(x => x.Origem).Distinct().Except(Inspecao.OrigensPadrao).OrderBy(x => x, StringComparer.Ordinal)).ToList();
        var origens = todas.Where(o => f.Origem.Count == 0 || f.Origem.Contains(o)).ToList();
        Dictionary<string, long> Po(IEnumerable<LinhaCubo> xs)
        {
            var d = origens.ToDictionary(o => o, _ => 0L);
            foreach (var x in xs) if (d.ContainsKey(x.Origem)) d[x.Origem] += x.N;
            return d;
        }
        var porAut = bas.Select(x => x.Aut).Distinct().OrderBy(x => x, StringComparer.Ordinal)
            .Select(a => new LinhaOrigem { Nome = a, Exibicao = Texto.NomeSegmento(a), PorOrigem = Po(desv.Where(d => d.Aut == a)) })
            .OrderByDescending(x => x.Total).ThenBy(x => x.Nome, StringComparer.Ordinal).ToList();
        var porInsp = bas.GroupBy(x => x.Insp).Select(g => new LinhaOrigem
        {
            Nome = g.Key, Exibicao = g.Key, Aval = g.Sum(x => x.N), Desv = desv.Where(d => d.Insp == g.Key).Sum(d => d.N),
            PorOrigem = Po(desv.Where(d => d.Insp == g.Key)),
        }).OrderByDescending(x => x.Desv).ThenByDescending(x => x.Aval).ThenBy(x => x.Nome, StringComparer.Ordinal).ToList();
        var porErro = desv.GroupBy(x => x.Erro).Select(g => (g.Key, g.Sum(x => x.N)))
            .OrderByDescending(x => x.Item2).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
        var meses = regsOk.Select(r =>
        {
            var dm = desv.Where(x => x.Mes == r.Mes).ToList();
            return new MesAgregado { Mes = r.Mes, Desvios = dm.Sum(x => x.N), Avaliacoes = bas.Where(x => x.Mes == r.Mes).Sum(x => x.N), PorOrigem = Po(dm) };
        }).ToList();
        return new ResultadoInspecao
        {
            Arquivo = $"{regsOk.Count} mês(es) com detalhe",
            Abas = porInsp.Select(x => x.Nome).ToList(),
            Avaliacoes = bas.Sum(x => x.N),
            Desvios = desv.Sum(x => x.N),
            Origens = origens,
            PorOrigem = Po(desv),
            PorAut = porAut,
            PorInspetor = porInsp,
            PorErro = porErro,
            Periodo = PeriodoDe(regsOk),
            Meses = meses,
            SemDetalhe = sem,
            FiltrosTexto = Volume.TextoFiltros(NomesFiltro.Select(n => (n.nome, (IEnumerable<string>)f.Get(n.chave)))),
        };
    }

    /// <summary>montar_mensagem_teams: texto organizado para colar/enviar no Teams.</summary>
    public static string MensagemTeams(List<RegistroHist> regs, ResultadoInspecao h)
    {
        var meses = regs.Select(r => r.Mes).ToList();
        var periodo = meses.Count == 1 ? Texto.NomeMes(meses[0]) : $"{Texto.NomeMes(meses[0])} a {Texto.NomeMes(meses[^1])} ({meses.Count} meses)";
        long tot = h.Avaliacoes, des = h.Desvios;
        string P(long a, long b) => Texto.F(a / (double)(b == 0 ? 1 : b) * 100) + "%";
        var L = new List<string> { $"📊 QSMART · QUALIDADE — {periodo}" };
        if (h.FiltrosTexto != "") L.Add($"🔎 Filtros: {h.FiltrosTexto}");
        L.Add("");
        L.AddRange(new[] { "📌 RESUMO", $"• Inspeções avaliadas: {Texto.Fmt(tot)}", $"• Desvios: {Texto.Fmt(des)} ({P(des, tot)} das avaliações)" });
        foreach (var o in h.Origens)
        {
            var q = h.Q(o);
            L.Add($"• Desvios {o}: {Texto.Fmt(q)} ({P(q, des)} dos desvios)");
        }
        if (regs.Count > 1)
        {
            L.AddRange(new[] { "", "📅 MÊS A MÊS" });
            foreach (var m in h.Meses)
                L.Add($"• {Texto.NomeMes(m.Mes)}: {Texto.Fmt(m.Desvios)} desvios em {Texto.Fmt(m.Avaliacoes)} avaliações ({P(m.Desvios, m.Avaliacoes)})");
        }
        if (h.PorAut.Count > 0)
        {
            L.AddRange(new[] { "", "🗂 DESVIOS POR TIPO DE AUTORIZAÇÃO" });
            foreach (var r in h.PorAut)
                L.Add($"• {r.Exibicao}: {Texto.Fmt(r.Total)} ({string.Join(" · ", h.Origens.Select(o => $"{o} {Texto.Fmt(r.O(o))}"))})");
        }
        if (h.PorInspetor.Count > 0)
        {
            L.AddRange(new[] { "", "👤 DESVIOS POR INSPETOR" });
            foreach (var r in h.PorInspetor)
                L.Add($"• {r.Nome}: {Texto.Fmt(r.Desv)} desvios em {Texto.Fmt(r.Aval)} avaliações ({P(r.Desv, r.Aval)}) — " +
                      string.Join(" · ", h.Origens.Select(o => $"{o} {Texto.Fmt(r.O(o))}")));
        }
        if (h.PorErro.Count > 0)
        {
            L.AddRange(new[] { "", "⚠ TIPOS DE ERRO MAIS FREQUENTES" });
            int i = 1;
            foreach (var (e, q) in h.PorErro.Take(5)) L.Add($"{i++}. {Texto.Rotulo(e)}: {Texto.Fmt(q)} ({P(q, des)})");
        }
        L.AddRange(new[] { "", $"Gerado pelo QSmart · Insi em {Texto.Agora()}" });
        return string.Join("\n", L);
    }
}
