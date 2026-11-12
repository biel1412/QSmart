using QSmart.Core.Planilhas;

namespace QSmart.Core;

public sealed record LinhaInterna(string Inspetor, int Linha, DateOnly? Dia, bool Avaliada, bool Desvio, string Colaborador, string Erro);

public sealed class ResultadoInternos
{
    public string Arquivo { get; init; } = "";
    public List<LinhaInterna> Df { get; init; } = new();
    public List<string> Inspetores { get; init; } = new();
    public Dictionary<string, long> Origens { get; init; } = new();
    public List<string> Ignoradas { get; init; } = new();
    public List<(string dup, string orig)> Copias { get; init; } = new();
}

public sealed class InspetorInterno
{
    public string Inspetor = "";
    public int Total, Desvios, Dias, Max, SemData;
    public double Media;
    public string DiaMax = "", Ultimo = "";
}

public sealed class ResumoInternos
{
    public List<DateOnly> Dias = new();
    public List<(DateOnly dia, int n, int d)> PorDia = new();
    public List<InspetorInterno> Inspetores = new();
    /// <summary>Matriz inspetor × dia (linhas em ordem alfabética, colunas = dias).</summary>
    public List<(string inspetor, int[] valores)> Matriz = new();
    public int Total, ComData, SemData, Desvios;
    public double MediaDia, MediaInspDia;
}

public static class Internos
{
    /// <summary>analisar_internos: inspeções do tipo INTERNO de cada aba de inspetor (com ou sem desvio), com a data.</summary>
    public static ResultadoInternos Analisar(List<Folha> folhas, string arquivo)
    {
        var sep = Inspecao.SepararAbas(folhas);
        if (sep.Validas.Count == 0)
            throw new InvalidDataException("Nenhuma aba de inspetor encontrada (precisa de INSPETOR, INTERNO OU EXTERNO, " +
                                           "TIPO DE AUTORIZACAO e TIPO DE ERRO).\n\n" + string.Join("\n", sep.Diag));
        var df = new List<LinhaInterna>();
        var origens = new Dictionary<string, long>();
        var ordemOrigens = new List<string>();
        foreach (var f in sep.Validas)
        {
            int cOri = f.Col("INTERNO OU EXTERNO"), cErro = f.Col("TIPO DE ERRO"), cData = f.Col("DATA DA INSPECAO"), cCol = f.Col("COLABORADOR");
            var nomeCache = new Dictionary<string, string>();
            for (int i = 0; i < f.Linhas.Count; i++)
            {
                var l = f.Linhas[i];
                var o = l[cOri] != null ? Texto.TitleCase(Texto.SemAcento(l[cOri]!.Trim())) : "(vazio)";
                if (!origens.ContainsKey(o)) ordemOrigens.Add(o);
                origens[o] = origens.GetValueOrDefault(o) + 1;
                if (o != "Interno") continue;
                var erro = l[cErro];
                bool avaliada = erro != null;
                var norm = erro != null ? Texto.SemAcento(erro.Trim()).ToLowerInvariant() : "";
                bool desvio = avaliada && norm != "" && norm != "sem erro";
                var dia = cData >= 0 ? Datas.DataCelula(l[cData]) : null;
                df.Add(new LinhaInterna(f.Nome, f.Idx[i] + 2, dia, avaliada, desvio,
                    cCol >= 0 ? (l[cCol] ?? "").Trim() : "", (erro ?? "").Trim()));
            }
        }
        return new ResultadoInternos
        {
            Arquivo = arquivo,
            Df = df,
            Inspetores = sep.Validas.Select(f => f.Nome).ToList(),
            Origens = ordemOrigens.ToDictionary(o => o, o => origens[o]),
            Ignoradas = sep.Motivos.Select(kv => $"{kv.Key} ({kv.Value})").ToList(),
            Copias = sep.Copias,
        };
    }

    /// <summary>resumo_internos: totais por dia, por inspetor e a matriz inspetor × dia (só linhas com data).</summary>
    public static ResumoInternos Resumo(List<LinhaInterna> df)
    {
        var r = new ResumoInternos();
        var com = df.Where(x => x.Dia != null).ToList();
        r.Dias = com.Select(x => x.Dia!.Value).Distinct().OrderBy(d => d).ToList();
        r.PorDia = r.Dias.Select(d =>
        {
            var g = com.Where(x => x.Dia == d).ToList();
            return (d, g.Count, g.Count(x => x.Desvio));
        }).ToList();
        foreach (var g in df.GroupBy(x => x.Inspetor))
        {
            var gd = g.Where(x => x.Dia != null).ToList();
            var cont = gd.GroupBy(x => x.Dia!.Value).Select(x => (dia: x.Key, n: x.Count())).OrderBy(x => x.dia).ToList();
            int nd = cont.Count;
            var ii = new InspetorInterno
            {
                Inspetor = g.Key, Total = g.Count(), Desvios = g.Count(x => x.Desvio), Dias = nd,
                Media = nd > 0 ? gd.Count / (double)nd : 0.0, SemData = g.Count(x => x.Dia == null),
            };
            if (nd > 0)
            {
                ii.Max = cont.Max(x => x.n);
                ii.DiaMax = Texto.Dm(cont.First(x => x.n == ii.Max).dia);
                ii.Ultimo = Texto.Dmy(cont.Max(x => x.dia));
            }
            r.Inspetores.Add(ii);
        }
        r.Inspetores = r.Inspetores.OrderByDescending(x => x.Total).ToList();
        if (com.Count > 0)
            r.Matriz = com.Select(x => x.Inspetor).Distinct().OrderBy(x => x, StringComparer.Ordinal)
                .Select(n => (n, r.Dias.Select(d => com.Count(x => x.Inspetor == n && x.Dia == d)).ToArray())).ToList();
        var pares = com.GroupBy(x => (x.Inspetor, x.Dia)).Select(g => g.Count()).ToList();
        r.Total = df.Count;
        r.ComData = com.Count;
        r.SemData = df.Count(x => x.Dia == null);
        r.Desvios = df.Count(x => x.Desvio);
        r.MediaDia = r.Dias.Count > 0 ? com.Count / (double)r.Dias.Count : 0.0;
        r.MediaInspDia = pares.Count > 0 ? pares.Average() : 0.0;
        return r;
    }

    public static byte[] Exportar(List<LinhaInterna> df, ResumoInternos r)
    {
        var wb = new XlsxWriter();
        var s1 = wb.Planilha("Por dia");
        s1.Cabecalho("Dia", "Semana", "Inspeções internas", "Com desvio", "Sem desvio");
        foreach (var (d, n, dd) in r.PorDia) s1.Linha(Texto.Dmy(d), Texto.DiaSemana(d), n, dd, n - dd);
        var s2 = wb.Planilha("Por inspetor");
        s2.Cabecalho("Inspetor", "Inspeções internas", "Com desvio", "Sem desvio", "Dias com inspeção", "Média por dia", "Máximo num dia", "Dia do máximo", "Sem data");
        foreach (var x in r.Inspetores)
            s2.Linha(x.Inspetor, x.Total, x.Desvios, x.Total - x.Desvios, x.Dias, Texto.PyRound(x.Media, 1), x.Max, x.DiaMax, x.SemData);
        if (r.Matriz.Count > 0)
        {
            var s3 = wb.Planilha("Inspetor x dia");
            s3.Cabecalho(new object?[] { "Inspetor" }.Concat(r.Dias.Select(d => (object?)Texto.Dm(d))).Append("Total").ToArray());
            foreach (var (n, v) in r.Matriz) s3.Linha(new object?[] { n }.Concat(v.Select(x => (object?)x)).Append(v.Sum()).ToArray());
            var tot = r.Dias.Select((_, j) => r.Matriz.Sum(m => m.valores[j])).ToArray();
            s3.Linha(new object?[] { "Total" }.Concat(tot.Select(x => (object?)x)).Append(tot.Sum()).ToArray());
        }
        var s4 = wb.Planilha("Linhas internas");
        s4.Cabecalho("Aba (inspetor)", "Linha", "Dia", "Avaliada", "Desvio", "Colaborador", "Tipo de erro");
        foreach (var x in df)
            s4.Linha(x.Inspetor, x.Linha, x.Dia != null ? Texto.Dmy(x.Dia.Value) : "(sem data)", x.Avaliada, x.Desvio ? "Sim" : "Não", x.Colaborador, x.Erro);
        foreach (var s in wb.Planilhas) { s.NegritoLinha(0); s.Congelar(1, 1); }
        return wb.Salvar();
    }
}
