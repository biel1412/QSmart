using System.Text;

namespace QSmart.Core;

/// <summary>Hash de 128 bits gerenciado (o MD5 do .NET não roda no navegador).</summary>
public static class Hash
{
    public static string De(string texto)
    {
        ulong h1 = 14695981039346656037UL, h2 = 0x9E3779B97F4A7C15UL;
        foreach (var c in texto)
        {
            h1 ^= c; h1 *= 1099511628211UL;
            h2 = (h2 ^ c) * 0xBF58476D1CE4E5B9UL; h2 ^= h2 >> 31;
        }
        h1 ^= (ulong)texto.Length; h2 += (ulong)texto.Length * 0x94D049BB133111EBUL;
        return h1.ToString("x16") + h2.ToString("x16");
    }
}
