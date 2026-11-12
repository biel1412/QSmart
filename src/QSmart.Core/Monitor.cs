using QSmart.Core.Planilhas;

namespace QSmart.Core;

public sealed class BaseMonitor
{
    /// <summary>Linhas válidas: chave do usuário, nome original, fim da atividade.</summary>
    public List<(string u, string usuario, DateTime fim)> Df = new();
    public List<(string nome, int linhas)> Arquivos = new();
    public int SemHora;
    public List<DateOnly> Dias = new();
    /// <summary>(chave, nome mais usado, atividades), do mais ativo para o menos ativo.</summary>
    public List<(string chave, string nome, int n)> Usuarios = new();
}

public sealed class Pausa
{
    public string Chave = "", Colaborador = "", Tipo = "";
    public DateOnly Dia;
    public DateTime De, Ate;
    public double Min;
}

public sealed class DiaMonitor
{
    public string Chave = "", Colaborador = "";
    public DateOnly Dia;
    public int Atividades, Antes, Depois, Pausas;
    public DateTime Primeira, Ultima;
    public (double min, DateTime? de, DateTime? ate) Maior;
    public double Bruto, Desconto, Liquido, Janela, Util, Pct;
}

public sealed class ColabMonitor
{
    public string Chave = "", Colaborador = "";
    public int Dias, Atividades, Pausas;
    public double Media, Pct, Total;
    public DiaMonitor? Mais, Menos;
}

public sealed class ParamMonitor
{
    public string Inicio = "";
    public double Gap, Intervalo;
    public string Fim = "";
    public override bool Equals(object? obj) => obj is ParamMonitor p && p.Inicio == Inicio && p.Gap == Gap && p.Intervalo == Intervalo && p.Fim == Fim;
    public override int GetHashCode() => HashCode.Combine(Inicio, Gap, Intervalo, Fim);
}

public sealed class ResultadoMonitor
{
    public List<DiaMonitor> DiasL = new();
    public List<Pausa> Pausas = new();
    public List<ColabMonitor> PorCol = new();
    public List<(DateOnly dia, int colaboradores, double media, double total, int atividades)> PorDia = new();
    public List<DateOnly> Dias = new();
    public double MediaDia, Pct;
    public int NPausas;
    public ParamMonitor Param = new();
}

public static class Monitor
{
    /// <summary>preparar_monitor: lê os compilados guardando só usuário e data/hora de FIM de cada atividade.</summary>
    public static async Task<BaseMonitor> Preparar(IEnumerable<Arquivo> arquivos, Progresso? prog = null)
    {
        var cols = new HashSet<string> { "USUARIO", "DATAHORAFIMATIVIDADE", "PROCEDIMENTO", "ATIVIDADE" };
        var b = new BaseMonitor();
        var brutos = new List<(string? usuario, string? fim)>();
        foreach (var a in arquivos.GroupBy(x => (x.Nome, x.Dados.Length)).Select(g => g.First()))
        {
            if (prog != null) prog.Msg = a.Nome;
            var f = await Compilados.Ler(a, cols, prog);
            int cf = f.Col("DATAHORAFIMATIVIDADE");
            if (cf < 0) throw new InvalidDataException($"{a.Nome}: não tem a coluna DATAHORAFIMATIVIDADE.");
            int cu = f.Col("USUARIO");
            b.Arquivos.Add((a.Nome, f.Linhas.Count));
            foreach (var l in f.Linhas) brutos.Add((l[cu], l[cf]));
        }
        var txt = brutos.Select(x => x.fim == null ? "nan" : x.fim.Trim()).ToList();
        var fim = txt.Select(t => Datas.Strptime(t, "%d/%m/%Y %H:%M:%S")).ToArray();
        var faltaIdx = Enumerable.Range(0, fim.Length).Where(i => fim[i] == null && brutos[i].fim != null).ToList();
        if (faltaIdx.Count > 0)
        {
            var conv = Datas.ToDatetime(faltaIdx.Select(i => (string?)txt[i]).ToList(), diaPrimeiro: true);
            for (int k = 0; k < faltaIdx.Count; k++) fim[faltaIdx[k]] = conv[k];
        }
        b.SemHora = fim.Count(x => x == null);
        var cache = new Dictionary<string, string>();
        for (int i = 0; i < brutos.Count; i++)
        {
            if (fim[i] == null) continue;
            var v = brutos[i].usuario ?? "";
            if (!cache.TryGetValue(v, out var u)) cache[v] = u = Compilados.NormUsuario(v);
            if (u == "") continue;
            b.Df.Add((u, brutos[i].usuario!, fim[i]!.Value));
        }
        b.Dias = b.Df.Select(x => DateOnly.FromDateTime(x.fim)).Distinct().OrderBy(d => d).ToList();
        var nomes = b.Df.GroupBy(x => x.u).ToDictionary(g => g.Key, g =>
            g.GroupBy(x => x.usuario.Trim()).OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key);
        b.Usuarios = Inspecao.ValueCounts(b.Df.Select(x => x.u)).Select(x => (x.valor, nomes[x.valor], (int)x.n)).ToList();
        return b;
    }

    /// <summary>calcular_ociosidade: tempo ocioso por colaborador e por dia.</summary>
    public static ResultadoMonitor Calcular(BaseMonitor b, List<string> usuarios, TimeOnly inicio, double gapMin, double intervaloMin, TimeOnly? fimJornada)
    {
        var nomes = b.Usuarios.ToDictionary(x => x.chave, x => x.nome);
        string Nome(string u) => nomes.TryGetValue(u, out var n) ? n : u;
        var setU = usuarios.ToHashSet();
        var sel = b.Df.Where(x => setU.Contains(x.u)).OrderBy(x => x.u, StringComparer.Ordinal).ThenBy(x => x.fim).ToList();
        var r = new ResultadoMonitor();
        foreach (var g in sel.GroupBy(x => (x.u, dia: DateOnly.FromDateTime(x.fim))).OrderBy(g => g.Key.u, StringComparer.Ordinal).ThenBy(g => g.Key.dia))
        {
            var (u, d) = g.Key;
            var t = g.Select(x => x.fim).ToList();
            var ini = d.ToDateTime(inicio);
            DateTime? fimRef = fimJornada != null ? d.ToDateTime(fimJornada.Value) : null;
            var dentro = t.Where(x => x > ini && (fimRef == null || x <= fimRef)).ToList();
            int antes = t.Count(x => x <= ini);
            int depois = t.Count(x => fimRef != null && x > fimRef);
            var marcos = new List<(DateTime, string)> { (ini, "inicio") };
            marcos.AddRange(dentro.Select(x => (x, "fim")));
            if (fimRef != null && (dentro.Count == 0 || dentro[^1] < fimRef)) marcos.Add((fimRef.Value, "jornada"));
            double bruto = 0;
            (double, DateTime?, DateTime?) maior = (0.0, null, null);
            int nP = 0;
            for (int k = 0; k + 1 < marcos.Count; k++)
            {
                var (a, ta) = marcos[k];
                var (bb, tb) = marcos[k + 1];
                var dur = (bb - a).TotalSeconds / 60;
                if (dur > gapMin)
                {
                    var tipo = ta == "inicio" ? "Início da jornada → 1ª finalização" : tb == "jornada" ? "Última finalização → fim da jornada" : "Entre finalizações";
                    r.Pausas.Add(new Pausa { Chave = u, Colaborador = Nome(u), Dia = d, De = a, Ate = bb, Min = dur, Tipo = tipo });
                    bruto += dur;
                    nP++;
                    if (dur > maior.Item1) maior = (dur, a, bb);
                }
            }
            var fimJanela = fimRef ?? (dentro.Count > 0 ? dentro[^1] : ini);
            var janela = Math.Max(0.0, (fimJanela - ini).TotalSeconds / 60);
            var desconto = Math.Min(intervaloMin, bruto);
            var liquido = bruto - desconto;
            var util = Math.Max(0.0, janela - intervaloMin);
            r.DiasL.Add(new DiaMonitor
            {
                Chave = u, Colaborador = Nome(u), Dia = d, Atividades = t.Count, Antes = antes, Depois = depois,
                Primeira = t[0], Ultima = t[^1], Pausas = nP, Maior = maior, Bruto = bruto, Desconto = desconto,
                Liquido = liquido, Janela = janela, Util = util, Pct = util > 0 ? liquido / util * 100 : 0.0,
            });
        }
        foreach (var u in usuarios)
        {
            var ds = r.DiasL.Where(x => x.Chave == u).ToList();
            if (ds.Count == 0) { r.PorCol.Add(new ColabMonitor { Chave = u, Colaborador = Nome(u) }); continue; }
            double tot = ds.Sum(x => x.Liquido), util = ds.Sum(x => x.Util);
            r.PorCol.Add(new ColabMonitor
            {
                Chave = u, Colaborador = Nome(u), Dias = ds.Count, Atividades = ds.Sum(x => x.Atividades), Media = tot / ds.Count,
                Pct = util > 0 ? tot / util * 100 : 0.0, Pausas = ds.Sum(x => x.Pausas),
                Mais = ds.Aggregate((a, c) => c.Liquido > a.Liquido ? c : a), Menos = ds.Aggregate((a, c) => c.Liquido < a.Liquido ? c : a),
                Total = tot,
            });
        }
        r.PorCol = r.PorCol.OrderByDescending(x => x.Media).ToList();
        r.Dias = r.DiasL.Select(x => x.Dia).Distinct().OrderBy(d => d).ToList();
        foreach (var d in r.Dias)
        {
            var ds = r.DiasL.Where(x => x.Dia == d).ToList();
            r.PorDia.Add((d, ds.Count, ds.Sum(x => x.Liquido) / ds.Count, ds.Sum(x => x.Liquido), ds.Sum(x => x.Atividades)));
        }
        double totUtil = r.DiasL.Sum(x => x.Util), totLiq = r.DiasL.Sum(x => x.Liquido);
        r.MediaDia = r.DiasL.Count > 0 ? totLiq / r.DiasL.Count : 0.0;
        r.Pct = totUtil > 0 ? totLiq / totUtil * 100 : 0.0;
        r.NPausas = r.Pausas.Count;
        r.Param = new ParamMonitor
        {
            Inicio = inicio.ToString("HH:mm"), Gap = gapMin, Intervalo = intervaloMin, Fim = fimJornada?.ToString("HH:mm") ?? "",
        };
        return r;
    }

    public static byte[] Exportar(ResultadoMonitor r, BaseMonitor b)
    {
        var p = r.Param;
        var wb = new XlsxWriter();
        var s = wb.Planilha("Parâmetros");
        s.Cabecalho("Parâmetro", "Valor");
        s.Linha("Planilha(s)", string.Join(", ", b.Arquivos.Select(x => x.nome)));
        s.Linha("Início da jornada", p.Inicio);
        s.Linha("Fim da jornada", p.Fim != "" ? p.Fim : "não informado (vai até a última finalização)");
        s.Linha("Pausa contada a partir de", $"mais de {Texto.G(p.Gap)} min entre uma finalização e outra");
        s.Linha("Intervalo descontado", p.Intervalo != 0 ? $"{Texto.G(p.Intervalo)} min por dia" : "não");
        s.Linha("Colaboradores", r.PorCol.Count);
        s.Linha("Dias", r.Dias.Count);
        s.Linha("Ociosidade média por dia", Texto.FmtMin(r.MediaDia));
        s.Linha("Ociosidade média (%)", Texto.PyRound(r.Pct, 1));
        s.Linha("Gerado em", Texto.Agora());

        var c = wb.Planilha("Por colaborador");
        c.Cabecalho("Colaborador", "Dias", "Atividades", "Ocioso médio/dia (min)", "Ocioso médio/dia", "% ocioso", "Pausas", "Ocioso total (min)",
            "Dia mais ocioso", "Min. no dia mais ocioso", "Dia menos ocioso", "Min. no dia menos ocioso");
        foreach (var x in r.PorCol)
            c.Linha(x.Colaborador, x.Dias, x.Atividades, (long)Texto.PyRound(x.Media, 0), Texto.FmtMin(x.Media), Texto.PyRound(x.Pct, 1), x.Pausas,
                (long)Texto.PyRound(x.Total, 0), x.Mais != null ? Texto.Dmy(x.Mais.Dia) : "", x.Mais != null ? (object)(long)Texto.PyRound(x.Mais.Liquido, 0) : "",
                x.Menos != null ? Texto.Dmy(x.Menos.Dia) : "", x.Menos != null ? (object)(long)Texto.PyRound(x.Menos.Liquido, 0) : "");

        var d = wb.Planilha("Dia a dia");
        d.Cabecalho("Dia", "Semana", "Colaborador", "Atividades", "1ª finalização", "Última finalização", "Finalizadas antes do início", "Pausas",
            "Maior pausa (min)", "Maior pausa (horário)", "Ocioso bruto (min)", "Intervalo descontado (min)", "Ocioso (min)", "Ocioso", "% ocioso");
        foreach (var x in r.DiasL.OrderBy(x => x.Dia).ThenBy(x => x.Colaborador, StringComparer.Ordinal))
            d.Linha(Texto.Dmy(x.Dia), Texto.DiaSemana(x.Dia), x.Colaborador, x.Atividades, Texto.Hms(x.Primeira), Texto.Hms(x.Ultima), x.Antes, x.Pausas,
                (long)Texto.PyRound(x.Maior.min, 0), x.Maior.de != null ? $"{Texto.Hm(x.Maior.de.Value)}–{Texto.Hm(x.Maior.ate!.Value)}" : "",
                (long)Texto.PyRound(x.Bruto, 0), (long)Texto.PyRound(x.Desconto, 0), (long)Texto.PyRound(x.Liquido, 0), Texto.FmtMin(x.Liquido), Texto.PyRound(x.Pct, 1));

        var pa = wb.Planilha("Pausas");
        pa.Cabecalho("Colaborador", "Dia", "De", "Até", "Duração (min)", "Tipo");
        foreach (var x in r.Pausas) pa.Linha(x.Colaborador, Texto.Dmy(x.Dia), Texto.Hms(x.De), Texto.Hms(x.Ate), Texto.PyRound(x.Min, 1), x.Tipo);

        var pd = wb.Planilha("Por dia");
        pd.Cabecalho("Dia", "Semana", "Colaboradores", "Atividades", "Ocioso médio por colaborador (min)", "Ocioso médio");
        foreach (var x in r.PorDia) pd.Linha(Texto.Dmy(x.dia), Texto.DiaSemana(x.dia), x.colaboradores, x.atividades, (long)Texto.PyRound(x.media, 0), Texto.FmtMin(x.media));

        foreach (var a in wb.Planilhas) { a.NegritoLinha(0); a.Congelar(1, 0); a.AutoLargura(12, 42); }
        return wb.Salvar();
    }
}
