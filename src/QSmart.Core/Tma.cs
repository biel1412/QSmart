using System.Globalization;

namespace QSmart.Core;

/// <summary>Um fluxo (uma linha do compilado) guardado de forma compacta para o TMA, exclusões e suspeitos.</summary>
public readonly record struct FluxoTma(int U, int S, string Dia, int Segundos, bool MesmoDia, int J, int Linha);

/// <summary>Fluxo com menos de <see cref="Tma.LimiteSuspeito"/> segundos (para conferência).</summary>
public sealed record FluxoSuspeito(string Usuario, string Segmento, string Registro, string Procedimento, DateTime Inicio, DateTime Fim, int Segundos);

/// <summary>Tudo o que o TMA precisa, montado uma vez na leitura da planilha do Resumo.</summary>
public sealed class FluxosTma
{
    public string[] Usuarios = Array.Empty<string>();
    public string[] Segmentos = Array.Empty<string>();
    /// <summary>Justificativas distintas, já sem acento e em maiúsculas (o índice J aponta para cá).</summary>
    public string[] Justificativas = Array.Empty<string>();
    public List<FluxoTma> Lista = new();
    /// <summary>Detalhe dos fluxos rápidos (abaixo do limite), para a lista de conferência.</summary>
    public List<FluxoSuspeito> Rapidos = new();
    public bool TemDatas;
}

public sealed record TmaSegmento(string Segmento, string Nome, long Fluxos, long Considerados, long Segundos, long Suspeitos, long Exclusoes)
{
    public long Tma => Considerados == 0 ? 0 : Segundos / Considerados;   // trunca, como o sistema atual
}

public sealed record TmaUsuario(string Usuario, long Fluxos, long Considerados, long Segundos, long Suspeitos, long Exclusoes, List<TmaSegmento> PorSegmento, string Busca)
{
    public long Tma => Considerados == 0 ? 0 : Segundos / Considerados;
}

public sealed class ResultadoTma
{
    public long Fluxos, Considerados, Segundos, Suspeitos, Exclusoes;
    public long Tma => Considerados == 0 ? 0 : Segundos / Considerados;
    public bool SoMesmoDia;
    public bool TemDatas;
    public List<TmaSegmento> Segmentos = new();
    public Dictionary<string, TmaUsuario> Usuarios = new(StringComparer.Ordinal);
    public List<FluxoSuspeito> SuspeitosLista = new();
}

/// <summary>
/// TMA (tempo médio de atendimento) por usuário e por segmento, exclusões identificadas e TMA suspeito.
/// Fluxo = cada linha do compilado. Tempo = DATAHORAFIMATIVIDADE − DATAHORAINICIOATIVIDADE.
/// Entram todos os fluxos que têm início e fim (de qualquer dia); a opção soMesmoDia reproduz o sistema antigo,
/// que só olhava fluxos iniciados e concluídos no mesmo dia. A média é simples e corta os segundos quebrados. Segmento = prefixo do IDREGISTROCONTROLADO (antes do #).
/// </summary>
public static class Tma
{
    public const int LimiteSuspeito = 10;

    public static readonly string[] TermosPadrao = { "EXCLUSAO", "EXCLUIDAS", "EXCLUIDOS", "EXCLUIDA", "EXCLUIDO", "EXCLUIR", "EXCL", "EXC" };

    /// <summary>Termo no formato de comparação: maiúsculas, sem acento, só letras e números.</summary>
    public static string NormTermo(string t) => new string(Texto.SemAcento(t).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Palavras da justificativa (sem acento, maiúsculas). "EXC." e "EXCLUSÃO:" viram EXC e EXCLUSAO.</summary>
    public static IEnumerable<string> Palavras(string norm)
    {
        int i = 0;
        while (i < norm.Length)
        {
            while (i < norm.Length && !char.IsLetterOrDigit(norm[i])) i++;
            int j = i;
            while (j < norm.Length && char.IsLetterOrDigit(norm[j])) j++;
            if (j > i) yield return norm[i..j];
            i = j;
        }
    }

    /// <summary>A justificativa tem algum termo de exclusão como palavra inteira? (não pega "EXCEPTION" nem "EXCEDE")</summary>
    public static bool TemExclusao(string norm, HashSet<string> termos) => termos.Count > 0 && Palavras(norm).Any(termos.Contains);

    public static FluxosTma Montar(Volume.Tabela df, Volume.Derivado d)
    {
        var f = new FluxosTma();
        int n = df.Linhas.Count;
        var ui = new Dictionary<string, int>(StringComparer.Ordinal); var us = new List<string>();
        var si = new Dictionary<string, int>(StringComparer.Ordinal); var ss = new List<string>();
        var ji = new Dictionary<string, int>(StringComparer.Ordinal); var js = new List<string>();
        int Idx(Dictionary<string, int> mapa, List<string> lista, string v) { if (!mapa.TryGetValue(v, out var k)) { k = lista.Count; mapa[v] = k; lista.Add(v); } return k; }

        DateTime?[] ini = new DateTime?[n], fim = new DateTime?[n];
        f.TemDatas = df.Colunas.Contains("DATAHORAINICIOATIVIDADE") && df.Colunas.Contains("DATAHORAFIMATIVIDADE");
        if (f.TemDatas) { ini = LerDatas(df.Coluna("DATAHORAINICIOATIVIDADE")); fim = LerDatas(df.Coluna("DATAHORAFIMATIVIDADE")); }
        var reg = df.Colunas.Contains("IDREGISTROCONTROLADO") ? df.Coluna("IDREGISTROCONTROLADO") : new string?[n];
        var cacheJ = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < n; i++)
        {
            int seg = -1; bool mesmo = false;
            if (ini[i] is DateTime a && fim[i] is DateTime b && b >= a)
            {
                seg = (int)Math.Min(int.MaxValue, (long)(b - a).TotalSeconds);
                mesmo = a.Date == b.Date;
            }
            int jj = -1;
            if (d.Justificativa[i] is { Length: > 0 } jt)
            {
                if (!cacheJ.TryGetValue(jt, out var nj)) cacheJ[jt] = nj = Texto.SemAcento(jt).ToUpperInvariant();
                jj = Idx(ji, js, nj);
            }
            var u = Idx(ui, us, d.Usuario[i]); var s = Idx(si, ss, d.Seg[i]);
            f.Lista.Add(new FluxoTma(u, s, d.Data[i], seg, mesmo, jj, i));
            if (seg >= 0 && seg < LimiteSuspeito)
                f.Rapidos.Add(new FluxoSuspeito(d.Usuario[i], d.Seg[i], reg[i] ?? "", d.Proc[i], ini[i]!.Value, fim[i]!.Value, seg));
        }
        f.Usuarios = us.ToArray(); f.Segmentos = ss.ToArray(); f.Justificativas = js.ToArray();
        return f;
    }

    /// <summary>Datas com hora: dd/mm/aaaa hh:mm:ss (o formato do compilado) ou o que o leitor geral entender.</summary>
    static DateTime?[] LerDatas(string?[] serie)
    {
        var r = new DateTime?[serie.Length];
        int falhas = 0;
        for (int i = 0; i < serie.Length; i++)
        {
            var s = serie[i]?.Trim();
            if (string.IsNullOrEmpty(s)) continue;
            if (DateTime.TryParseExact(s, new[] { "dd/MM/yyyy HH:mm:ss", "dd/MM/yyyy H:mm:ss", "dd/MM/yyyy HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) r[i] = d;
            else falhas++;
        }
        if (falhas > 0)
        {
            var outro = Datas.ToDatetime(serie, diaPrimeiro: true);
            for (int i = 0; i < r.Length; i++) r[i] ??= outro[i];
        }
        return r;
    }

    public static ResultadoTma Calcular(FluxosTma f, FiltrosVolume filtros, IEnumerable<string> termos, bool soMesmoDia = false)
    {
        var t = termos.Select(NormTermo).Where(x => x != "").ToHashSet(StringComparer.Ordinal);
        var excJ = f.Justificativas.Select(j => TemExclusao(j, t)).ToArray();
        bool okU(int u) => filtros.Usuarios.Count == 0 || filtros.Usuarios.Contains(f.Usuarios[u]);
        bool okS(int s) => filtros.Segmentos.Count == 0 || filtros.Segmentos.Contains(f.Segmentos[s]);

        // acumuladores por (usuário, segmento): fluxos, considerados, segundos, suspeitos, exclusões
        var acc = new Dictionary<(int u, int s), long[]>();
        foreach (var x in f.Lista)
        {
            if (!okU(x.U) || !okS(x.S) || (filtros.Dias.Count > 0 && !filtros.Dias.Contains(x.Dia))) continue;
            if (!acc.TryGetValue((x.U, x.S), out var a)) acc[(x.U, x.S)] = a = new long[5];
            a[0]++;
            if (x.Segundos >= 0 && (!soMesmoDia || x.MesmoDia))
            {
                a[1]++; a[2] += x.Segundos;
                if (x.Segundos < LimiteSuspeito) a[3]++;
            }
            if (x.J >= 0 && excJ[x.J]) a[4]++;
        }

        var r = new ResultadoTma { SoMesmoDia = soMesmoDia, TemDatas = f.TemDatas };
        TmaSegmento Seg(int s, IEnumerable<long[]> v)
        {
            long a0 = 0, a1 = 0, a2 = 0, a3 = 0, a4 = 0;
            foreach (var a in v) { a0 += a[0]; a1 += a[1]; a2 += a[2]; a3 += a[3]; a4 += a[4]; }
            var nome = f.Segmentos[s];
            return new TmaSegmento(nome, Texto.NomeSegmento(nome), a0, a1, a2, a3, a4);
        }
        r.Segmentos = acc.GroupBy(kv => kv.Key.s).Select(g => Seg(g.Key, g.Select(kv => kv.Value)))
            .OrderByDescending(x => x.Fluxos).ThenBy(x => x.Segmento, StringComparer.Ordinal).ToList();
        foreach (var g in acc.GroupBy(kv => kv.Key.u))
        {
            var porSeg = g.Select(kv => Seg(kv.Key.s, new[] { kv.Value })).OrderByDescending(x => x.Fluxos).ThenBy(x => x.Segmento, StringComparer.Ordinal).ToList();
            var nome = f.Usuarios[g.Key];
            r.Usuarios[nome] = new TmaUsuario(nome, porSeg.Sum(x => x.Fluxos), porSeg.Sum(x => x.Considerados), porSeg.Sum(x => x.Segundos),
                porSeg.Sum(x => x.Suspeitos), porSeg.Sum(x => x.Exclusoes), porSeg, Texto.NormalizarBusca(nome));
        }
        r.Fluxos = r.Segmentos.Sum(x => x.Fluxos); r.Considerados = r.Segmentos.Sum(x => x.Considerados); r.Segundos = r.Segmentos.Sum(x => x.Segundos);
        r.Suspeitos = r.Segmentos.Sum(x => x.Suspeitos); r.Exclusoes = r.Segmentos.Sum(x => x.Exclusoes);
        r.SuspeitosLista = f.Rapidos.Where(x => (filtros.Usuarios.Count == 0 || filtros.Usuarios.Contains(x.Usuario))
                                              && (filtros.Segmentos.Count == 0 || filtros.Segmentos.Contains(x.Segmento))
                                              && (filtros.Dias.Count == 0 || filtros.Dias.Contains(x.Fim.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)))
                                              && (!soMesmoDia || x.Inicio.Date == x.Fim.Date))
            .OrderBy(x => x.Usuario, StringComparer.Ordinal).ThenBy(x => x.Inicio).ToList();
        return r;
    }

    /// <summary>Segundos em HH:MM:SS (passa de 24h se precisar).</summary>
    public static string Hms(long s)
    {
        if (s < 0) s = 0;
        return $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
    }
}
