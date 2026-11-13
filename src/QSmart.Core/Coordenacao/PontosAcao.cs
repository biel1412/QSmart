using QSmart.Core.Planilhas;

namespace QSmart.Core.Coordenacao;

public sealed class LinhaPontos
{
    public string Usuario = "";
    public long Total;
    /// <summary>Dias em que o usuário teve pelo menos uma ação.</summary>
    public int DiasTrabalhados;
    /// <summary>Divisor usado na média (dias trabalhados ou todos os dias do período).</summary>
    public int Divisor;
    public double Media;
    public long MenorDia, MaiorDia;
    public DateOnly? DataMenor, DataMaior, Primeiro, Ultimo;
    public Dictionary<DateOnly, long> PorDia = new();
}

public sealed class ResultadoPontos
{
    public List<LinhaPontos> Usuarios = new();
    public List<DateOnly> Dias = new();
    public long Acoes, SemData, SemUsuario;
    public bool PorDiaTrabalhado;
    public List<(string nome, int linhas)> Arquivos = new();
    public double MediaGeral => Usuarios.Count == 0 ? 0 : Usuarios.Average(u => u.Media);
    public List<LinhaPontos> Abaixo(double limite) => Usuarios.Where(u => u.Media < limite).OrderBy(u => u.Media).ThenBy(u => u.Usuario, StringComparer.Ordinal).ToList();
}

/// <summary>Pontos de ação mensal: média diária de ações (linhas do compilado) por usuário e quem fica abaixo do limite.</summary>
public static class PontosAcao
{
    public const double LimitePadrao = 150;

    public static ResultadoPontos Calcular(BaseCompilados b, bool porDiaTrabalhado = true)
    {
        var df = b.Df;
        var r = new ResultadoPontos { Dias = b.Dias.ToList(), PorDiaTrabalhado = porDiaTrabalhado, Arquivos = b.Arquivos.ToList() };
        int cu = df.Col("USUARIO");
        var nomes = new Dictionary<string, string>(StringComparer.Ordinal);
        var cont = new Dictionary<string, Dictionary<DateOnly, long>>(StringComparer.Ordinal);
        for (int i = 0; i < df.Linhas.Count; i++)
        {
            var k = b.U[i];
            if (k == "" || k == "NAN") { r.SemUsuario++; continue; }
            if (b.Dia[i] is not DateOnly d) { r.SemData++; continue; }
            if (!cont.TryGetValue(k, out var pd))
            {
                cont[k] = pd = new();
                nomes[k] = (df.V(i, cu) ?? k).Trim();
            }
            pd[d] = pd.GetValueOrDefault(d) + 1;
            r.Acoes++;
        }
        int diasPeriodo = r.Dias.Count;
        foreach (var (k, pd) in cont)
        {
            var ordenado = pd.OrderBy(x => x.Key).ToList();
            var menor = ordenado.MinBy(x => x.Value); var maior = ordenado.MaxBy(x => x.Value);
            long total = ordenado.Sum(x => x.Value);
            int div = porDiaTrabalhado ? ordenado.Count : Math.Max(1, diasPeriodo);
            r.Usuarios.Add(new LinhaPontos
            {
                Usuario = nomes[k], Total = total, DiasTrabalhados = ordenado.Count, Divisor = div, Media = total / (double)div,
                MenorDia = menor.Value, DataMenor = menor.Key, MaiorDia = maior.Value, DataMaior = maior.Key,
                Primeiro = ordenado[0].Key, Ultimo = ordenado[^1].Key, PorDia = pd,
            });
        }
        r.Usuarios = r.Usuarios.OrderByDescending(u => u.Media).ThenBy(u => u.Usuario, StringComparer.Ordinal).ToList();
        return r;
    }

    /// <summary>Faixa de cor pelo percentual da meta: abaixo de 50%, de 50 a 80%, de 80% até a meta.</summary>
    public static int Faixa(double media, double limite) => limite <= 0 ? 2 : media / limite < 0.5 ? 0 : media / limite < 0.8 ? 1 : 2;
    public static string NomeFaixa(int f) => f switch { 0 => "Crítico (abaixo de 50% da meta)", 1 => "Atenção (50% a 80%)", _ => "Próximo da meta (80% ou mais)" };

    public static byte[] Exportar(ResultadoPontos r, double limite)
    {
        var wb = new XlsxWriter();
        var abaixo = r.Abaixo(limite);
        var cores = new[] { "FDECEC", "FEF3E2", "FFF9DB" };
        var s = wb.Planilha($"Abaixo de {Core.Texto.G(limite)}");
        s.Cabecalho("Usuário", "Média por dia", "% da meta", "Faltam por dia", "Total de ações", "Dias trabalhados", "Menor dia", "Data do menor", "Maior dia", "Data do maior", "Faixa");
        foreach (var u in abaixo)
        {
            int f = Faixa(u.Media, limite);
            s.Linha(u.Usuario, Texto.PyRound(u.Media, 1), Texto.PyRound(u.Media / limite * 100, 1), Texto.PyRound(limite - u.Media, 1), u.Total, u.DiasTrabalhados,
                u.MenorDia, u.DataMenor is DateOnly a ? Texto.Dmy(a) : "", u.MaiorDia, u.DataMaior is DateOnly b ? Texto.Dmy(b) : "", NomeFaixa(f));
            s.PreencherLinha(s.N - 1, cores[f]);
        }
        s.Congelar(1, 1);
        s.AutoLargura(10, 40);

        var t = wb.Planilha("Todos os usuários");
        t.Cabecalho("Usuário", "Média por dia", "Total de ações", "Dias trabalhados", "Menor dia", "Maior dia", "Primeiro dia", "Último dia", "Situação");
        foreach (var u in r.Usuarios)
        {
            t.Linha(u.Usuario, Texto.PyRound(u.Media, 1), u.Total, u.DiasTrabalhados, u.MenorDia, u.MaiorDia,
                u.Primeiro is DateOnly p ? Texto.Dmy(p) : "", u.Ultimo is DateOnly q ? Texto.Dmy(q) : "", u.Media < limite ? $"Abaixo de {Core.Texto.G(limite)}" : "OK");
            if (u.Media < limite) t.Preencher(t.N - 1, 8, "FDECEC", true);
        }
        t.Congelar(1, 1);
        t.AutoLargura(10, 40);

        var m = wb.Planilha("Por dia (abaixo)");
        m.Cabecalho(new object?[] { "Usuário" }.Concat(r.Dias.Select(d => (object?)Texto.Dmy(d))).Append("Total").Append("Média").ToArray());
        foreach (var u in abaixo)
            m.Linha(new object?[] { u.Usuario }.Concat(r.Dias.Select(d => (object?)(u.PorDia.TryGetValue(d, out var n) ? n : null)))
                .Append(u.Total).Append(Texto.PyRound(u.Media, 1)).ToArray());
        m.Congelar(1, 1);
        m.Largura(0, 26);

        var so = wb.Planilha("Sobre");
        so.Cabecalho("Item", "Valor");
        so.Linha("Compilado(s)", string.Join(", ", r.Arquivos.Select(x => x.nome)));
        so.Linha("Período", r.Dias.Count > 0 ? $"{Texto.Dmy(r.Dias[0])} a {Texto.Dmy(r.Dias[^1])} ({r.Dias.Count} dias com registros)" : "");
        so.Linha("Ações contadas", r.Acoes);
        so.Linha("Usuários", r.Usuarios.Count);
        so.Linha("Limite (média por dia)", limite);
        so.Linha("Usuários abaixo do limite", abaixo.Count);
        so.Linha("Média calculada por", r.PorDiaTrabalhado ? "dia trabalhado (dias em que o usuário teve ações)" : "todos os dias do período");
        if (r.SemData > 0) so.Linha("Linhas sem data (fora da conta)", r.SemData);
        if (r.SemUsuario > 0) so.Linha("Linhas sem usuário (fora da conta)", r.SemUsuario);
        so.Linha("Gerado em", Texto.Agora());
        so.AutoLargura(10, 70);
        return wb.Salvar();
    }
}
