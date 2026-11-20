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
        new("depurador", "Depurador de leitura", "Depurador", "depurador", "Ferramentas",
            "Mostra, linha a linha, o que o sistema lê de uma planilha e o que fica de fora.", "depurador"),
        new("insitalent", "InsiTalent", "InsiTalent", "talento", "Ferramentas",
            "Abre o InsiTalent em uma nova aba.", "https://biel1412.github.io/insimatch/"),
        new("mesa", "Mesa de Inspeção", "Mesa de Inspeção", "mesa", "Ferramentas",
            "Abre o Pulso · Mesa de Inspeção em uma nova aba.", "https://biel1412.github.io/healthcoop/"),
    };

    /// <summary>Ordem da lista de abas nas Configurações (igual ao original).</summary>
    public static readonly string[] OrdemConfig = { "resumo", "usuarios", "evolucao", "inspecao", "volume", "monitor", "corredor", "estrategia", "historico", "pareceres", "ferias", "pontos", "oneaone", "servicos3069", "radar360", "depurador", "insitalent", "mesa" };

    public static readonly string[] Grupos = { "Decisões", "Inspeção", "Conformidade", "Operação", "Coordenação", "Relacionamento e cooperativas", "Ferramentas" };

    /// <summary>Cor de cada grupo (classe g-N no CSS).</summary>
    public static int Cor(string grupo) => grupo switch
    {
        "Decisões" => 0, "Inspeção" => 1, "Conformidade" => 2, "Operação" => 3, "Ferramentas" => 4, "Coordenação" => 5, "Relacionamento e cooperativas" => 6, _ => 4,
    };

    public static Aba? Por(string chave) => Todas.FirstOrDefault(a => a.Chave == chave);
}
