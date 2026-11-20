namespace QSmart.Core.Conta;

/// <summary>Medalha: ganha uma vez, quando a meta é batida.</summary>
public sealed record Medalha(string Id, string Nome, string Como, string Icone, string Cor, int Valor, int Meta)
{
    public bool Ganha => Valor >= Meta;
    public double Progresso => Meta <= 0 ? 1 : Math.Min(1, (double)Valor / Meta);
}

/// <summary>Troféu com níveis (bronze, prata, ouro).</summary>
public sealed record Trofeu(string Id, string Nome, string Como, string Icone, int Valor, int[] Metas)
{
    /// <summary>0 = ainda não ganhou; 1 bronze; 2 prata; 3 ouro.</summary>
    public int Grau => Metas.Count(m => Valor >= m);
    public string GrauNome => Grau switch { 3 => "OURO", 2 => "PRATA", 1 => "BRONZE", _ => "BLOQUEADO" };
    public int? ProximaMeta => Grau < Metas.Length ? Metas[Grau] : null;
    public string Cor => Grau switch { 3 => "#F59E0B", 2 => "#94A3B8", 1 => "#CD7F32", _ => "#334155" };
}

public sealed class ResumoConta
{
    public int Xp, Nivel, XpNivel, XpProximo, Dias, Seguidos, MaiorSequencia, Ferramentas;
    public string Titulo = "";
    public List<Medalha> Medalhas = new();
    public List<Trofeu> Trofeus = new();
    public int MedalhasGanhas => Medalhas.Count(m => m.Ganha);
    public int TrofeusGanhos => Trofeus.Count(t => t.Grau > 0);
    public double ProgressoNivel => XpProximo <= XpNivel ? 1 : (double)(Xp - XpNivel) / (XpProximo - XpNivel);
}

/// <summary>Níveis, medalhas e troféus a partir do uso registrado no banco.</summary>
public static class Conquistas
{
    /// <summary>Eventos de ação (além de abrir as abas).</summary>
    public const string RadarConsulta = "radar.consulta", RadarCpf = "radar.cpf", OoRegistro = "oo.registro",
        EstrategiaIA = "estrategia.ia", S3069Analise = "s3069.analise";

    static readonly Dictionary<string, int> Pesos = new()
    {
        [RadarConsulta] = 6, [RadarCpf] = 6, [OoRegistro] = 10, [EstrategiaIA] = 8, [S3069Analise] = 8,
    };

    /// <summary>XP mínimo de cada nível: 0, 100, 300, 600, 1000…</summary>
    public static int XpDoNivel(int nivel) => 50 * (nivel - 1) * nivel;

    public static string TituloDo(int nivel) => nivel switch
    {
        <= 2 => "Recruta", <= 4 => "Explorador", <= 7 => "Analista", <= 10 => "Analista Sênior",
        <= 14 => "Analista Estelar", <= 19 => "Especialista", <= 29 => "Mestre QSmart", _ => "Lenda da Insi",
    };

    /// <summary>Dias úteis seguidos até hoje (fim de semana não quebra a sequência).</summary>
    public static (int atual, int maior) Sequencias(IEnumerable<DateOnly> dias, DateOnly hoje)
    {
        var set = dias.ToHashSet();
        static bool Util(DateOnly d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        int atual = 0;
        var d = hoje;
        if (!set.Contains(d)) d = d.AddDays(-1);          // hoje ainda não usou: conta até ontem
        for (int i = 0; i < 800; i++, d = d.AddDays(-1))
        {
            if (set.Contains(d)) { atual++; continue; }
            if (!Util(d)) continue;
            break;
        }
        int maior = 0, corrente = 0;
        DateOnly? ant = null;
        foreach (var x in set.OrderBy(x => x))
        {
            if (ant is { } a)
            {
                bool seguido = true;
                for (var k = a.AddDays(1); k < x; k = k.AddDays(1)) if (Util(k)) { seguido = false; break; }
                corrente = seguido ? corrente + 1 : 1;
            }
            else corrente = 1;
            maior = Math.Max(maior, corrente);
            ant = x;
        }
        return (atual, Math.Max(maior, atual));
    }

    /// <param name="uso">chave → quantidade (abas abertas e ações).</param>
    /// <param name="abas">chaves das ferramentas que contam para “Explorador”.</param>
    public static ResumoConta Calcular(IReadOnlyDictionary<string, int> uso, IEnumerable<DateOnly> dias, IReadOnlyCollection<string> abas, DateOnly hoje)
    {
        int U(string k) => uso.TryGetValue(k, out var v) ? v : 0;
        var listaDias = dias.Distinct().ToList();
        var (seg, maior) = Sequencias(listaDias, hoje);
        int ferramentas = abas.Count(a => U(a) > 0);
        var r = new ResumoConta { Dias = listaDias.Count, Seguidos = seg, MaiorSequencia = maior, Ferramentas = ferramentas };

        r.Medalhas = new()
        {
            new("passos", "Primeiros passos", "Entrar no QSmart pela primeira vez", "check", "#10B981", r.Dias, 1),
            new("semana", "Uma semana", "Usar o QSmart em 5 dias", "calendario", "#0EA5E9", r.Dias, 5),
            new("mes", "De casa", "Usar o QSmart em 20 dias", "inicio", "#6366F1", r.Dias, 20),
            new("veterano", "Veterano", "Usar o QSmart em 100 dias", "selo", "#F59E0B", r.Dias, 100),
            new("chamas", "Em chamas", "5 dias úteis seguidos", "fogo", "#EF4444", maior, 5),
            new("imparavel", "Imparável", "20 dias úteis seguidos", "fogo", "#DC2626", maior, 20),
            new("curioso", "Curioso", "Abrir 5 ferramentas diferentes", "busca", "#14B8A6", ferramentas, 5),
            new("explorador", "Explorador", $"Usar todas as {abas.Count} ferramentas", "estrela", "#8B5CF6", ferramentas, Math.Max(1, abas.Count)),
            new("detetive", "Detetive", "10 consultas no Radar Smart360", "radar", "#EC4899", U(RadarConsulta) + U(RadarCpf), 10),
            new("investigador", "Investigador", "5 consultas de CPF no Radar", "pessoa", "#DB2777", U(RadarCpf), 5),
            new("escuta", "Escuta ativa", "Registrar 10 conversas de 1:1", "conversa", "#14B8A6", U(OoRegistro), 10),
            new("guardiao", "Guardião do 1:1", "Registrar 30 conversas de 1:1", "conversa", "#0D9488", U(OoRegistro), 30),
            new("parceiro", "Parceiro do Mr. Smart", "Pedir 3 estratégias ao Mr. Smart", "faisca", "#F59E0B", U(EstrategiaIA), 3),
            new("impecavel", "Resposta impecável", "Analisar 5 respostas no Serviços 3069", "selo", "#C026D3", U(S3069Analise), 5),
            new("bomdia", "Bom dia, time", "Abrir a Estratégia do dia 10 vezes", "estrategia", "#F97316", U("estrategia"), 10),
            new("decisor", "Decisor", "Abrir o Resumo 10 vezes", "resumo", "#10B981", U("resumo"), 10),
            new("olho", "Olho clínico", "Abrir a Inspeção 10 vezes", "inspecao", "#3B82F6", U("inspecao"), 10),
            new("revisor", "Revisor", "Conferir pareceres 10 vezes", "pareceres", "#8B5CF6", U("pareceres"), 10),
            new("historiador", "Historiador", "Abrir o Histórico 5 vezes", "historico", "#6366F1", U("historico"), 5),
            new("ritmo", "No ritmo", "Abrir o Volume 10 vezes", "volume", "#F97316", U("volume"), 10),
            new("vigia", "Vigia", "Abrir o Monitor 10 vezes", "monitor", "#EAB308", U("monitor"), 10),
            new("organizador", "Organizador", "Usar o Corredor 5 vezes", "corredor", "#D97706", U("corredor"), 5),
            new("planejador", "Planejador", "Abrir as Férias 5 vezes", "ferias", "#0EA5E9", U("ferias"), 5),
            new("pontual", "Pontual", "Abrir os Pontos de ação 5 vezes", "pontos", "#0D9488", U("pontos"), 5),
        };

        r.Trofeus = new()
        {
            new("radar", "Mestre do Radar", "consultas no Radar Smart360", "radar", U(RadarConsulta) + U(RadarCpf), new[] { 10, 50, 150 }),
            new("oo", "Guardião do 1:1", "conversas de 1:1 registradas", "conversa", U(OoRegistro), new[] { 10, 30, 80 }),
            new("estrategia", "Estrategista", "estratégias escritas com o Mr. Smart", "estrategia", U(EstrategiaIA), new[] { 5, 20, 60 }),
            new("presenca", "Presença", "dias usando o QSmart", "calendario", r.Dias, new[] { 30, 100, 250 }),
            new("constancia", "Constância", "dias úteis seguidos (recorde)", "fogo", maior, new[] { 10, 30, 60 }),
        };

        int xp = r.Dias * 25;
        foreach (var (k, v) in uso) xp += v * (Pesos.TryGetValue(k, out var p) ? p : 2);
        xp += r.MedalhasGanhas * 50 + r.Trofeus.Sum(t => t.Grau) * 100;
        r.Xp = xp;
        int n = 1;
        while (XpDoNivel(n + 1) <= xp && n < 99) n++;
        r.Nivel = n; r.XpNivel = XpDoNivel(n); r.XpProximo = XpDoNivel(n + 1); r.Titulo = TituloDo(n);
        return r;
    }
}
