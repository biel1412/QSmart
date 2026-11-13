namespace QSmart.UI.Servicos;

public sealed record Aba(string Chave, string Nome, string Menu, string Icone, string Grupo, string Descricao, string Rota);

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
        new("inspecao", "Inspeção (internas por dia)", "Inspeção", "inspecao", "Inspeção",
            "Inspeções do tipo Interno por inspetor e por dia, com matriz inspetor × dia.", "inspecao"),
        new("historico", "Histórico", "Histórico", "historico", "Inspeção",
            "Meses gravados, evolução mês a mês, filtros e mensagem pronta para o Teams.", "historico"),
        new("pareceres", "Pareceres", "Pareceres", "pareceres", "Conformidade",
            "Confere as justificativas com os pareceres padrão e aponta as divergências.", "pareceres"),
        new("volume", "Volume por usuário", "Volume", "volume", "Operação",
            "Cruza o compilado com uma lista de usuários: quanto cada um executou.", "volume"),
        new("monitor", "Monitor de ociosidade", "Monitor", "monitor", "Operação",
            "Tempo ocioso por colaborador e por dia a partir das finalizações.", "monitor"),
        new("corredor", "Corredor (unir compilados e PDFs)", "Corredor", "corredor", "Operação",
            "Une vários compilados num só e junta PDFs na ordem escolhida.", "corredor"),
        new("ferias", "Férias", "Férias", "ferias", "Coordenação",
            "Organiza as férias da equipe, avisa choques e datas muito próximas e marca quem quer vender dias.", "ferias"),
        new("pontos", "Pontos de ação mensal", "Pontos de ação", "pontos", "Coordenação",
            "Média diária de ações por usuário no mês e painel só com quem fica abaixo de 150.", "pontos"),
        new("depurador", "Depurador de leitura", "Depurador", "depurador", "Ferramentas",
            "Mostra, linha a linha, o que o sistema lê de uma planilha e o que fica de fora.", "depurador"),
    };

    /// <summary>Ordem da lista de abas nas Configurações (igual ao original).</summary>
    public static readonly string[] OrdemConfig = { "resumo", "usuarios", "evolucao", "inspecao", "volume", "monitor", "corredor", "historico", "pareceres", "ferias", "pontos", "depurador" };

    public static readonly string[] Grupos = { "Decisões", "Inspeção", "Conformidade", "Operação", "Coordenação", "Ferramentas" };

    /// <summary>Cor de cada grupo (classe g-N no CSS).</summary>
    public static int Cor(string grupo) => grupo switch
    {
        "Decisões" => 0, "Inspeção" => 1, "Conformidade" => 2, "Operação" => 3, "Ferramentas" => 4, "Coordenação" => 5, _ => 4,
    };

    public static Aba? Por(string chave) => Todas.FirstOrDefault(a => a.Chave == chave);
}
