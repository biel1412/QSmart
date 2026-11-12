using System.Text;

namespace QSmart.Core.Planilhas;

/// <summary>Leitor de CSV/TXT: tenta UTF-8 e depois Latin-1, detecta o separador entre ; , tab e |.</summary>
public static class LeitorCsv
{
    public sealed record Resultado(Folha Folha, string Codificacao, char Separador);

    public static (string texto, string enc) Decodificar(byte[] dados)
    {
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            int ini = dados.Length >= 3 && dados[0] == 0xEF && dados[1] == 0xBB && dados[2] == 0xBF ? 3 : 0;
            return (utf8.GetString(dados, ini, dados.Length - ini), "utf-8-sig");
        }
        catch (DecoderFallbackException)
        {
            return (Encoding.Latin1.GetString(dados), "latin-1");
        }
    }

    /// <summary>Equivalente simplificado do csv.Sniffer: o separador que aparece com a mesma contagem nas primeiras linhas.</summary>
    public static char Detectar(string amostra, string candidatos = ";,\t|")
    {
        var linhas = amostra.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Take(30).ToList();
        if (linhas.Count == 0) return ';';
        char melhor = '\0';
        double melhorNota = -1;
        foreach (var c in candidatos)
        {
            var contagens = linhas.Select(l => ContarFora(l, c)).ToList();
            if (contagens[0] == 0) continue;
            var moda = contagens.GroupBy(x => x).OrderByDescending(g => g.Count()).First();
            if (moda.Key == 0) continue;
            double consistencia = moda.Count() / (double)contagens.Count;
            if (consistencia > melhorNota + 1e-9)
            {
                melhorNota = consistencia;
                melhor = c;
            }
        }
        return melhor == '\0' ? ';' : melhor;
    }

    static int ContarFora(string linha, char sep)
    {
        int n = 0;
        bool aspas = false;
        foreach (var ch in linha)
        {
            if (ch == '"') aspas = !aspas;
            else if (ch == sep && !aspas) n++;
        }
        return n;
    }

    public static List<string[]> Separar(string texto, char sep, int? limite = null)
    {
        var res = new List<string[]>();
        var campo = new StringBuilder();
        var linha = new List<string>();
        bool aspas = false;
        int i = 0;
        while (i < texto.Length)
        {
            var c = texto[i];
            if (aspas)
            {
                if (c == '"')
                {
                    if (i + 1 < texto.Length && texto[i + 1] == '"') { campo.Append('"'); i += 2; continue; }
                    aspas = false; i++; continue;
                }
                campo.Append(c); i++; continue;
            }
            if (c == '"' && campo.Length == 0) { aspas = true; i++; continue; }
            if (c == sep) { linha.Add(campo.ToString()); campo.Clear(); i++; continue; }
            if (c == '\r' || c == '\n')
            {
                linha.Add(campo.ToString()); campo.Clear();
                if (!(linha.Count == 1 && linha[0].Length == 0)) res.Add(linha.ToArray());
                linha.Clear();
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n') i++;
                i++;
                if (limite.HasValue && res.Count > limite.Value) return res;
                continue;
            }
            campo.Append(c); i++;
        }
        if (campo.Length > 0 || linha.Count > 0)
        {
            linha.Add(campo.ToString());
            res.Add(linha.ToArray());
        }
        return res;
    }

    public static async Task<Resultado> Ler(byte[] dados, OpcoesLeitura? op = null, Progresso? prog = null, char? sepFixo = null)
    {
        op ??= new OpcoesLeitura();
        var (texto, enc) = Decodificar(dados);
        var sep = sepFixo ?? Detectar(texto.Length > 16384 ? texto[..16384] : texto);
        if (prog != null) await prog.Talvez();
        var linhas = Separar(texto, sep, op.Limite.HasValue ? op.Limite + 1 : null);
        var f = new Folha { Nome = "CSV" };
        if (linhas.Count == 0) return new Resultado(f, enc, sep);
        int nCol = linhas.Max(l => l.Length);
        List<string> nomes;
        int inicio;
        if (op.SemCabecalho)
        {
            nomes = Enumerable.Range(0, nCol).Select(x => x.ToString()).ToList();
            inicio = 0;
        }
        else
        {
            nCol = linhas[0].Length;
            var vistos = new Dictionary<string, int>();
            nomes = new List<string>();
            for (int c = 0; c < nCol; c++)
            {
                var n = linhas[0][c];
                if (string.IsNullOrEmpty(n)) n = $"Unnamed: {c}";
                if (vistos.TryGetValue(n, out var k)) { vistos[n] = k + 1; n = $"{n}.{k}"; }
                else vistos[n] = 1;
                nomes.Add(n);
            }
            inicio = 1;
        }
        var manter = Enumerable.Range(0, nCol).Where(c => op.Colunas == null || op.Colunas(nomes[c])).ToArray();
        f.Colunas = manter.Select(c => nomes[c]).ToList();
        for (int r = inicio; r < linhas.Count; r++)
        {
            var l = linhas[r];
            var row = new Celula[manter.Length];
            for (int j = 0; j < manter.Length; j++)
            {
                var c = manter[j];
                var v = c < l.Length ? l[c] : null;
                row[j] = v == null || LeitorXlsx.ValoresNa.Contains(v) ? Celula.Vazia : new Celula(TipoCelula.Texto, v);
            }
            f.Linhas.Add(row);
            if (op.Limite.HasValue && f.Linhas.Count >= op.Limite.Value) break;
            if (r % 5000 == 0 && prog != null) { prog.Msg = $"linha {Texto.Fmt(r)}"; await prog.Talvez(); }
        }
        return new Resultado(f, enc, sep);
    }
}

/// <summary>Ponto único de leitura: escolhe xlsx ou csv pela extensão.</summary>
public static class Leitor
{
    public static bool EhCsv(Arquivo a) => a.Extensao is ".csv" or ".txt";

    public static async Task<List<Folha>> Ler(Arquivo a, OpcoesLeitura? op = null, Progresso? prog = null)
    {
        if (EhCsv(a))
        {
            var r = await LeitorCsv.Ler(a.Dados, op, prog);
            return new List<Folha> { r.Folha };
        }
        return await LeitorXlsx.Ler(a.Dados, op, prog);
    }
}
