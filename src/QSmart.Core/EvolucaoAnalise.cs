namespace QSmart.Core;

/// <summary>Central de análise da Evolução: mapa inspetor × tipo de autorização, indicadores,
/// perfil (radar) de um inspetor contra a média do time e diagnóstico automático. Tudo pode ser filtrado por origem.</summary>
public sealed class EvolucaoAnalise
{
    public sealed record Insp(string Nome, long Aval, long Desv, long[] PorTipo, Dictionary<string, long> PorOrigem)
    {
        public double Taxa => Aval > 0 ? Desv * 100.0 / Aval : 0;
    }

    public string? Origem { get; private init; }
    public List<string> Tipos { get; private init; } = new();
    public List<Insp> Inspetores { get; private init; } = new();
    public long Avaliacoes { get; private init; }
    public long Desvios { get; private init; }
    public double TaxaTime => Avaliacoes > 0 ? Desvios * 100.0 / Avaliacoes : 0;
    public long[] TotalPorTipo { get; private init; } = Array.Empty<long>();
    public long Maximo { get; private init; }

    /// <summary>Faixas da taxa de desvio: até 3% ok, até 10% atenção, acima crítico.</summary>
    public const double Ok = 3, Atencao = 10;
    public static string Nivel(double taxa) => taxa <= Ok ? "ok" : taxa <= Atencao ? "atencao" : "critico";

    /// <summary>Monta a análise. <paramref name="origem"/> nulo = todas as origens. Mostra até <paramref name="maxTipos"/> tipos; o resto vira "Outros".</summary>
    public static EvolucaoAnalise Montar(ResultadoInspecao r, string? origem, int maxTipos = 6)
    {
        var desv = r.Cubo.Where(c => c.Desvio && (origem == null || c.Origem == origem)).ToList();
        var ordem = desv.GroupBy(c => c.Aut).Select(g => (aut: g.Key, n: g.Sum(x => x.N)))
            .OrderByDescending(x => x.n).ThenBy(x => x.aut, StringComparer.Ordinal).Select(x => x.aut).ToList();
        // sem desvios no filtro: usa a ordem geral dos tipos para o mapa não ficar sem colunas
        if (ordem.Count == 0) ordem = r.PorAut.Where(a => a.Total > 0).Select(a => a.Nome).ToList();
        var mostrados = ordem.Count > maxTipos ? ordem.Take(maxTipos - 1).ToList() : ordem;
        bool outros = ordem.Count > maxTipos;
        var col = mostrados.Select((a, i) => (a, i)).ToDictionary(x => x.a, x => x.i);
        int nCol = mostrados.Count + (outros ? 1 : 0);
        int Col(string aut) => col.TryGetValue(aut, out var i) ? i : nCol - 1;

        var insp = r.PorInspetor.Select(p =>
        {
            var v = new long[nCol];
            var po = new Dictionary<string, long>();
            foreach (var c in desv.Where(c => c.Aba == p.Nome))
            {
                v[Col(c.Aut)] += c.N;
                po[c.Origem] = po.GetValueOrDefault(c.Origem) + c.N;
            }
            return new Insp(p.Nome, p.Aval, v.Sum(), v, po);
        }).OrderByDescending(x => x.Desv).ThenByDescending(x => x.Taxa).ThenBy(x => x.Nome, StringComparer.Ordinal).ToList();

        var tot = new long[nCol];
        foreach (var i in insp) for (int k = 0; k < nCol; k++) tot[k] += i.PorTipo[k];

        var nomes = mostrados.Select(Texto.NomeSegmento).ToList();
        if (outros) nomes.Add("Outros");
        return new EvolucaoAnalise
        {
            Origem = origem,
            Tipos = nomes,
            Inspetores = insp,
            Avaliacoes = r.Avaliacoes,
            Desvios = insp.Sum(i => i.Desv),
            TotalPorTipo = tot,
            Maximo = insp.Count == 0 || nCol == 0 ? 0 : insp.Max(i => i.PorTipo.DefaultIfEmpty(0).Max()),
        };
    }

    /// <summary>Tipo com mais desvios (nome, quantidade).</summary>
    public (string nome, long n)? TipoCritico
    {
        get
        {
            if (TotalPorTipo.Length == 0 || TotalPorTipo.Max() == 0) return null;
            int i = Array.IndexOf(TotalPorTipo, TotalPorTipo.Max());
            return (Tipos[i], TotalPorTipo[i]);
        }
    }

    /// <summary>Inspetor com a maior taxa de desvio (só quem tem avaliações).</summary>
    public Insp? MaiorTaxa => Inspetores.Where(i => i.Aval > 0 && i.Desv > 0).OrderByDescending(i => i.Taxa).ThenByDescending(i => i.Desv).FirstOrDefault();

    public int DentroDaMeta => Inspetores.Count(i => i.Taxa <= Ok);

    /// <summary>Parcela de cada tipo nos desvios do inspetor e do time (0 a 1), para o radar.</summary>
    public (double[] insp, double[] time) Perfil(Insp i)
    {
        double ti = Math.Max(1, i.Desv), tt = Math.Max(1, Desvios);
        return (i.PorTipo.Select(v => v / ti).ToArray(), TotalPorTipo.Select(v => v / tt).ToArray());
    }

    /// <summary>Diagnóstico em texto simples para o inspetor escolhido.</summary>
    public string Diagnostico(Insp i, IReadOnlyList<string> origens)
    {
        if (i.Desv == 0) return Origem == null ? "Nenhum desvio no período. Bom trabalho para destacar." : $"Nenhum desvio de origem {Origem} no período.";
        var partes = new List<string>();
        double media = TaxaTime;
        if (media > 0 && i.Taxa >= media * 1.5) partes.Add($"Taxa de desvio {Texto.F(i.Taxa / media, 1)}× a média do time ({Texto.F(i.Taxa, 1)}% contra {Texto.F(media, 1)}%).");
        else if (media > 0 && i.Taxa <= media * 0.6) partes.Add($"Taxa abaixo da média do time ({Texto.F(i.Taxa, 1)}% contra {Texto.F(media, 1)}%).");
        else partes.Add($"Taxa próxima da média do time ({Texto.F(i.Taxa, 1)}% contra {Texto.F(media, 1)}%).");
        if (i.Aval < 50) partes.Add($"Atenção: só {i.Aval} avaliações no período, a taxa pode variar muito.");

        var (pi, pt) = Perfil(i);
        int k = Array.IndexOf(pi, pi.Max());
        var dif = (pi[k] - pt[k]) * 100;
        partes.Add($"Concentra {Math.Round(pi[k] * 100)}% dos desvios em {Tipos[k]}" + (dif >= 5 ? $" ({Math.Round(dif)} p.p. acima do time)." : "."));

        string? principal = Origem;
        if (principal == null && i.PorOrigem.Count > 0)
        {
            var top = i.PorOrigem.OrderByDescending(x => x.Value).First();
            principal = top.Key;
            partes.Add($"A maior parte é de origem {top.Key} ({top.Value} de {i.Desv}).");
        }
        partes.Add(principal != null && Texto.SemAcento(principal).ToLowerInvariant().StartsWith("intern")
            ? "Sugestão: reforçar o checklist interno desse tipo com o inspetor."
            : principal != null && Texto.SemAcento(principal).ToLowerInvariant().StartsWith("chamad")
                ? "Sugestão: revisar os chamados abertos e alinhar o critério com quem abre."
                : "Sugestão: revisar a orientação com a cooperativa sobre esse tipo.");
        return string.Join(" ", partes);
    }
}
