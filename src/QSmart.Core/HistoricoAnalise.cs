namespace QSmart.Core;

/// <summary>Central do histórico: números de cada mês gravado (com os filtros), variação contra o mês anterior,
/// mapa de calor inspetor × mês e diagnóstico de tendência.</summary>
public sealed class HistoricoAnalise
{
    public sealed record Mes(string Chave, long Aval, long Desv, Dictionary<string, long> PorOrigem, Dictionary<string, (long aval, long desv)> PorInsp, Dictionary<string, long> PorTipo)
    {
        public double Taxa => Aval > 0 ? Desv * 100.0 / Aval : 0;
    }

    public List<Mes> Meses { get; private init; } = new();
    public List<string> Origens { get; private init; } = new();
    /// <summary>Inspetores do ranking (todos que avaliaram no período, até 40), em ordem de desvios.</summary>
    public List<string> Inspetores { get; private init; } = new();
    /// <summary>Tipos de autorização com mais desvios no período (até 6), para os minigráficos.</summary>
    public List<string> Tipos { get; private init; } = new();

    /// <summary>Posição (1 = maior taxa) do inspetor no mês, entre os inspetores do mapa que avaliaram naquele mês.</summary>
    public int? Posicao(Mes m, string insp)
    {
        var ordem = Inspetores.Select(n => (n, t: TaxaInsp(m, n))).Where(x => x.t != null)
            .OrderByDescending(x => x.t).ThenBy(x => x.n, StringComparer.Ordinal).Select(x => x.n).ToList();
        int i = ordem.IndexOf(insp);
        return i < 0 ? null : i + 1;
    }

    public static HistoricoAnalise Montar(List<RegistroHist> regs, FiltrosHist filtros, int maxMeses = 12, int maxInsp = 40)
    {
        var usados = regs.OrderBy(r => r.Mes, StringComparer.Ordinal).TakeLast(maxMeses).ToList();
        var meses = new List<Mes>();
        var origens = new List<string>();
        foreach (var r in usados)
        {
            var a = Historico.Agregar(new List<RegistroHist> { r }, filtros);
            // mês gravado antes dos filtros existirem: sem detalhe para filtrar
            if (filtros.Algum && a.SemDetalhe.Contains(r.Mes)) continue;
            foreach (var o in a.Origens) if (!origens.Contains(o)) origens.Add(o);
            meses.Add(new Mes(r.Mes, a.Avaliacoes, a.Desvios, new(a.PorOrigem),
                a.PorInspetor.ToDictionary(i => i.Nome, i => (i.Aval, i.Desv)),
                a.PorAut.GroupBy(x => x.Exibicao).ToDictionary(g => g.Key, g => g.Sum(x => x.Total))));
        }
        var insp = meses.SelectMany(m => m.PorInsp).GroupBy(kv => kv.Key)
            .Select(g => (nome: g.Key, desv: g.Sum(x => x.Value.desv), aval: g.Sum(x => x.Value.aval)))
            .Where(x => x.aval > 0).OrderByDescending(x => x.desv).ThenBy(x => x.nome, StringComparer.Ordinal)
            .Take(maxInsp).Select(x => x.nome).ToList();
        var tipos = meses.SelectMany(m => m.PorTipo).GroupBy(kv => kv.Key).Select(g => (g.Key, n: g.Sum(x => x.Value)))
            .Where(x => x.n > 0).OrderByDescending(x => x.n).ThenBy(x => x.Key, StringComparer.Ordinal).Take(6).Select(x => x.Key).ToList();
        return new HistoricoAnalise { Meses = meses, Origens = origens, Inspetores = insp, Tipos = tipos };
    }

    public Mes? Anterior(Mes m) { int i = Meses.IndexOf(m); return i > 0 ? Meses[i - 1] : null; }

    /// <summary>Taxa do inspetor no mês (null se não avaliou naquele mês).</summary>
    public static double? TaxaInsp(Mes m, string insp) =>
        m.PorInsp.TryGetValue(insp, out var v) && v.aval > 0 ? v.desv * 100.0 / v.aval : null;

    /// <summary>Tendência do inspetor nos meses com avaliação: (primeiro mês, taxa, último mês, taxa).</summary>
    public (string de, double t0, string ate, double t1)? Tendencia(string insp)
    {
        var com = Meses.Select(m => (m, t: TaxaInsp(m, insp))).Where(x => x.t != null).ToList();
        if (com.Count < 2) return null;
        return (com[0].m.Chave, com[0].t!.Value, com[^1].m.Chave, com[^1].t!.Value);
    }

    /// <summary>Diagnóstico de tendência: o inspetor encontra os desvios; a sugestão é o que ele reforça com os times.</summary>
    public string Diagnostico(string insp, Mes foco)
    {
        var tf = TaxaInsp(foco, insp);
        var td = Tendencia(insp);
        if (td == null)
            return tf == null
                ? $"{insp} não tem avaliações em {Texto.NomeMes(foco.Chave)}."
                : $"Nas avaliações de {insp} em {Texto.NomeMes(foco.Chave)}, a taxa de desvio foi {Texto.F(tf.Value, 1)}%. " +
                  (Meses.Count > 1 ? "No período, só há avaliações nesse mês, então ainda não dá para ver a tendência." : "Grave mais meses para ver a tendência.");
        var (de, t0, ate, t1) = td.Value;
        var dif = t1 - t0;
        var partes = new List<string>();
        if (Math.Abs(dif) < 0.3)
            partes.Add($"Nas avaliações de {insp}, a taxa de desvio ficou estável: {Texto.F(t0, 1)}% em {Texto.NomeMes(de, true)} e {Texto.F(t1, 1)}% em {Texto.NomeMes(ate, true)}.");
        else
            partes.Add($"Nas avaliações de {insp}, a taxa de desvio {(dif > 0 ? "subiu" : "caiu")} de {Texto.F(t0, 1)}% em {Texto.NomeMes(de, true)} para {Texto.F(t1, 1)}% em {Texto.NomeMes(ate, true)}.");
        if (tf != null && Anterior(foco) is { } ant && TaxaInsp(ant, insp) is double ta)
        {
            var d = tf.Value - ta;
            partes.Add($"Em {Texto.NomeMes(foco.Chave, true)}: {(d >= 0 ? "+" : "")}{Texto.F(d, 1)} p.p. contra {Texto.NomeMes(ant.Chave, true)}.");
        }
        partes.Add(dif >= 0.3
            ? $"Sugestão: {insp} pode reforçar com os times os pontos que mais cresceram."
            : dif <= -0.3
                ? $"Bom sinal: vale {insp} compartilhar com os times o que funcionou."
                : $"Sugestão: {insp} pode manter o acompanhamento com os times.");
        return string.Join(" ", partes);
    }
}
