using System.Globalization;
using QSmart.Core.Planilhas;

namespace QSmart.Core.Coordenacao;

// =====================================================================================
//  Férias pela CLT, por coordenação:
//  - 12 meses trabalhados (período aquisitivo) liberam 30 dias de férias;
//  - elas têm de ser tiradas nos 12 meses seguintes (período concessivo) — o fim dele é o LIMITE;
//  - dá para dividir em até 3 períodos (um com 14 dias ou mais, os outros com 5 ou mais)
//    e vender até 10 dias (abono);
//  - cada coordenação pode ter até 3 pessoas de férias no mesmo mês (o geral = coordenações × 3);
//  - quem está marcado como "não impacta" não entra nas contas.
// =====================================================================================

/// <summary>Uma linha da base de colaboradores (planilha carregada no Perfil da coordenação).</summary>
public sealed record LinhaBase(string Nome, DateOnly? Admissao, string Lideranca, string Coordenacao);

/// <summary>Pessoa no painel de férias.</summary>
public sealed class ColabFer
{
    public string Id { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Lideranca { get; set; } = "";
    public string Coordenacao { get; set; } = "";
    public DateOnly? Admissao { get; set; }
    public FeriasPessoa F { get; set; } = new();
    /// <summary>É da coordenação de quem está vendo (pode editar).</summary>
    public bool Minha { get; set; }
}

/// <summary>Um ciclo de férias: 12 meses trabalhados liberam; os 12 seguintes são o prazo.</summary>
public sealed record Ciclo(int N, DateOnly AquisIni, DateOnly AquisFim, DateOnly Libera, DateOnly Limite);

public enum NivelFer { SemAdmissao, Aguardando, Ok, Atencao, Urgente, Estourado, Completo }

/// <summary>Como está a pessoa hoje: qual ciclo está aberto, quantos dias faltam marcar e até quando.</summary>
public sealed record SituacaoFer(Ciclo? Ciclo, int Marcados, int Vendidos, int Faltam, DateOnly? InicioMax, int DiasAteLimite, NivelFer Nivel)
{
    public string Rotulo => Nivel switch
    {
        NivelFer.SemAdmissao => "Sem data de admissão",
        NivelFer.Aguardando => "Ainda não liberou",
        NivelFer.Completo => "Tudo marcado",
        NivelFer.Estourado => "Não cabe mais no prazo",
        NivelFer.Urgente => "Urgente",
        NivelFer.Atencao => "Atenção",
        _ => "No prazo",
    };
}

public sealed record AvisoFer(bool Erro, string Texto);

public sealed record SugestaoFer(DateOnly Inicio, int Dias, string Motivo, int Custo)
{
    public DateOnly Fim => Inicio.AddDays(Dias - 1);
}

public static class FeriasClt
{
    public const int LimitePorCoordenacao = 3;
    public const int MaxVenda = 10;
    static readonly string[] DiasSemana = { "domingo", "segunda", "terça", "quarta", "quinta", "sexta", "sábado" };
    public static readonly string[] Meses = { "jan", "fev", "mar", "abr", "mai", "jun", "jul", "ago", "set", "out", "nov", "dez" };
    public static readonly string[] MesesLongos = { "janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro" };
    public static string Mes(int ano, int mes) => $"{ano:0000}-{mes:00}";
    public static string MesNome(string chave)
    {
        var p = chave.Split('-');
        return p.Length == 2 && int.TryParse(p[1], out var m) && m is >= 1 and <= 12 ? $"{MesesLongos[m - 1]}/{p[0]}" : chave;
    }

    // ================================================================== ciclos
    public static Ciclo CicloN(DateOnly adm, int n) =>
        new(n, adm.AddYears(n), adm.AddYears(n + 1).AddDays(-1), adm.AddYears(n + 1), adm.AddYears(n + 2).AddDays(-1));

    /// <summary>O ciclo a que um período pertence (o que estava liberado quando ele começa).</summary>
    public static Ciclo? CicloDe(DateOnly adm, DateOnly inicio)
    {
        if (inicio < adm.AddYears(1)) return null;
        int n = inicio.Year - adm.Year - 1;
        if (n < 0) n = 0;
        while (CicloN(adm, n + 1).Libera <= inicio) n++;
        while (n > 0 && CicloN(adm, n).Libera > inicio) n--;
        return CicloN(adm, n);
    }

    public static (int marcados, int vendidos) Contas(FeriasPessoa f, Ciclo c)
    {
        var ps = f.Periodos.Where(p => p.Inicio >= c.Libera && p.Inicio <= c.Limite).ToList();
        return (ps.Sum(p => Math.Max(0, p.Dias)), ps.Any(p => p.Vende) ? MaxVenda : 0);
    }

    public static SituacaoFer Situacao(ColabFer c, DateOnly hoje)
    {
        if (c.Admissao is not { } adm) return new(null, 0, 0, 0, null, 0, NivelFer.SemAdmissao);
        // primeiro ciclo cujo prazo ainda não passou
        int n = 0;
        while (CicloN(adm, n).Limite < hoje) n++;
        var ciclo = CicloN(adm, n);
        var (m, v) = Contas(c.F, ciclo);
        if (m + v >= 30)
        {
            // esse está completo: mostra o próximo (que ainda vai liberar)
            var prox = CicloN(adm, n + 1);
            var (m2, v2) = Contas(c.F, prox);
            if (m2 + v2 >= 30) return new(prox, m2, v2, 0, null, prox.Limite.DayNumber - hoje.DayNumber, NivelFer.Completo);
            if (prox.Libera > hoje.AddDays(120)) return new(ciclo, m, v, 0, null, ciclo.Limite.DayNumber - hoje.DayNumber, NivelFer.Completo);
            ciclo = prox; m = m2; v = v2;
        }
        int faltam = Math.Max(0, 30 - m - v);
        var inicioMax = ciclo.Limite.AddDays(-Math.Max(1, faltam) + 1);
        int ate = ciclo.Limite.DayNumber - hoje.DayNumber;
        int folga = inicioMax.DayNumber - hoje.DayNumber;
        var nivel = ciclo.Libera > hoje && faltam > 0 ? NivelFer.Aguardando
            : faltam == 0 ? NivelFer.Completo
            : folga < 0 ? NivelFer.Estourado
            : folga <= 45 ? NivelFer.Urgente
            : folga <= 120 ? NivelFer.Atencao
            : NivelFer.Ok;
        if (nivel == NivelFer.Aguardando && folga <= 120) nivel = NivelFer.Atencao;
        return new(ciclo, m, v, faltam, inicioMax, ate, nivel);
    }

    // ================================================================== validação (CLT art. 134 e 143)
    public static List<AvisoFer> Validar(ColabFer c, DateOnly hoje)
    {
        var av = new List<AvisoFer>();
        if (c.Admissao is not { } adm) { av.Add(new(false, "Sem data de admissão: não dá para saber o prazo. Atualize a planilha base no Perfil.")); return av; }
        var ps = c.F.Periodos.OrderBy(p => p.Inicio).ToList();
        for (int i = 1; i < ps.Count; i++)
            if (ps[i].Inicio <= ps[i - 1].Fim) av.Add(new(true, $"Os períodos de {ps[i - 1].Inicio:dd/MM} e {ps[i].Inicio:dd/MM} se sobrepõem."));
        foreach (var g in ps.GroupBy(p => CicloDe(adm, p.Inicio)))
        {
            if (g.Key is not { } ci)
            {
                foreach (var p in g) av.Add(new(true, $"Férias em {p.Inicio:dd/MM/yyyy} começam antes de liberar (só depois de 12 meses: {adm.AddYears(1):dd/MM/yyyy})."));
                continue;
            }
            var lista = g.ToList();
            var tit = $"Ciclo {ci.Libera:MM/yyyy}–{ci.Limite:MM/yyyy}";
            if (lista.Count > 3) av.Add(new(true, $"{tit}: no máximo 3 períodos."));
            int venda = lista.Any(p => p.Vende) ? MaxVenda : 0;
            int soma = lista.Sum(p => p.Dias);
            if (soma + venda > 30) av.Add(new(true, $"{tit}: {soma} dias{(venda > 0 ? " + 10 vendidos" : "")} passam de 30."));
            if (lista.Count(p => p.Vende) > 1) av.Add(new(true, $"{tit}: a venda de 10 dias é uma só por ciclo."));
            if (lista.Count > 1 && !lista.Any(p => p.Dias >= 14)) av.Add(new(true, $"{tit}: dividindo, um dos períodos precisa ter 14 dias ou mais."));
            foreach (var p in lista.Where(p => p.Dias < 5)) av.Add(new(true, $"{tit}: o período de {p.Inicio:dd/MM} tem {p.Dias} dias (mínimo 5)."));
            foreach (var p in lista.Where(p => p.Fim > ci.Limite)) av.Add(new(true, $"O período de {p.Inicio:dd/MM/yyyy} termina depois do limite ({ci.Limite:dd/MM/yyyy}): férias vencidas são pagas em dobro."));
            if (soma + venda < 30 && ci.Limite < hoje.AddDays(120)) av.Add(new(false, $"{tit}: faltam {30 - soma - venda} dias para marcar e o limite é {ci.Limite:dd/MM/yyyy}."));
        }
        foreach (var p in ps.Where(p => p.Inicio.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday))
            av.Add(new(false, $"Férias que começam em {p.Inicio:dd/MM} ({DiasSemana[(int)p.Inicio.DayOfWeek]}): a CLT não deixa começar nos 2 dias antes do descanso semanal."));
        return av;
    }

    // ================================================================== carga por mês
    public static IEnumerable<string> MesesDe(PeriodoFer p)
    {
        var d = new DateOnly(p.Inicio.Year, p.Inicio.Month, 1);
        while (d <= p.Fim) { yield return Mes(d.Year, d.Month); d = d.AddMonths(1); }
    }

    public sealed class Carga
    {
        /// <summary>mês → coordenação → pessoas (ids) que têm algum dia de férias no mês.</summary>
        public Dictionary<string, Dictionary<string, HashSet<string>>> Por = new();
        public List<string> Coordenacoes = new();
        public int LimiteGeral => Math.Max(1, Coordenacoes.Count) * LimitePorCoordenacao;
        public int Na(string mes, string coord) => Por.TryGetValue(mes, out var x) && x.TryGetValue(coord, out var s) ? s.Count : 0;
        public int Geral(string mes) => Por.TryGetValue(mes, out var x) ? x.Values.Sum(s => s.Count) : 0;
        public bool Estoura(string mes, string coord) => Na(mes, coord) > LimitePorCoordenacao;
        public bool EstouraGeral(string mes) => Geral(mes) > LimiteGeral;
        public HashSet<string> Quem(string mes, string coord) => Por.TryGetValue(mes, out var x) && x.TryGetValue(coord, out var s) ? s : new();
    }

    public static Carga Calcular(IEnumerable<ColabFer> todos, string? ignorar = null)
    {
        var c = new Carga();
        var lista = todos.ToList();
        c.Coordenacoes = lista.Select(x => x.Coordenacao).Where(x => x != "").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        foreach (var p in lista)
        {
            if (p.F.SemImpacto || p.Id == ignorar) continue;
            foreach (var per in p.F.Periodos)
                foreach (var m in MesesDe(per))
                {
                    if (!c.Por.TryGetValue(m, out var pc)) c.Por[m] = pc = new(StringComparer.OrdinalIgnoreCase);
                    if (!pc.TryGetValue(p.Coordenacao, out var s)) pc[p.Coordenacao] = s = new();
                    s.Add(p.Id);
                }
        }
        return c;
    }

    /// <summary>Meses em que a pessoa faz a coordenação dela passar do limite.</summary>
    public static List<string> Conflitos(ColabFer p, Carga carga)
    {
        if (p.F.SemImpacto) return new();
        return p.F.Periodos.SelectMany(MesesDe).Distinct().Where(m => carga.Estoura(m, p.Coordenacao) || carga.EstouraGeral(m)).OrderBy(m => m).ToList();
    }

    // ================================================================== sugestões
    /// <summary>Duas datas boas e próximas para um período de <paramref name="dias"/> dias.</summary>
    public static List<SugestaoFer> Sugerir(ColabFer p, IEnumerable<ColabFer> todos, int dias, DateOnly hoje, PeriodoFer? atual = null, int quantas = 2)
    {
        var res = new List<SugestaoFer>();
        if (p.Admissao is not { } adm || dias < 1) return res;
        var lista = todos.ToList();
        var carga = Calcular(lista, ignorar: p.Id);
        // de que ciclo é: o do período atual, ou o que está aberto
        var ciclo = atual != null ? CicloDe(adm, atual.Inicio) : Situacao(p, hoje).Ciclo;
        if (ciclo == null) ciclo = CicloN(adm, 0);
        var de = ciclo.Libera > hoje.AddDays(7) ? ciclo.Libera : hoje.AddDays(7);
        var ate = ciclo.Limite.AddDays(-dias + 1);
        if (ate < de) { ate = de.AddDays(60); }   // já não cabe: sugere o mais cedo possível
        var refe = atual?.Inicio ?? (de > hoje.AddDays(30) ? de : hoje.AddDays(30));
        // as outras férias da mesma pessoa não podem encostar
        var outras = p.F.Periodos.Where(x => x != atual).ToList();
        // dias de férias de quem é da mesma liderança (para não deixar a equipe vazia)
        var mesmaLid = lista.Where(x => x.Id != p.Id && !x.F.SemImpacto && x.Lideranca != "" && string.Equals(x.Lideranca, p.Lideranca, StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.F.Periodos).ToList();

        var cands = new List<(DateOnly ini, int custo, string motivo)>();
        for (var d = de; d <= ate; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Monday) continue;
            var fim = d.AddDays(dias - 1);
            if (outras.Any(o => d <= o.Fim.AddDays(1) && fim >= o.Inicio.AddDays(-1))) continue;
            int custo = 0, piorCoord = 0, piorGeral = 0;
            var mesesAqui = new List<string>();
            for (var m = new DateOnly(d.Year, d.Month, 1); m <= fim; m = m.AddMonths(1))
            {
                var k = Mes(m.Year, m.Month); mesesAqui.Add(k);
                int nc = carga.Na(k, p.Coordenacao) + 1, ng = carga.Geral(k) + 1;
                piorCoord = Math.Max(piorCoord, nc); piorGeral = Math.Max(piorGeral, ng);
                if (nc > LimitePorCoordenacao) custo += 1000 * (nc - LimitePorCoordenacao);
                if (ng > carga.LimiteGeral) custo += 400 * (ng - carga.LimiteGeral);
                custo += 25 * (nc - 1);
            }
            int juntos = mesmaLid.Sum(o => Math.Max(0, Math.Min(fim.DayNumber, o.Fim.DayNumber) - Math.Max(d.DayNumber, o.Inicio.DayNumber) + 1));
            custo += juntos * 8;
            custo += Math.Abs(d.DayNumber - refe.DayNumber) / 3;
            if (fim > ciclo.Limite) custo += 5000;
            var motivo = $"{(piorCoord <= LimitePorCoordenacao ? $"{piorCoord} de {LimitePorCoordenacao} na coordenação" : $"coordenação com {piorCoord} (limite {LimitePorCoordenacao})")}" +
                         $" · {(juntos == 0 ? "ninguém da liderança junto" : $"{juntos} dia(s) junto com a liderança")}";
            cands.Add((d, custo, motivo));
        }
        foreach (var c in cands.OrderBy(c => c.custo).ThenBy(c => Math.Abs(c.ini.DayNumber - refe.DayNumber)))
        {
            if (atual != null && c.ini == atual.Inicio) continue;
            if (res.Any(r => Math.Abs(r.Inicio.DayNumber - c.ini.DayNumber) < 14)) continue;
            res.Add(new SugestaoFer(c.ini, dias, c.motivo, c.custo));
            if (res.Count == quantas) break;
        }
        return res.OrderBy(r => r.Inicio).ToList();
    }

    // ================================================================== planilha base
    /// <summary>Lê a planilha base: Nome, Data de admissão, Liderança e Coordenação (a ordem das colunas não importa).</summary>
    public static async Task<List<LinhaBase>> LerBase(Arquivo arq)
    {
        var folhas = await Leitor.Ler(arq, new OpcoesLeitura { SemCabecalho = true });
        foreach (var f in folhas)
        {
            // procura a linha do cabeçalho nas 15 primeiras
            for (int h = 0; h < Math.Min(15, f.N); h++)
            {
                var cab = f.Linhas[h].Select(c => Ferias.Norm(c.Texto)).ToArray();
                int Col(params string[] nomes) => Array.FindIndex(cab, x => nomes.Any(n => x == n || x.StartsWith(n + " ")));
                int cn = Col("nome", "colaborador", "nome completo", "funcionario");
                int ca = Col("admissao", "data de admissao", "data admissao", "dt admissao", "admitido em");
                int cl = Col("lideranca", "lider", "lideranca imediata", "supervisor", "supervisao", "equipe", "time");
                int cc = Col("coordenacao", "coordenador", "coordenadora", "coord");
                if (cn < 0 || cl < 0 || cc < 0) continue;
                var lista = new List<LinhaBase>();
                for (int i = h + 1; i < f.N; i++)
                {
                    var nome = (f.Valor(i, cn).Texto ?? "").Trim();
                    if (nome == "") continue;
                    lista.Add(new LinhaBase(System.Text.RegularExpressions.Regex.Replace(nome, @"\s+", " "), ca >= 0 ? Data(f.Valor(i, ca)) : null,
                        (f.Valor(i, cl).Texto ?? "").Trim(), (f.Valor(i, cc).Texto ?? "").Trim()));
                }
                if (lista.Count > 0) return lista;
            }
        }
        throw new InvalidDataException("Não achei o cabeçalho. A planilha precisa das colunas Nome, Data de admissão, Liderança e Coordenação.");
    }

    static DateOnly? Data(Celula c)
    {
        if (c.Tipo == TipoCelula.Data) return DateOnly.FromDateTime(c.Data);
        if (c.Tipo is TipoCelula.Numero or TipoCelula.Inteiro && c.Numero > 10000 && c.Numero < 80000) return LeitorXlsx.DeSerial(c.Numero) is { } dt ? DateOnly.FromDateTime(dt) : null;
        var t = (c.Texto ?? "").Trim();
        if (t == "") return null;
        foreach (var fmt in new[] { "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "dd-MM-yyyy", "dd.MM.yyyy" })
            if (DateTime.TryParseExact(t, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return DateOnly.FromDateTime(d);
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 10000 && n < 80000 && LeitorXlsx.DeSerial(n) is { } dd) return DateOnly.FromDateTime(dd);
        return null;
    }

    /// <summary>Modelo em branco da planilha base.</summary>
    public static byte[] Modelo()
    {
        var wb = new XlsxWriter();
        var s = wb.Planilha("Colaboradores");
        s.Cabecalho("Nome", "Data de admissão", "Liderança", "Coordenação");
        s.NegritoLinha(0);
        s.Linha("Maria da Silva", new DateOnly(2024, 3, 11), "Diogo Machado", "Lilian Brum");
        s.Linha("João Souza", new DateOnly(2025, 8, 4), "Larissa Muhl", "Lilian Brum");
        s.FormatoData(1, 1, 2);
        foreach (var (c, w) in new[] { (0, 34.0), (1, 16.0), (2, 24.0), (3, 24.0) }) s.Largura(c, w);
        return wb.Salvar();
    }
}
