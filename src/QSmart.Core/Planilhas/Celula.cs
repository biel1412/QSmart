using System.Globalization;

namespace QSmart.Core.Planilhas;

public enum TipoCelula : byte { Vazia, Texto, Inteiro, Numero, Bool, Data, Hora }

/// <summary>
/// Valor de uma célula. <see cref="Texto"/> é a representação que o pandas daria (str(valor)),
/// e é o que as regras do programa usam quando a leitura é "como texto".
/// </summary>
public readonly struct Celula
{
    public readonly TipoCelula Tipo;
    public readonly string? Texto;
    public readonly double Numero;
    public readonly DateTime Data;

    public Celula(TipoCelula tipo, string? texto, double numero = 0, DateTime data = default)
    {
        Tipo = tipo; Texto = texto; Numero = numero; Data = data;
    }

    public static readonly Celula Vazia = new(TipoCelula.Vazia, null);
    public bool EhVazia => Tipo == TipoCelula.Vazia;
    public bool Tem => Tipo != TipoCelula.Vazia;

    public static Celula DeTexto(string? s) => s == null ? Vazia : new Celula(TipoCelula.Texto, s);

    public static Celula DeData(DateTime d)
        => new(TipoCelula.Data, FormatarData(d), 0, d);

    public static string FormatarData(DateTime d)
    {
        var s = d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (d.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            var micro = (d.Ticks % TimeSpan.TicksPerSecond) / 10;
            s += "." + micro.ToString("000000", CultureInfo.InvariantCulture);
        }
        return s;
    }

    public override string ToString() => Texto ?? "";
}

/// <summary>Uma aba lida da planilha: nomes das colunas (como o pandas nomearia) e as linhas.</summary>
public sealed class Folha
{
    public string Nome { get; set; } = "";
    public List<string> Colunas { get; set; } = new();
    public List<Celula[]> Linhas { get; set; } = new();
    public int N => Linhas.Count;

    public int Indice(string coluna) => Colunas.IndexOf(coluna);

    public Celula Valor(int linha, int col) => col >= 0 && col < Linhas[linha].Length ? Linhas[linha][col] : Celula.Vazia;

    /// <summary>Coluna como texto (null = vazio), como o pandas com dtype=str.</summary>
    public string?[] ColunaTexto(int col)
    {
        var r = new string?[Linhas.Count];
        if (col < 0) return r;
        for (int i = 0; i < Linhas.Count; i++)
        {
            var l = Linhas[i];
            r[i] = col < l.Length && l[col].Tem ? l[col].Texto : null;
        }
        return r;
    }

    public string?[] ColunaTexto(string nome) => ColunaTexto(Indice(nome));

    public Celula[] Coluna(int col)
    {
        var r = new Celula[Linhas.Count];
        if (col < 0) { Array.Fill(r, Celula.Vazia); return r; }
        for (int i = 0; i < Linhas.Count; i++)
        {
            var l = Linhas[i];
            r[i] = col < l.Length ? l[col] : Celula.Vazia;
        }
        return r;
    }

    public bool LinhaVazia(int i) => Linhas[i].All(c => c.EhVazia);
}

/// <summary>Arquivo enviado pelo usuário (nome + conteúdo).</summary>
public sealed record Arquivo(string Nome, byte[] Dados)
{
    public string Extensao => Path.GetExtension(Nome).ToLowerInvariant();
}
