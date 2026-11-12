using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QSmart.Core.Planilhas;

namespace QSmart.Core;

/// <summary>Aba de inspetor com cabeçalhos normalizados e o número original de cada linha (índice do pandas).</summary>
public sealed class FolhaN
{
    public string Nome = "";
    public List<string> Colunas = new();
    public List<string?[]> Linhas = new();
    /// <summary>Índice da linha na aba original (0 = primeira linha de dados, linha 2 do Excel).</summary>
    public List<int> Idx = new();
    public int Col(string c) => Colunas.IndexOf(c);
    public bool Tem(string c) => Colunas.Contains(c);
    public string? V(int linha, string col) { var c = Col(col); return c < 0 ? null : Linhas[linha][c]; }
}

public sealed class LinhaOrigem
{
    public string Nome { get; set; } = "";
    public long Aval { get; set; }
    public long Desv { get; set; }
    public Dictionary<string, long> PorOrigem { get; set; } = new();
    public long Total => PorOrigem.Values.Sum();
    public long O(string o) => PorOrigem.TryGetValue(o, out var v) ? v : 0;
    /// <summary>Nome exibido (tipo de autorização formatado).</summary>
    public string Exibicao { get; set; } = "";
}

public sealed record CuboInsp(string Aba, string Origem, string Aut, string Erro, bool Desvio, long N);

public sealed class MesAgregado
{
    public string Mes { get; set; } = "";
    public long Desvios { get; set; }
    public long Avaliacoes { get; set; }
    public Dictionary<string, long> PorOrigem { get; set; } = new();
}

/// <summary>Resultado da Evolução (e, com o mesmo formato, do Histórico agregado).</summary>
public sealed class ResultadoInspecao
{
    public string Arquivo { get; set; } = "";
    public List<string> Abas { get; set; } = new();
    public List<(string dup, string orig)> Ignoradas { get; set; } = new();
    public long Avaliacoes { get; set; }
    public long Desvios { get; set; }
    public List<string> Origens { get; set; } = new();
    public Dictionary<string, long> PorOrigem { get; set; } = new();
    public List<LinhaOrigem> PorAut { get; set; } = new();
    public List<LinhaOrigem> PorInspetor { get; set; } = new();
    public List<LinhaOrigem> PorCoop { get; set; } = new();
    public bool TemCoop { get; set; }
    public List<LinhaOrigem> PorColab { get; set; } = new();
    public bool TemColab { get; set; }
    public List<(string colab, string erro, long n)> PorColabErro { get; set; } = new();
    public List<(string erro, long qtd)> PorErro { get; set; } = new();
    public (DateTime ini, DateTime fim)? Periodo { get; set; }
    public string? MesSugerido { get; set; }
    public List<CuboInsp> Cubo { get; set; } = new();
    public List<MesAgregado> Meses { get; set; } = new();
    public List<string> SemDetalhe { get; set; } = new();
    public string FiltrosTexto { get; set; } = "";
    public long Q(string origem) => PorOrigem.TryGetValue(origem, out var v) ? v : 0;
}

public static class Inspecao
{
    public static readonly string[] OrigensPadrao = { "Interno", "Externo", "Chamados" };
    public static readonly HashSet<string> NecessariasEvo = new() { "INTERNO OU EXTERNO", "TIPO DE AUTORIZACAO", "TIPO DE ERRO", "INSPETOR" };

    public static string NormCabecalho(string c) => Texto.SemAcento(c.Trim()).ToUpperInvariant();

    public static async Task<List<Folha>> LerTodasAbas(Arquivo a, Progresso? prog = null)
        => await Leitor.Ler(a, null, prog);

    /// <summary>Converte uma Folha lida em FolhaN (cabeçalhos normalizados, textos).</summary>
    public static FolhaN Normalizar(Folha f)
    {
        var fn = new FolhaN { Nome = f.Nome, Colunas = f.Colunas.Select(NormCabecalho).ToList() };
        for (int i = 0; i < f.Linhas.Count; i++)
        {
            var l = f.Linhas[i];
            fn.Linhas.Add(l.Select(c => c.Tem ? c.Texto : null).ToArray());
            fn.Idx.Add(i);
        }
        return fn;
    }

    public sealed class Separacao
    {
        public List<FolhaN> Validas = new();
        public List<(string dup, string orig)> Copias = new();
        public List<string> Diag = new();
        public Dictionary<string, string> Motivos = new();
    }

    /// <summary>separar_abas_inspecao: escolhe as abas de inspetor (regra única de Evolução, Inspeção e Depurador).</summary>
    public static Separacao SepararAbas(IEnumerable<Folha> folhas)
    {
        var s = new Separacao();
        var assinaturas = new Dictionary<string, string>();
        foreach (var f0 in folhas)
        {
            var f = Normalizar(f0);
            var falta = NecessariasEvo.Where(c => !f.Colunas.Contains(c)).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (falta.Count > 0)
            {
                var txt = string.Join(", ", falta);
                s.Diag.Add($"aba “{f.Nome}”: falta " + txt);
                s.Motivos[f.Nome] = "falta " + txt;
                continue;
            }
            // dropna(how="all")
            var g = new FolhaN { Nome = f.Nome, Colunas = f.Colunas };
            for (int i = 0; i < f.Linhas.Count; i++)
                if (f.Linhas[i].Any(v => v != null)) { g.Linhas.Add(f.Linhas[i]); g.Idx.Add(f.Idx[i]); }
            if (g.Linhas.Count == 0) { s.Motivos[f.Nome] = "aba sem dados"; continue; }
            var ass = Assinatura(g);
            if (assinaturas.TryGetValue(ass, out var orig))
            {
                s.Copias.Add((f.Nome, orig));
                s.Motivos[f.Nome] = $"cópia idêntica da aba “{orig}” (não conta em dobro)";
                continue;
            }
            assinaturas[ass] = f.Nome;
            s.Validas.Add(g);
        }
        return s;
    }

    static string Assinatura(FolhaN f)
    {
        var sb = new StringBuilder();
        sb.Append(f.Linhas.Count).Append('x').Append(f.Colunas.Count).Append('|');
        foreach (var l in f.Linhas)
        {
            foreach (var v in l) sb.Append(v ?? "").Append('\u001f');
            sb.Append('\u001e');
        }
        return Hash.De(sb.ToString());
    }

    static readonly Regex PontoZero = new(@"^\d+\.0$");

    public static string Coop(string? v)
    {
        var t = (v ?? "").Trim();
        if (PontoZero.IsMatch(t)) t = t[..^2];
        return t.Split('_')[0].Trim();
    }

    /// <summary>Linha da base empilhada (todas as abas válidas) com os campos derivados.</summary>
    public sealed class LinhaBase
    {
        public string Aba = "";
        public int Pos;            // posição na aba (FolhaN.Linhas)
        public int Idx;            // índice original
        public string? TipoErroBruto;
        public bool Desvio;
        public string Origem = "", Aut = "", Erro = "";
        public string? Coop, Colab;
    }

    /// <summary>Empilha as abas válidas e devolve, para cada linha, os valores derivados (derivar_inspecao).
    /// Mantém só as linhas avaliadas (TIPO DE ERRO preenchido).</summary>
    public static (List<LinhaBase> linhas, bool temCoop, bool temColab) Derivar(List<FolhaN> validas)
    {
        bool temCoop = validas.Any(f => f.Tem("COOP")), temColab = validas.Any(f => f.Tem("COLABORADOR"));
        var res = new List<LinhaBase>();
        var cErro = new Dictionary<string, (bool, string)>();
        foreach (var f in validas)
        {
            int cTipo = f.Col("TIPO DE ERRO"), cOri = f.Col("INTERNO OU EXTERNO"), cAut = f.Col("TIPO DE AUTORIZACAO");
            int cCoop = f.Col("COOP"), cColab = f.Col("COLABORADOR");
            for (int i = 0; i < f.Linhas.Count; i++)
            {
                var l = f.Linhas[i];
                var te = l[cTipo];
                if (te == null) continue;
                if (!cErro.TryGetValue(te, out var de))
                {
                    var norm = Texto.SemAcento(te.Trim()).ToLowerInvariant();
                    de = (norm != "" && norm != "sem erro", Texto.ColapsarEspacos(te.Trim()).ToUpperInvariant());
                    cErro[te] = de;
                }
                var lb = new LinhaBase
                {
                    Aba = f.Nome, Pos = i, Idx = f.Idx[i], TipoErroBruto = te, Desvio = de.Item1, Erro = de.Item2,
                    Origem = Texto.TitleCase(Texto.SemAcento((l[cOri] ?? "Nao informado").Trim())),
                    Aut = Texto.SemAcento((l[cAut] ?? "NAO INFORMADO").Trim()).ToUpperInvariant(),
                };
                if (temCoop)
                {
                    var c = Coop(cCoop >= 0 ? l[cCoop] : null);
                    lb.Coop = c == "" ? "Não informada" : c;
                }
                if (temColab)
                {
                    var c = (cColab >= 0 ? l[cColab] : null)?.Trim() ?? "";
                    lb.Colab = c == "" ? "(não informado)" : c;
                }
                res.Add(lb);
            }
        }
        return (res, temCoop, temColab);
    }

    static List<string> OrigensDe(IEnumerable<string> presentes)
        => OrigensPadrao.Concat(presentes.Distinct().Where(o => !OrigensPadrao.Contains(o)).OrderBy(x => x, StringComparer.Ordinal)).ToList();

    static Dictionary<string, long> PorOrigemDe(IEnumerable<LinhaBase> desv, List<string> origens)
    {
        var d = origens.ToDictionary(o => o, _ => 0L);
        foreach (var x in desv) if (d.ContainsKey(x.Origem)) d[x.Origem]++;
        return d;
    }

    /// <summary>Contagem em ordem de frequência (value_counts): empates na ordem em que aparecem.</summary>
    public static List<(string valor, long n)> ValueCounts(IEnumerable<string> valores)
    {
        var ordem = new Dictionary<string, (int pos, long n)>(StringComparer.Ordinal);
        int k = 0;
        foreach (var v in valores)
            ordem[v] = ordem.TryGetValue(v, out var x) ? (x.pos, x.n + 1) : (k++, 1);
        return ordem.OrderByDescending(kv => kv.Value.n).ThenBy(kv => kv.Value.pos).Select(kv => (kv.Key, kv.Value.n)).ToList();
    }

    /// <summary>analisar_evolucao: lê TODAS as abas de inspetor e conta os desvios.</summary>
    public static async Task<ResultadoInspecao> AnalisarEvolucao(Arquivo a, Progresso? prog = null)
    {
        var folhas = await LerTodasAbas(a, prog);
        return AnalisarEvolucao(folhas, a.Nome);
    }

    public static ResultadoInspecao AnalisarEvolucao(List<Folha> folhas, string arquivo)
    {
        var sep = SepararAbas(folhas);
        if (sep.Validas.Count == 0)
            throw new InvalidDataException("Nenhuma aba com as colunas de inspeção (INSPETOR, INTERNO OU EXTERNO, " +
                "TIPO DE AUTORIZACAO, TIPO DE ERRO) foi encontrada.\n\n" +
                (sep.Diag.Count > 0 ? string.Join("\n", sep.Diag.Take(8)) : "O arquivo não tem abas com dados."));

        (DateTime, DateTime)? periodo = null;
        string? mesSug = null;
        if (sep.Validas.Any(f => f.Tem("DATA DA INSPECAO")))
        {
            var vals = sep.Validas.SelectMany(f => Enumerable.Range(0, f.Linhas.Count).Select(i => f.V(i, "DATA DA INSPECAO"))).ToList();
            var datas = Datas.ToDatetime(vals).Where(d => d != null).Select(d => d!.Value).ToList();
            if (datas.Count > 0)
            {
                periodo = (datas.Min(), datas.Max());
                mesSug = ValueCounts(datas.Select(d => d.ToString("yyyy-MM", CultureInfo.InvariantCulture))).First().valor;
            }
        }

        var (bas, temCoop, temColab) = Derivar(sep.Validas);
        var desvios = bas.Where(x => x.Desvio).ToList();
        var origens = OrigensDe(desvios.Select(x => x.Origem));
        var r = new ResultadoInspecao
        {
            Arquivo = arquivo,
            Abas = sep.Validas.Select(f => f.Nome).ToList(),
            Ignoradas = sep.Copias,
            Avaliacoes = bas.Count,
            Desvios = desvios.Count,
            Origens = origens,
            PorOrigem = PorOrigemDe(desvios, origens),
            PorErro = ValueCounts(desvios.Select(x => x.Erro)),
            Periodo = periodo,
            MesSugerido = mesSug,
            TemCoop = temCoop,
            TemColab = temColab,
        };

        r.PorAut = bas.Select(x => x.Aut).Distinct().OrderBy(x => x, StringComparer.Ordinal).Select(aut =>
            new LinhaOrigem { Nome = aut, Exibicao = Texto.NomeSegmento(aut), PorOrigem = PorOrigemDe(desvios.Where(d => d.Aut == aut), origens) })
            .OrderByDescending(x => x.Total).ThenBy(x => x.Nome, StringComparer.Ordinal).ToList();

        if (temCoop)
            r.PorCoop = desvios.Select(x => x.Coop!).Distinct().Select(c =>
                new LinhaOrigem { Nome = c, Exibicao = c, PorOrigem = PorOrigemDe(desvios.Where(d => d.Coop == c), origens) })
                .OrderByDescending(x => x.Total).ThenBy(x => x.Nome, StringComparer.Ordinal).ToList();

        if (temColab)
        {
            r.PorColab = bas.GroupBy(x => x.Colab!).Select(g => new LinhaOrigem
            {
                Nome = g.Key, Exibicao = g.Key, Aval = g.Count(), Desv = g.Count(x => x.Desvio),
                PorOrigem = PorOrigemDe(g.Where(x => x.Desvio), origens),
            }).OrderByDescending(x => x.Desv).ThenByDescending(x => x.Aval).ThenBy(x => x.Nome, StringComparer.Ordinal).ToList();
            r.PorColabErro = desvios.GroupBy(x => (x.Colab!, x.Erro)).OrderBy(g => g.Key.Item1, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Erro, StringComparer.Ordinal).Select(g => (g.Key.Item1, g.Key.Erro, (long)g.Count())).ToList();
        }

        var ordemAbas = sep.Validas.Select((f, i) => (f.Nome, i)).ToDictionary(x => x.Nome, x => x.i);
        r.PorInspetor = bas.GroupBy(x => x.Aba).Select(g => new LinhaOrigem
        {
            Nome = g.Key, Exibicao = g.Key, Aval = g.Count(), Desv = g.Count(x => x.Desvio),
            PorOrigem = PorOrigemDe(g.Where(x => x.Desvio), origens),
        }).OrderByDescending(x => x.Desv).ThenByDescending(x => x.Aval).ThenBy(x => ordemAbas[x.Nome]).ToList();

        r.Cubo = bas.GroupBy(x => (x.Aba, x.Origem, x.Aut, x.Erro, x.Desvio))
            .OrderBy(g => g.Key.Aba, StringComparer.Ordinal).ThenBy(g => g.Key.Origem, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Aut, StringComparer.Ordinal).ThenBy(g => g.Key.Erro, StringComparer.Ordinal).ThenBy(g => g.Key.Desvio)
            .Select(g => new CuboInsp(g.Key.Aba, g.Key.Origem, g.Key.Aut, g.Key.Erro, g.Key.Desvio, g.Count())).ToList();
        return r;
    }
}
