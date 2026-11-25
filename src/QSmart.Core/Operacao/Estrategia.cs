using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QSmart.Core.Planilhas;

namespace QSmart.Core.Operacao;

/// <summary>Compilado do dia já lido, só com o que a Estratégia usa.</summary>
public sealed class BaseEstrategia
{
    public List<(string nome, int linhas)> Arquivos = new();
    /// <summary>Usuário normalizado (Compilados.NormUsuario).</summary>
    public string[] U = Array.Empty<string>();
    /// <summary>Serviço = IDREGISTROCONTROLADO antes do “#”.</summary>
    public string[] Servico = Array.Empty<string>();
    public string[] Procedimento = Array.Empty<string>();
    public DateTime?[] Inicio = Array.Empty<DateTime?>();
    public DateTime?[] Fim = Array.Empty<DateTime?>();
    /// <summary>Dias em que alguma atividade terminou (DATAHORAFIMATIVIDADE).</summary>
    public List<DateOnly> DiasFim = new();
    /// <summary>Dias em que alguma atividade começou (DATAHORAINICIOATIVIDADE).</summary>
    public List<DateOnly> DiasInicio = new();
    public int N => U.Length;
}

public sealed record ServicoEstrategia(string Servico, long Quantidade, long QtdDia, double TmaDiaSeg);

public sealed class ResultadoEstrategia
{
    public DateOnly Dia;
    /// <summary>Linhas consideradas (fonte aplicada).</summary>
    public long Registros;
    public List<ServicoEstrategia> Servicos = new();
    /// <summary>Fluxos com início e fim no dia (os do TMA).</summary>
    public long FluxosDia;
    public double SomaSeg;
    /// <summary>TMA em segundos inteiros (corta a fração, igual ao sistema de referência).</summary>
    public long TmaSeg;
    /// <summary>Atividades que terminaram no dia.</summary>
    public long RealizadoDia;
    public Dictionary<string, long> Procedimentos = new();
    public int Usuarios;

    public int Distintos => Servicos.Count;
    public double MediaPorServico => Distintos == 0 ? 0 : Registros / (double)Distintos;
    public double Top3 => Registros == 0 ? 0 : Servicos.OrderByDescending(s => s.Quantidade).Take(3).Sum(s => s.Quantidade) * 100.0 / Registros;
    public ServicoEstrategia? MaisFrequente => Servicos.OrderByDescending(s => s.Quantidade).ThenBy(s => s.Servico, StringComparer.Ordinal).FirstOrDefault();
    public ServicoEstrategia? MenosFrequente => Servicos.OrderBy(s => s.Quantidade).ThenBy(s => s.Servico, StringComparer.Ordinal).FirstOrDefault();
}

/// <summary>Campos da mensagem do dia (o que não vem do compilado é digitado na tela).</summary>
public sealed class MensagemEstrategia
{
    public string Saudacao { get; set; } = "Bom dia pessoal! Todos";
    public string CentralizadaPaginas { get; set; } = "";
    public string CentralizadaFluxos { get; set; } = "";
    public string AutorizarPaginas { get; set; } = "";
    public string AutorizarFluxos { get; set; } = "";
    public string Texto { get; set; } = "";
    public string DiaDados { get; set; } = "";
    public string VolumeTotal { get; set; } = "";
    public string DataFluxos { get; set; } = "";
    public string VolumeComData { get; set; } = "";
    public string Entraram { get; set; } = "";
    public string Sla { get; set; } = "";
    /// <summary>Orientações gravadas que o Mr. Smart segue ao escrever a estratégia (prioridade sobre o padrão).</summary>
    public string Orientacoes { get; set; } = EstrategiaIA.OrientacoesPadrao;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MensagemEstrategia))]
internal partial class JsonEstrategia : JsonSerializerContext { }

public sealed record ModeloEstrategia(string Nome, string Dica, string Texto);

public static class Estrategia
{
    static readonly HashSet<string> Colunas = new() { "USUARIO", "IDREGISTROCONTROLADO", "PROCEDIMENTO", "DATAHORAINICIOATIVIDADE", "DATAHORAFIMATIVIDADE" };

    public static string ServicoDe(string? idRegistro)
    {
        var s = (idRegistro ?? "").Trim();
        var k = s.IndexOf('#');
        s = (k >= 0 ? s[..k] : s).Trim().ToUpperInvariant();
        return s == "" ? "(sem serviço)" : s;
    }

    static DateTime? DataHora(string? v)
        => Datas.Strptime(v, "%d/%m/%Y %H:%M:%S") ?? Datas.Strptime(v, "%d/%m/%Y %H:%M") ?? Datas.Flexivel(v, true);

    public static async Task<BaseEstrategia> Preparar(IEnumerable<Arquivo> arquivos, Progresso? prog = null)
    {
        var b = new BaseEstrategia();
        var u = new List<string>(); var sv = new List<string>(); var pr = new List<string>(); var ini = new List<DateTime?>(); var fim = new List<DateTime?>();
        var cacheU = new Dictionary<string, string>(); var cacheS = new Dictionary<string, string>(); var cacheD = new Dictionary<string, DateTime?>();
        foreach (var a in arquivos)
        {
            if (prog != null) prog.Msg = a.Nome;
            var df = await Compilados.Ler(a, Colunas, prog);
            int cu = df.Col("USUARIO"), cr = df.Col("IDREGISTROCONTROLADO"), cp = df.Col("PROCEDIMENTO"), ci = df.Col("DATAHORAINICIOATIVIDADE"), cf = df.Col("DATAHORAFIMATIVIDADE");
            if (cr < 0) throw new InvalidDataException($"{a.Nome}: não tem a coluna IDREGISTROCONTROLADO (é um compilado?).");
            if (ci < 0 || cf < 0) throw new InvalidDataException($"{a.Nome}: faltam as colunas DATAHORAINICIOATIVIDADE e DATAHORAFIMATIVIDADE.");
            for (int i = 0; i < df.Linhas.Count; i++)
            {
                var vu = df.V(i, cu) ?? "";
                if (!cacheU.TryGetValue(vu, out var nu)) cacheU[vu] = nu = Compilados.NormUsuario(vu);
                var vr = df.V(i, cr) ?? "";
                if (!cacheS.TryGetValue(vr, out var ns)) cacheS[vr] = ns = ServicoDe(vr);
                u.Add(nu); sv.Add(ns); pr.Add((df.V(i, cp) ?? "").Trim().ToUpperInvariant());
                var a1 = df.V(i, ci) ?? ""; var a2 = df.V(i, cf) ?? "";
                if (!cacheD.TryGetValue(a1, out var d1)) cacheD[a1] = d1 = DataHora(a1);
                if (!cacheD.TryGetValue(a2, out var d2)) cacheD[a2] = d2 = DataHora(a2);
                ini.Add(d1); fim.Add(d2);
            }
            b.Arquivos.Add((a.Nome, df.Linhas.Count));
        }
        b.U = u.ToArray(); b.Servico = sv.ToArray(); b.Procedimento = pr.ToArray(); b.Inicio = ini.ToArray(); b.Fim = fim.ToArray();
        b.DiasFim = b.Fim.Where(d => d != null).Select(d => DateOnly.FromDateTime(d!.Value)).Distinct().OrderBy(d => d).ToList();
        b.DiasInicio = b.Inicio.Where(d => d != null).Select(d => DateOnly.FromDateTime(d!.Value)).Distinct().OrderBy(d => d).ToList();
        if (b.N == 0) throw new InvalidDataException("O compilado não tem linhas.");
        return b;
    }

    /// <summary>Dia padrão: o dia em que mais atividades terminaram.</summary>
    public static DateOnly DiaPadrao(BaseEstrategia b)
        => b.Fim.Where(d => d != null).GroupBy(d => DateOnly.FromDateTime(d!.Value)).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).Select(g => g.Key).FirstOrDefault(DateOnly.FromDateTime(DateTime.Today));

    static bool Na(HashSet<string>? lista, string u) => lista == null || lista.Contains(u);

    /// <summary>
    /// Serviços: todas as linhas (com a fonte aplicada). TMA: só linhas com início e fim no dia; média de (fim − início)
    /// em segundos, cortando a fração (01:53:22 no compilado de 10/10).
    /// </summary>
    public static ResultadoEstrategia Calcular(BaseEstrategia b, DateOnly dia, HashSet<string>? lista)
    {
        var r = new ResultadoEstrategia { Dia = dia };
        var qtd = new Dictionary<string, long>(); var qd = new Dictionary<string, long>(); var sd = new Dictionary<string, double>();
        var us = new HashSet<string>();
        for (int i = 0; i < b.N; i++)
        {
            if (!Na(lista, b.U[i])) continue;
            r.Registros++;
            us.Add(b.U[i]);
            var s = b.Servico[i];
            qtd[s] = qtd.GetValueOrDefault(s) + 1;
            var p = b.Procedimento[i];
            if (p != "") r.Procedimentos[p] = r.Procedimentos.GetValueOrDefault(p) + 1;
            if (b.Fim[i] is DateTime f && DateOnly.FromDateTime(f) == dia)
            {
                r.RealizadoDia++;
                if (b.Inicio[i] is DateTime a && DateOnly.FromDateTime(a) == dia)
                {
                    var seg = (f - a).TotalSeconds;
                    r.FluxosDia++; r.SomaSeg += seg;
                    qd[s] = qd.GetValueOrDefault(s) + 1; sd[s] = sd.GetValueOrDefault(s) + seg;
                }
            }
        }
        r.Usuarios = us.Count;
        r.TmaSeg = r.FluxosDia == 0 ? 0 : (long)Math.Floor(r.SomaSeg / r.FluxosDia);
        r.Servicos = qtd.Select(kv => new ServicoEstrategia(kv.Key, kv.Value, qd.GetValueOrDefault(kv.Key),
                qd.GetValueOrDefault(kv.Key) == 0 ? 0 : Math.Floor(sd[kv.Key] / qd[kv.Key])))
            .OrderByDescending(x => x.Quantidade).ThenBy(x => x.Servico, StringComparer.Ordinal).ToList();
        return r;
    }

    /// <summary>Atividades que terminaram no dia e começaram na data indicada (“fluxos com data de x”).</summary>
    public static long RealizadoComData(BaseEstrategia b, DateOnly dia, DateOnly dataFluxos, HashSet<string>? lista)
    {
        long n = 0;
        for (int i = 0; i < b.N; i++)
            if (Na(lista, b.U[i]) && b.Fim[i] is DateTime f && DateOnly.FromDateTime(f) == dia && b.Inicio[i] is DateTime a && DateOnly.FromDateTime(a) == dataFluxos) n++;
        return n;
    }

    public static string Hms(double seg)
    {
        var s = (long)Math.Max(0, Math.Floor(seg));
        return $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
    }

    static string Dec(double v) => v.ToString("0.#", CultureInfo.InvariantCulture).Replace(".", ",");
    public static string Num(long n) => n.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", ".");

    // ================= modelos de texto da estratégia =================

    public static List<ModeloEstrategia> Modelos(ResultadoEstrategia? r, MensagemEstrategia m)
    {
        string Ou(string v, string padrao) => string.IsNullOrWhiteSpace(v) ? padrao : v.Trim();
        var top = r?.MaisFrequente;
        var cen = Ou(m.CentralizadaPaginas, "x"); var aut = Ou(m.AutorizarPaginas, "x");
        long dev = r?.Procedimentos.GetValueOrDefault("DEVOLVER PARA CORREÇÃO") ?? 0;
        double pctDev = r == null || r.Registros == 0 ? 0 : dev * 100.0 / r.Registros;
        var sla = r != null && r.FluxosDia > 0 ? Hms(r.TmaSeg) : Ou(m.Sla, "x");
        var vol = r != null ? Num(r.RealizadoDia) : "x";
        var dDados = r != null ? r.Dia.ToString("dd/MM", CultureInfo.InvariantCulture) : "x";
        var lista = new List<ModeloEstrategia>
        {
            new("Mais antigos primeiro", "Fila por ordem de entrada",
                "Hoje a prioridade são os fluxos mais antigos: vamos trabalhar a fila por ordem de entrada, começando pela Centralizada, para não deixar nada envelhecer e manter o SLA sob controle. Zerando os mais antigos, seguimos para a Autorizar."),
            new("Foco na Centralizada", "Mais gente na Centralizada",
                $"A Centralizada começou o dia com {cen} páginas, então a maior parte do time fica nela no início do dia. Quem terminar a sua parte ou ficar sem fluxo, avisa aqui para redistribuirmos."),
            new("Foco na Autorizar", "Reforço na Autorizar",
                $"A Autorizar está com {aut} páginas, então vamos reforçar essa fila logo cedo, sem deixar a Centralizada parada. Ao longo do dia reavaliamos a divisão conforme as filas andarem."),
            new("Dividir o time", "Metade em cada fila",
                $"Hoje vamos dividir o time entre as duas filas: Centralizada ({cen} páginas) e Autorizar ({aut} páginas). No meio do dia revemos a divisão conforme o volume que sobrar em cada uma."),
            new("Manter o ritmo", "Meta com base no dia anterior",
                $"No dia {dDados} realizamos {vol} fluxos, com SLA de {sla}. Vamos manter esse ritmo hoje, com atenção à qualidade: em caso de dúvida, chamem antes de concluir."),
        };
        if (top != null && r!.Registros > 0)
            lista.Add(new("Atenção ao serviço que mais entra", $"{top.Servico} = {Dec(top.Quantidade * 100.0 / r.Registros)}%",
                $"{top.Servico} representou {Dec(top.Quantidade * 100.0 / r.Registros)}% do que foi feito no dia {dDados}. Vamos manter atenção redobrada nesse tipo de registro e conferir bem a documentação antes de aprovar ou devolver."));
        if (dev > 0)
            lista.Add(new("Reduzir devoluções", $"{Num(dev)} devoluções ({Dec(pctDev)}%)",
                $"No dia {dDados} tivemos {Num(dev)} devoluções para correção ({Dec(pctDev)}% do volume). Antes de devolver, confiram se a informação não está em outro anexo ou registro do fluxo e usem o parecer padrão."));
        return lista;
    }

    public static string Gravar(MensagemEstrategia m) => JsonSerializer.Serialize(m, JsonEstrategia.Default.MensagemEstrategia);
    public static MensagemEstrategia Ler(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? new() : JsonSerializer.Deserialize(json, JsonEstrategia.Default.MensagemEstrategia) ?? new(); }
        catch { return new(); }
    }

    public static string Mensagem(MensagemEstrategia m)
    {
        string X(string v) => string.IsNullOrWhiteSpace(v) ? "x" : v.Trim();
        var sb = new StringBuilder();
        sb.AppendLine(X(m.Saudacao));
        sb.AppendLine();
        sb.AppendLine($"Iniciamos o dia com {X(m.CentralizadaPaginas)} páginas na Centralizada, totalizando {X(m.CentralizadaFluxos)} fluxos, e {X(m.AutorizarPaginas)} páginas na Autorizar, totalizando {X(m.AutorizarFluxos)} fluxos.");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(m.Texto)) { sb.AppendLine(m.Texto.Trim()); sb.AppendLine(); }
        sb.AppendLine();
        sb.AppendLine($"Segue os dados do dia {X(m.DiaDados)}:");
        sb.AppendLine($"📊 Volume total realizado: {X(m.VolumeTotal)} fluxos");
        sb.AppendLine($"📅 Volume realizado de fluxos com data de {X(m.DataFluxos)}: {X(m.VolumeComData)} fluxos");
        sb.AppendLine($"📥 Total de fluxos que entraram no dia {X(m.DataFluxos)}: {X(m.Entraram)} fluxos");
        sb.Append($"⏳SLA de atendimento a partir da entrada do fluxo: {X(m.Sla)}");
        return sb.ToString();
    }
}
