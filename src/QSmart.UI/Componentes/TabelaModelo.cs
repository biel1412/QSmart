namespace QSmart.UI.Componentes;

/// <summary>Coluna da tabela: título, largura em px (0 = ocupa o resto) e alinhamento ('e', 'c', 'd').</summary>
public sealed record ColunaTab(string Titulo, int Largura = 0, char Alinh = 'e', string? Dica = null);

public sealed record EstiloCelula(string? Fundo = null, string? Cor = null, bool Negrito = false, string? Dica = null);

public sealed class LinhaTab
{
    public object?[] Valores { get; init; } = Array.Empty<object?>();
    /// <summary>Chaves de ordenação (opcional); sem elas, ordena pelo número ou pelo texto.</summary>
    public object?[]? Chaves { get; init; }
    /// <summary>Fundo de erro (vermelho claro), como no original.</summary>
    public bool Erro { get; init; }
    public bool Negrito { get; init; }
    public Dictionary<int, EstiloCelula>? Celulas { get; set; }
    public object? Tag { get; init; }

    public static LinhaTab De(params object?[] v) => new() { Valores = v };

    public string Texto(int c) => c < Valores.Length ? Formatar(Valores[c]) : "";

    public static string Formatar(object? v) => v switch
    {
        null => "",
        double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    public object? Chave(int c)
    {
        if (Chaves != null && c < Chaves.Length) return Chaves[c];
        var v = c < Valores.Length ? Valores[c] : null;
        return v is int or long or double or float or decimal ? v : Texto(c);
    }
}

public sealed class ComparadorChave : IComparer<object?>
{
    public static readonly ComparadorChave I = new();
    public int Compare(object? a, object? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;
        if (IsNum(a) && IsNum(b)) return Convert.ToDouble(a).CompareTo(Convert.ToDouble(b));
        if (a is DateTime da && b is DateTime db) return da.CompareTo(db);
        if (a is DateOnly oa && b is DateOnly ob) return oa.CompareTo(ob);
        if (a is TimeOnly ta && b is TimeOnly tb) return ta.CompareTo(tb);
        return string.CompareOrdinal(a.ToString(), b.ToString());
    }
    static bool IsNum(object o) => o is int or long or double or float or decimal or short;
}
