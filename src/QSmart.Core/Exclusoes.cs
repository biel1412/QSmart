namespace QSmart.Core;

/// <summary>Um fluxo (uma linha do compilado), guardado de forma compacta para contar as exclusões.</summary>
public readonly record struct FluxoExclusao(int U, int S, string Dia, int J);

/// <summary>Os fluxos lidos na planilha do Resumo, para identificar exclusões pelas justificativas.</summary>
public sealed class FluxosExclusao
{
    public string[] Usuarios = Array.Empty<string>();
    public string[] Segmentos = Array.Empty<string>();
    /// <summary>Justificativas distintas, já sem acento e em maiúsculas (o índice J aponta para cá).</summary>
    public string[] Justificativas = Array.Empty<string>();
    public List<FluxoExclusao> Lista = new();
}

public sealed class ResultadoExclusoes
{
    public long Total;
    public Dictionary<string, long> PorUsuario = new(StringComparer.Ordinal);
}

/// <summary>
/// Exclusões identificadas (aba Usuários): conta os fluxos cuja JUSTIFICATIVA tem alguma das palavras de exclusão
/// como palavra inteira (sem acento, sem diferença de maiúsculas). Vale para todos os fluxos da planilha.
/// </summary>
public static class Exclusoes
{
    public static readonly string[] TermosPadrao = { "EXCLUSAO", "EXCLUIDAS", "EXCLUIDOS", "EXCLUIDA", "EXCLUIDO", "EXCLUIR", "EXCL", "EXC" };

    /// <summary>Termo no formato de comparação: maiúsculas, sem acento, só letras e números.</summary>
    public static string NormTermo(string t) => new string(Texto.SemAcento(t).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Palavras da justificativa. "EXC." e "EXCLUSÃO:" viram EXC e EXCLUSAO.</summary>
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

    /// <summary>A justificativa tem algum termo como palavra inteira? (não pega "EXCEPTION" nem "EXCEDE")</summary>
    public static bool TemExclusao(string norm, HashSet<string> termos) => termos.Count > 0 && Palavras(norm).Any(termos.Contains);

    public static FluxosExclusao Montar(Volume.Derivado d)
    {
        var f = new FluxosExclusao();
        var ui = new Dictionary<string, int>(StringComparer.Ordinal); var us = new List<string>();
        var si = new Dictionary<string, int>(StringComparer.Ordinal); var ss = new List<string>();
        var ji = new Dictionary<string, int>(StringComparer.Ordinal); var js = new List<string>();
        int Idx(Dictionary<string, int> mapa, List<string> lista, string v) { if (!mapa.TryGetValue(v, out var k)) { k = lista.Count; mapa[v] = k; lista.Add(v); } return k; }
        var cacheJ = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < d.Usuario.Length; i++)
        {
            int jj = -1;
            if (d.Justificativa[i] is { Length: > 0 } jt)
            {
                if (!cacheJ.TryGetValue(jt, out var nj)) cacheJ[jt] = nj = Texto.SemAcento(jt).ToUpperInvariant();
                jj = Idx(ji, js, nj);
            }
            f.Lista.Add(new FluxoExclusao(Idx(ui, us, d.Usuario[i]), Idx(si, ss, d.Seg[i]), d.Data[i], jj));
        }
        f.Usuarios = us.ToArray(); f.Segmentos = ss.ToArray(); f.Justificativas = js.ToArray();
        return f;
    }

    /// <summary>Conta as exclusões por usuário, respeitando os filtros do Resumo (usuários, segmentos e dias).</summary>
    public static ResultadoExclusoes Calcular(FluxosExclusao f, FiltrosVolume filtros, IEnumerable<string> termos)
    {
        var t = termos.Select(NormTermo).Where(x => x != "").ToHashSet(StringComparer.Ordinal);
        var exc = f.Justificativas.Select(j => TemExclusao(j, t)).ToArray();
        var r = new ResultadoExclusoes();
        foreach (var x in f.Lista)
        {
            if (x.J < 0 || !exc[x.J]) continue;
            var u = f.Usuarios[x.U];
            if (filtros.Usuarios.Count > 0 && !filtros.Usuarios.Contains(u)) continue;
            if (filtros.Segmentos.Count > 0 && !filtros.Segmentos.Contains(f.Segmentos[x.S])) continue;
            if (filtros.Dias.Count > 0 && !filtros.Dias.Contains(x.Dia)) continue;
            r.PorUsuario[u] = r.PorUsuario.GetValueOrDefault(u) + 1;
            r.Total++;
        }
        return r;
    }
}
