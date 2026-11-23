namespace QSmart.UI.Servicos;

public sealed record Aba(string Chave, string Nome, string Menu, string Icone, string Grupo, string Descricao, string Rota)
{
    /// <summary>Link para outro sistema: abre em nova aba do navegador.</summary>
    public bool Externo => Rota.StartsWith("http", StringComparison.OrdinalIgnoreCase);
}

/// <summary>As abas do sistema (mesma lista e mesmos nomes do original), agrupadas para o menu.</summary>
public static class Abas
{
    public static readonly Aba[] Todas =
    {
        new("resumo", "Resumo", "Resumo", "resumo", "Decisões",
            "Aprovações x reprovações, motivos e devolutivas por segmento, com filtros e relatório em PDF.", "resumo"),
        new("usuarios", "Usuários", "Usuários", "usuarios", "Decisões",
            "Ranking de quem mais devolve, com busca e ordenação.", "usuarios"),
        new("evolucao", "Evolução", "Evolução", "evolucao", "Inspeção",
            "Desvios Interno x Externo x Chamados por autorização e por inspetor; grava o mês no histórico.", "evolucao"),
        new("inspecao", "Inspeção", "Inspeção", "inspecao", "Inspeção",
            "Inspeções do tipo Interno por inspetor e por dia, com matriz inspetor × dia.", "inspecao"),
        new("historico", "Histórico", "Histórico", "historico", "Inspeção",
            "Meses gravados, evolução mês a mês, filtros e mensagem pronta para o Teams.", "historico"),
        new("pareceres", "Pareceres", "Pareceres", "pareceres", "Conformidade",
            "Confere as justificativas com os pareceres padrão e aponta as divergências.", "pareceres"),
        new("volume", "Volume por usuário", "Volume", "volume", "Operação",
            "Cruza o compilado com uma lista de usuários: quanto cada um executou.", "volume"),
        new("monitor", "Monitor de ociosidade", "Monitor", "monitor", "Operação",
            "Tempo ocioso por colaborador e por dia a partir das finalizações.", "monitor"),
        new("corredor", "Corredor", "Corredor", "corredor", "Operação",
            "Une vários compilados num só e junta PDFs na ordem escolhida.", "corredor"),
        new("estrategia", "Estratégia do dia", "Estratégia", "estrategia", "Operação",
            "Resumo dos serviços, SLA e volume do dia a partir do compilado, com a mensagem da estratégia pronta para o Teams.", "estrategia"),
        new("dayoffpedir", "Pedir day off", "Pedir day off", "estrela", "Operação",
            "A liderança pede o day off de alguém da equipe (até 5 pessoas por dia) e acompanha os pedidos do mês: aprovados, reprovados e aguardando.", "day-off-pedidos"),
        new("dayoff", "Day off", "Day off", "calendario", "Coordenação",
            "Pedidos de day off das lideranças para aprovar ou reprovar e o calendário geral de quem está de folga, entre todas as coordenações.", "day-off"),
        new("ferias", "Férias", "Férias", "ferias", "Coordenação",
            "Férias de cada pessoa do One a One, por liderança: choques de datas, sugestões de novas datas e quanto o impacto diminuiu.", "ferias"),
        new("pontos", "Pontos de ação mensal", "Pontos de ação", "pontos", "Coordenação",
            "Média diária de ações por usuário no mês e painel só com quem fica abaixo de 150.", "pontos"),
        new("oneaone", "One a One", "One a One", "conversa", "Coordenação",
            "Esteira dos 1:1 da coordenação com cada colaborador: quem está em dia, chegando ou atrasado, por liderança, com o registro de cada conversa.", "one-a-one"),
        new("servicos3069", "Serviços 3069", "Serviços 3069", "selo", "Relacionamento e cooperativas",
            "Analisa a nossa resposta para a 3069 antes de ir pelo Teams: se responde a pergunta, objetividade, discordância, pontos que podem ser contestados e tom.", "servicos-3069"),
        new("radar360", "Radar Smart360", "Radar Smart360", "radar", "Relacionamento e cooperativas",
            "Ficha 360 de um CNPJ (cadastro, sócios, linha do tempo, Junta Comercial e checagens públicas) e análise de CPF com finalidade, sem salvar nada.", "radar-smart360"),
        new("pacote", "Pacote Smart", "Pacote Smart", "pacote", "Extensão",
            "Smart Write (textos, como o Word), Smart Math (planilhas com fórmulas e gráficos, como o Excel) e Smart Note (cadernos, como o OneNote) num lugar só.", "pacote-smart"),
        new("downloads", "Downloads", "Downloads", "download", "Ferramentas",
            "Automações, modelos e materiais para baixar: cada item tem título, imagem e o arquivo. Quem publica é o administrador.", "downloads"),
        new("depurador", "Depurador de leitura", "Depurador", "depurador", "Ferramentas",
            "Mostra, linha a linha, o que o sistema lê de uma planilha e o que fica de fora.", "depurador"),
        new("insitalent", "InsiTalent", "InsiTalent", "talento", "Ferramentas",
            "Abre o InsiTalent em uma nova aba.", "https://biel1412.github.io/insimatch/"),
        new("mesa", "Mesa de Inspeção", "Mesa de Inspeção", "mesa", "Ferramentas",
            "Abre o Pulso · Mesa de Inspeção em uma nova aba.", "https://biel1412.github.io/healthcoop/"),
    };

    /// <summary>Ferramentas que vivem dentro de outra (o Pacote Smart): não aparecem no menu, abrem pela tela do pacote.</summary>
    public static readonly Aba[] Internas =
    {
        new("smartwrite", "Smart Write", "Smart Write", "documento", "Extensão",
            "Editor de textos como o Word: abre e salva arquivos .docx no seu computador.", "smart-write"),
        new("smartmath", "Smart Math", "Smart Math", "grade-planilha", "Extensão",
            "Planilhas como o Excel: fórmulas, várias abas, formatação, filtro e gráficos; abre e salva .xlsx no seu computador.", "smart-math"),
        new("smartscreen", "Smart Screen", "Smart Screen", "apresentacao", "Extensão",
            "Apresentações como o PowerPoint: slides com temas, textos, imagens, formas, tabelas e gráficos, transições, animações e modo apresentação; abre e salva .pptx.", "smart-screen"),
        new("smartnote", "Smart Note", "Smart Note", "caderno", "Extensão",
            "Seus cadernos de anotações, como no OneNote: cadernos, páginas e subpáginas, texto formatado, listas de tarefas, tabelas e imagens coladas.", "smart-note"),
    };

    /// <summary>A ferramenta “mãe” de uma interna (para ligar/desligar e para o menu marcar o item certo).</summary>
    public static string? PaiDe(string chave) => Internas.Any(a => a.Chave == chave) ? "pacote" : null;

    public static Aba? PorRota(string rota) => Todas.FirstOrDefault(a => a.Rota == rota) ?? Internas.FirstOrDefault(a => a.Rota == rota);

    /// <summary>Ordem da lista de abas nas Configurações (igual ao original).</summary>
    public static readonly string[] OrdemConfig = { "resumo", "usuarios", "evolucao", "inspecao", "volume", "monitor", "corredor", "estrategia", "dayoffpedir", "historico", "pareceres", "dayoff", "ferias", "pontos", "oneaone", "servicos3069", "radar360", "pacote", "downloads", "depurador", "insitalent", "mesa" };

    public static readonly string[] Grupos = { "Decisões", "Inspeção", "Conformidade", "Operação", "Coordenação", "Relacionamento e cooperativas", "Extensão", "Ferramentas" };

    /// <summary>Cor de cada grupo (classe g-N no CSS).</summary>
    public static int Cor(string grupo) => grupo switch
    {
        "Decisões" => 0, "Inspeção" => 1, "Conformidade" => 2, "Operação" => 3, "Ferramentas" => 4, "Coordenação" => 5, "Relacionamento e cooperativas" => 6, "Extensão" => 7, _ => 4,
    };

    public static Aba? Por(string chave) => Todas.FirstOrDefault(a => a.Chave == chave) ?? Internas.FirstOrDefault(a => a.Chave == chave);
}
