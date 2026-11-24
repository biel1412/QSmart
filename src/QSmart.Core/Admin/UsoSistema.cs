using System.Globalization;
using QSmart.Core.Planilhas;

namespace QSmart.Core.Admin;

/// <summary>Limites do plano (vêm do servidor, para mudar num lugar só).</summary>
public sealed record LimitesUso(string Plano, long ReqDia, long LerDia, long GravarDia, long BancoBytes, long ContaBancoBytes,
    long R2Bytes, long R2AMes, long R2BMes, long IaReqDia, long IaTokDia, long ArquivoMaxBytes);

public sealed record TabelaUso(string Nome, string Modulo, long Linhas, long Bytes);
public sealed record PastaUso(string Pasta, long Objetos, long Bytes);
public sealed record ArquivoUso(string Nome, string Titulo, long Bytes, string Em);
public sealed record PapelUso(string Papel, string Status, long N);
public sealed record FerramentaUso(string Chave, long Usos, long Pessoas, string Ultimo);

/// <summary>Um dia da medição: chaves "req", "rd", "wr", "ia", "iatok", "r2a", "r2b", "err", "rota:xxx", "banco_bytes"...</summary>
public sealed record DiaUso(DateOnly Dia, Dictionary<string, long> V)
{
    public long this[string k] => V.TryGetValue(k, out var x) ? x : 0;
}

public enum NivelUso { Ok, Info, Atencao, Critico }

public sealed record AvisoUso(NivelUso Nivel, string Titulo, string Texto, string Acao);

/// <summary>Uma linha do quadro "limites": o que foi usado, o limite, a porcentagem e a previsão.</summary>
public sealed record MedidorUso(string Chave, string Nome, string Grupo, long Usado, long Limite, string Periodo, bool Bytes, double? Previsto = null)
{
    public double Pct => Limite <= 0 ? 0 : Usado * 100.0 / Limite;
    public NivelUso Nivel => Pct >= 90 ? NivelUso.Critico : Pct >= 70 ? NivelUso.Atencao : NivelUso.Ok;
    public string UsadoTxt => Bytes ? UsoSistema.Tam(Usado) : Texto.Fmt(Usado);
    public string LimiteTxt => Bytes ? UsoSistema.Tam(Limite) : Texto.Fmt(Limite);
    public string Resta => Bytes ? UsoSistema.Tam(Math.Max(0, Limite - Usado)) : Texto.Fmt(Math.Max(0, Limite - Usado));
}

/// <summary>
/// Painel de uso do administrador: armazenamento (banco e arquivos), consumo diário e mensal comparado aos limites
/// do plano grátis, avisos de atenção, projeções e exportação (planilha). O PDF fica em Pdf/Relatorios.Uso.
/// </summary>
public sealed class UsoSistema
{
    public DateTime GeradoEm;                 // UTC
    public DateOnly DiaUtc;
    public LimitesUso Lim = null!;
    public long BancoBytes;
    public int Indices;
    public List<TabelaUso> Tabelas = new();
    public bool R2;
    public long R2Bytes, R2Objetos;
    public List<PastaUso> Pastas = new();
    public List<ArquivoUso> Maiores = new();
    public List<PapelUso> Papeis = new();
    public long AtivosHoje, Ativos7, Ativos30, Sessoes;
    public List<FerramentaUso> Ferramentas = new();
    public List<DiaUso> Historico = new();
    public DateOnly MedindoDesde;

    // ------------------------------------------------------------------ números de hoje e do mês
    public DiaUso Hoje => Historico.FirstOrDefault(d => d.Dia == DiaUtc) ?? new DiaUso(DiaUtc, new());
    public long Mes(string k) => Historico.Where(d => d.Dia.Year == DiaUtc.Year && d.Dia.Month == DiaUtc.Month).Sum(d => d[k]);
    public int DiasMedidos => Historico.Count;
    public long UsuariosTotal => Papeis.Sum(p => p.N);
    public long Pendentes => Papeis.Where(p => p.Status == "pendente").Sum(p => p.N);
    public long LinhasBanco => Tabelas.Sum(t => t.Linhas);

    /// <summary>Quanto do dia (UTC, que é como a Cloudflare conta) já passou: 0 a 1.</summary>
    public double FracaoDia => Math.Clamp((GeradoEm - GeradoEm.Date).TotalHours / 24.0, 0.02, 1);

    /// <summary>Limite diário de IA informado pela Groq (cabeçalhos); sem ele, o valor de referência.</summary>
    public long IaLimite => Hoje["ia_lim_req"] > 0 ? Hoje["ia_lim_req"] : Lim.IaReqDia;
    public long? IaRestaGroq => Hoje.V.ContainsKey("ia_rest_req") ? Hoje["ia_rest_req"] : null;

    public List<MedidorUso> Medidores()
    {
        double Prev(long hoje) => hoje / FracaoDia;
        var iaUsada = IaRestaGroq is long rest ? Math.Max(Hoje["ia"], IaLimite - rest) : Hoje["ia"];
        var l = new List<MedidorUso>
        {
            new("banco", "Banco de dados (D1)", "Armazenamento", BancoBytes, Lim.BancoBytes, "total", true),
            new("r2", "Arquivos (R2)", "Armazenamento", R2Bytes, Lim.R2Bytes, "total", true),
            new("req", "Pedidos ao servidor", "Hoje", Hoje["req"], Lim.ReqDia, "por dia", false, Prev(Hoje["req"])),
            new("rd", "Linhas lidas do banco", "Hoje", Hoje["rd"], Lim.LerDia, "por dia", false, Prev(Hoje["rd"])),
            new("wr", "Linhas gravadas no banco", "Hoje", Hoje["wr"], Lim.GravarDia, "por dia", false, Prev(Hoje["wr"])),
            new("ia", "Perguntas à IA", "Hoje", iaUsada, IaLimite, "por dia", false, Prev(iaUsada)),
            new("iatok", "Tokens da IA", "Hoje", Hoje["iatok"], Lim.IaTokDia, "por dia", false, Prev(Hoje["iatok"])),
            new("r2a", "Envios/listagens de arquivos (classe A)", "No mês", Mes("r2a"), Lim.R2AMes, "por mês", false),
            new("r2b", "Downloads de arquivos (classe B)", "No mês", Mes("r2b"), Lim.R2BMes, "por mês", false),
        };
        return l;
    }

    // ------------------------------------------------------------------ módulos e crescimento
    public List<(string modulo, long bytes, long linhas)> Modulos() =>
        Tabelas.GroupBy(t => t.Modulo).Select(g => (g.Key, g.Sum(t => t.Bytes), g.Sum(t => t.Linhas))).OrderByDescending(x => x.Item2).ToList();

    /// <summary>Crescimento médio por dia (bytes) usando o histórico de tamanho; nulo se ainda não há dias suficientes.</summary>
    public double? CrescimentoDia(string chave)
    {
        var pts = Historico.Where(d => d[chave] > 0).OrderBy(d => d.Dia).ToList();
        if (pts.Count < 2) return null;
        var a = pts[0]; var b = pts[^1];
        var dias = b.Dia.DayNumber - a.Dia.DayNumber;
        if (dias <= 0) return null;
        return (b[chave] - a[chave]) / (double)dias;
    }

    public static int? DiasAteEncher(long atual, long limite, double? porDia) =>
        porDia is double p && p > 0 ? (int)Math.Ceiling((limite - atual) / p) : null;

    public double MediaDiaria(string k, int dias = 7)
    {
        var ult = Historico.Where(d => d.Dia < DiaUtc).OrderByDescending(d => d.Dia).Take(dias).ToList();
        return ult.Count == 0 ? 0 : ult.Average(d => (double)d[k]);
    }

    public List<(string rota, long n)> Rotas(DiaUso? d = null)
    {
        d ??= Hoje;
        return d.V.Where(kv => kv.Key.StartsWith("rota:")).GroupBy(kv => NomeRota(kv.Key[5..]))
            .Select(g => (g.Key, g.Sum(kv => kv.Value))).OrderByDescending(x => x.Item2).ToList();
    }

    public static string NomeRota(string r) => r switch
    {
        "chat" => "Chat", "oo" => "One a One", "fe" or "ferias" => "Férias", "dayoff" => "Day off", "admin" => "Administração",
        "sn" => "Smart Note", "feed" => "Feed", "notificacoes" or "notif" => "Notificações", "downloads" => "Downloads",
        "ia" => "IA (Groq)", "portal" => "Portal da Transparência", "entrar" or "sair" or "sessao" or "perfil" or "cadastro" or "eu" or "senha" => "Conta e login",
        "uso" => "Registro de uso", "lid" => "Minha equipe", _ => char.ToUpper(r[0]) + r[1..],
    };

    // ------------------------------------------------------------------ avisos
    public List<AvisoUso> Avisos()
    {
        var av = new List<AvisoUso>();
        string P(double x) => Texto.Fv(x, 1) + "%";
        foreach (var m in Medidores())
        {
            if (m.Nivel == NivelUso.Critico)
                av.Add(new(NivelUso.Critico, $"{m.Nome}: {P(m.Pct)} do limite", $"Usado {m.UsadoTxt} de {m.LimiteTxt} ({m.Periodo}).", AcaoDe(m.Chave)));
            else if (m.Nivel == NivelUso.Atencao)
                av.Add(new(NivelUso.Atencao, $"{m.Nome}: {P(m.Pct)} do limite", $"Usado {m.UsadoTxt} de {m.LimiteTxt} ({m.Periodo}).", AcaoDe(m.Chave)));
            else if (m.Previsto is double pv && m.Limite > 0 && pv / m.Limite >= 0.9 && FracaoDia < 0.9 && m.Usado > 50)
                av.Add(new(NivelUso.Atencao, $"{m.Nome}: no ritmo de hoje, chega a {P(pv * 100 / m.Limite)}",
                    $"Até agora {m.UsadoTxt}. Se continuar assim, o dia fecha perto de {(m.Bytes ? Tam((long)pv) : Texto.Fmt((long)pv))} (limite {m.LimiteTxt}).", AcaoDe(m.Chave)));
        }
        foreach (var (chave, nome, atual, lim) in new[] { ("banco_bytes", "O banco de dados", BancoBytes, Lim.BancoBytes), ("r2_bytes", "Os arquivos (R2)", R2Bytes, Lim.R2Bytes) })
        {
            var dias = DiasAteEncher(atual, lim, CrescimentoDia(chave));
            if (dias is int d && d <= 30) av.Add(new(NivelUso.Critico, $"{nome} enche em cerca de {d} dias", "Projeção pelo crescimento médio dos últimos dias.", AcaoDe(chave.StartsWith("banco") ? "banco" : "r2")));
            else if (dias is int d2 && d2 <= 120) av.Add(new(NivelUso.Atencao, $"{nome} enche em cerca de {d2} dias", "Projeção pelo crescimento médio dos últimos dias.", AcaoDe(chave.StartsWith("banco") ? "banco" : "r2")));
        }
        if (!R2) av.Add(new(NivelUso.Atencao, "Armazenamento de arquivos (R2) desligado", "Sem o R2, os arquivos do Downloads não podem ser enviados nem baixados.", "Na Cloudflare, ligue o bucket ao Worker com o nome ARQUIVOS."));
        if (Hoje["ia429"] > 0) av.Add(new(NivelUso.Atencao, $"A IA recusou {Texto.Fmt(Hoje["ia429"])} pedido(s) hoje por limite", "A Groq respondeu \"limite atingido\" (429). O Mr. Smart IA fica indisponível por alguns minutos ou até o dia virar.", "Se acontecer sempre, considere o plano pago da Groq ou outro modelo."));
        if (Hoje["req"] >= 50 && Hoje["err"] * 100.0 / Hoje["req"] >= 5) av.Add(new(NivelUso.Atencao, $"{P(Hoje["err"] * 100.0 / Hoje["req"])} dos pedidos de hoje deram erro no servidor", $"{Texto.Fmt(Hoje["err"])} erros em {Texto.Fmt(Hoje["req"])} pedidos.", "Veja os registros (Logs) do Worker na Cloudflare."));
        var maior = Tabelas.FirstOrDefault();
        if (maior != null && BancoBytes > 0 && maior.Bytes * 100.0 / Math.Max(1, Tabelas.Sum(t => t.Bytes)) >= 50 && maior.Bytes > 1024 * 1024)
            av.Add(new(NivelUso.Info, $"{maior.Modulo} ocupa a maior parte do banco", $"A tabela {maior.Nome} tem {Tam(maior.Bytes)} ({Texto.Fmt(maior.Linhas)} linhas).", "Se crescer demais, vale limpar registros antigos desse módulo."));
        var grande = Maiores.FirstOrDefault(a => a.Bytes >= Lim.ArquivoMaxBytes / 2);
        if (grande != null) av.Add(new(NivelUso.Info, "Arquivo grande no Downloads", $"{(grande.Titulo != "" ? grande.Titulo : grande.Nome)} tem {Tam(grande.Bytes)}.", "Arquivos compactados (.zip) economizam espaço e tempo de download."));
        if (Pendentes > 0) av.Add(new(NivelUso.Info, $"{Texto.Fmt(Pendentes)} conta(s) aguardando aprovação", "Pessoas que se cadastraram e ainda não podem entrar.", "Aprove ou recuse em Perfil › Equipe e acessos."));
        if (DiasMedidos < 7) av.Add(new(NivelUso.Info, "A medição começou há pouco tempo", $"Os números diários são contados pelo próprio QSmart desde {MedindoDesde:dd/MM/yyyy}. Projeções ficam mais confiáveis depois de uma semana.", "O número oficial está no painel da Cloudflare (Workers e D1 › Métricas)."));
        if (!av.Any(a => a.Nivel is NivelUso.Atencao or NivelUso.Critico))
            av.Insert(0, new(NivelUso.Ok, "Tudo dentro dos limites", "Nenhum limite do plano grátis passou de 70% e não há projeção de estouro.", "Nada a fazer agora."));
        int Ordem(NivelUso n) => n switch { NivelUso.Critico => 0, NivelUso.Atencao => 1, NivelUso.Ok => 2, _ => 3 };
        return av.OrderBy(a => Ordem(a.Nivel)).ToList();
    }

    /// <summary>Crítico, Atenção ou OK (avisos só informativos não mudam a situação geral).</summary>
    public NivelUso NivelGeral
    {
        get
        {
            var n = Avisos().Select(a => a.Nivel).DefaultIfEmpty(NivelUso.Ok).Max();
            return n == NivelUso.Info ? NivelUso.Ok : n;
        }
    }

    static string AcaoDe(string chave) => chave switch
    {
        "banco" => "Limpe dados antigos (notificações, histórico) ou passe para o Workers Paid (US$ 5/mês, banco de até 10 GB).",
        "r2" => "Apague downloads antigos ou que ninguém baixa. Acima de 10 GB a Cloudflare cobra US$ 0,015 por GB/mês.",
        "req" => "Os pedidos zeram às 21h (Brasília). Muitos pedidos costumam vir do chat aberto: o intervalo de atualização pode ser aumentado.",
        "rd" => "Leituras zeram às 21h (Brasília). Telas que leem tabelas inteiras (One a One, Férias) são as que mais consomem.",
        "wr" => "Gravações zeram às 21h (Brasília). Importações grandes de planilha gravam muitas linhas de uma vez.",
        "ia" or "iatok" => "O limite da IA zera a cada dia. Respostas mais curtas ou um modelo menor gastam menos tokens.",
        "r2a" or "r2b" => "Operações de arquivos zeram no início de cada mês.",
        _ => "",
    };

    // ------------------------------------------------------------------ formatos
    public static string Tam(long b)
    {
        string N(double x, int c) => x.ToString("N" + c, Pacote.FormatoPt.Pt);
        return b >= 1L << 30 ? N(b / (double)(1L << 30), 2) + " GB"
            : b >= 1L << 20 ? N(b / (double)(1L << 20), 1) + " MB"
            : b >= 1024 ? N(b / 1024.0, 0) + " KB" : b + " B";
    }

    public static string NomeNivel(NivelUso n) => n switch { NivelUso.Critico => "Crítico", NivelUso.Atencao => "Atenção", NivelUso.Info => "Informação", _ => "OK" };

    public static string NomePapel(string p) => p switch { "admin" => "Administrador", "coordenador" => "Coordenação", "lideranca" => "Liderança", "usuario" => "Usuário", _ => p == "" ? "(sem papel)" : p };

    public static string NomeFerramenta(string chave)
    {
        var c = chave.Split('.')[0];
        return c switch
        {
            "downloads" => "Downloads", "resumo" => "Resumo", "evolucao" => "Evolução", "inspecao" => "Inspeção", "pareceres" => "Pareceres",
            "volume" => "Volume", "monitor" => "Monitor", "corredor" => "Corredor", "estrategia" => "Estratégia", "oo" or "oneaone" => "One a One",
            "ferias" => "Férias", "dayoff" => "Day off", "sn" or "smartnote" => "Smart Note", "mr" or "mrsmart" => "Mr. Smart", _ => char.ToUpper(c[0]) + c[1..],
        } + (chave.Contains('.') ? " · " + chave[(chave.IndexOf('.') + 1)..] : "");
    }

    static double Mb(long b) => Math.Round(b / 1024.0 / 1024.0, 3);

    // ------------------------------------------------------------------ planilha
    public byte[] Planilha()
    {
        var wb = new XlsxWriter();
        var brt = GeradoEm.AddHours(-3);

        var s = wb.Planilha("Resumo");
        s.Linha("Relatório de uso e armazenamento — QSmart");
        s.NegritoLinha(0);
        s.Linha($"Gerado em {brt:dd/MM/yyyy HH:mm} (Brasília) · {Lim.Plano}");
        s.Linha("Limites diários zeram às 21h de Brasília (meia-noite UTC). Números diários medidos pelo próprio QSmart; o oficial fica no painel da Cloudflare.");
        s.Linha();
        s.Cabecalho("Item", "Grupo", "Usado", "Limite", "Período", "% usado", "Resta", "Previsão para o fim do dia", "Situação");
        s.NegritoLinha(4);
        int ini = s.N;
        foreach (var m in Medidores())
            s.Linha(m.Nome, m.Grupo, m.UsadoTxt, m.LimiteTxt, m.Periodo, Math.Round(m.Pct, 2), m.Resta,
                m.Previsto is double p ? (m.Bytes ? Tam((long)p) : Texto.Fmt((long)p)) : "", NomeNivel(m.Nivel));
        s.CondicionalCor($"F{ini + 1}:F{s.N}", $"F{ini + 1}>=90", "FECACA", true);
        s.CondicionalCor($"F{ini + 1}:F{s.N}", $"AND(F{ini + 1}>=70,F{ini + 1}<90)", "FDE68A");
        s.Linha();
        s.Linha("Pessoas"); s.NegritoLinha(s.N - 1);
        s.Linha("Contas cadastradas", UsuariosTotal); s.Linha("Aguardando aprovação", Pendentes);
        s.Linha("Ativas hoje", AtivosHoje); s.Linha("Ativas nos últimos 7 dias", Ativos7); s.Linha("Ativas nos últimos 30 dias", Ativos30); s.Linha("Sessões abertas", Sessoes);
        foreach (var (c, w) in new[] { (0, 40.0), (1, 16.0), (2, 14.0), (3, 14.0), (4, 10.0), (5, 10.0), (6, 14.0), (7, 24.0), (8, 12.0) }) s.Largura(c, w);

        var a = wb.Planilha("Avisos");
        a.Cabecalho("Nível", "Aviso", "Detalhe", "O que fazer");
        a.NegritoLinha(0);
        var avs = Avisos();
        foreach (var x in avs) a.Linha(NomeNivel(x.Nivel), x.Titulo, x.Texto, x.Acao);
        for (int i = 0; i < avs.Count; i++)
            if (avs[i].Nivel != NivelUso.Ok) a.Preencher(i + 1, 0, avs[i].Nivel == NivelUso.Critico ? "FECACA" : avs[i].Nivel == NivelUso.Atencao ? "FDE68A" : "DBEAFE", true);
        foreach (var (c, w) in new[] { (0, 12.0), (1, 46.0), (2, 70.0), (3, 70.0) }) a.Largura(c, w);

        var b = wb.Planilha("Banco de dados");
        b.Cabecalho("Tabela", "Módulo", "Linhas", "Tamanho dos dados (MB)", "% dos dados");
        b.NegritoLinha(0);
        long somaT = Math.Max(1, Tabelas.Sum(t => t.Bytes));
        foreach (var t in Tabelas) b.Linha(t.Nome, t.Modulo, t.Linhas, Mb(t.Bytes), Math.Round(t.Bytes * 100.0 / somaT, 2));
        b.Linha();
        b.Linha("Total dos dados", "", LinhasBanco, Mb(Tabelas.Sum(t => t.Bytes)), 100);
        b.Linha("Tamanho real do banco (com índices e estrutura)", "", null, Mb(BancoBytes), null);
        b.Linha("Índices", "", Indices, null, null);
        b.NegritoLinha(b.N - 3);
        b.Congelar(1, 1);
        foreach (var (c, w) in new[] { (0, 46.0), (1, 24.0), (2, 12.0), (3, 22.0), (4, 12.0) }) b.Largura(c, w);

        var mo = wb.Planilha("Por módulo");
        mo.Cabecalho("Módulo", "Linhas", "Tamanho (MB)", "% dos dados");
        mo.NegritoLinha(0);
        foreach (var (m, by, li) in Modulos()) mo.Linha(m, li, Mb(by), Math.Round(by * 100.0 / somaT, 2));
        foreach (var (c, w) in new[] { (0, 28.0), (1, 12.0), (2, 14.0), (3, 12.0) }) mo.Largura(c, w);

        var r = wb.Planilha("Arquivos (R2)");
        r.Cabecalho("Arquivo", "Download", "Tamanho (MB)", "Enviado em");
        r.NegritoLinha(0);
        foreach (var x in Maiores) r.Linha(x.Nome, x.Titulo, Mb(x.Bytes), Data(x.Em));
        r.Linha();
        r.Linha("Pasta", "Arquivos", "Tamanho (MB)"); r.NegritoLinha(r.N - 1);
        foreach (var p in Pastas) r.Linha(p.Pasta, p.Objetos, Mb(p.Bytes));
        r.Linha("Total", R2Objetos, Mb(R2Bytes)); r.NegritoLinha(r.N - 1);
        foreach (var (c, w) in new[] { (0, 30.0), (1, 50.0), (2, 14.0), (3, 18.0) }) r.Largura(c, w);

        var h = wb.Planilha("Histórico diário");
        h.Cabecalho("Dia (UTC)", "Pedidos", "Linhas lidas", "Linhas gravadas", "Consultas", "Perguntas IA", "Tokens IA", "IA recusada (429)", "Erros", "Arquivos enviados/listados", "Arquivos baixados", "Banco (MB)", "Arquivos R2 (MB)");
        h.NegritoLinha(0);
        foreach (var d in Historico.OrderBy(d => d.Dia))
            h.Linha(d.Dia, d["req"], d["rd"], d["wr"], d["q"], d["ia"], d["iatok"], d["ia429"], d["err"], d["r2a"], d["r2b"],
                d["banco_bytes"] > 0 ? Mb(d["banco_bytes"]) : null, d["r2_bytes"] > 0 ? Mb(d["r2_bytes"]) : null);
        h.FormatoData(0, 1, Math.Max(1, h.N - 1));
        h.Congelar(1, 1);
        for (int c = 0; c < 13; c++) h.Largura(c, c == 0 ? 12 : 16);

        var ro = wb.Planilha("Pedidos por área");
        var diasR = Historico.OrderByDescending(d => d.Dia).Take(30).ToList();
        var areas = diasR.SelectMany(d => d.V.Keys).Where(k => k.StartsWith("rota:")).Distinct().OrderBy(k => k).ToList();
        ro.Cabecalho(new object?[] { "Área" }.Concat(diasR.Select(d => (object?)d.Dia.ToString("dd/MM"))).Append("Total").ToArray());
        ro.NegritoLinha(0);
        foreach (var g in areas.GroupBy(k => NomeRota(k[5..])).OrderByDescending(g => diasR.Sum(d => g.Sum(k => d[k]))))
            ro.Linha(new object?[] { g.Key }.Concat(diasR.Select(d => (object?)g.Sum(k => d[k]))).Append(diasR.Sum(d => g.Sum(k => d[k]))).ToArray());
        ro.Largura(0, 26);
        ro.Congelar(1, 1);

        var f = wb.Planilha("Ferramentas");
        f.Cabecalho("Ferramenta / ação", "Usos", "Pessoas", "Último uso");
        f.NegritoLinha(0);
        foreach (var x in Ferramentas) f.Linha(NomeFerramenta(x.Chave), x.Usos, x.Pessoas, Data(x.Ultimo));
        foreach (var (c, w) in new[] { (0, 36.0), (1, 10.0), (2, 10.0), (3, 18.0) }) f.Largura(c, w);

        var pe = wb.Planilha("Contas");
        pe.Cabecalho("Papel", "Situação", "Contas");
        pe.NegritoLinha(0);
        foreach (var x in Papeis.OrderBy(p => p.Papel)) pe.Linha(NomePapel(x.Papel), x.Status, x.N);
        foreach (var (c, w) in new[] { (0, 20.0), (1, 14.0), (2, 10.0) }) pe.Largura(c, w);

        return wb.Salvar();
    }

    public static string Data(string iso) =>
        DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d)
            ? d.AddHours(-3).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : "";
}
