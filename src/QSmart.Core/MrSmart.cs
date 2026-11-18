namespace QSmart.Core;

/// <summary>Uma resposta pronta do Mr Smart: palavras que a disparam, o texto e (opcional) a aba para abrir.</summary>
public sealed record RespostaMr(string Pergunta, string[] Chaves, string Texto, string? Aba = null);

/// <summary>Suporte do QSmart: perguntas e respostas prontas, tudo no navegador (nada é enviado para fora).</summary>
public static class MrSmart
{
    public static readonly RespostaMr[] Base =
    {
        new("O que é o QSmart?", new[] { "qsmart", "plataforma", "sistema", "serve", "o que e" },
            "O QSmart é a plataforma de qualidade da Insi. Ele junta decisões, inspeções, pareceres, operação e coordenação num só lugar, e lê as planilhas direto no seu navegador."),
        new("Meus arquivos vão para algum servidor?", new[] { "seguro", "segur", "servidor", "privacidade", "enviado", "nuvem", "dados", "sigilo" },
            "Não. As planilhas são lidas no seu próprio navegador e não são enviadas para nenhum servidor. O que fica salvo (histórico, férias, justificativas) fica guardado neste navegador."),
        new("Como abro o compilado?", new[] { "abrir", "compilado", "anexar", "carregar", "planilha", "arquivo", "upload" },
            "Em cada aba há um botão no canto superior direito, como “Abrir planilha” ou “Anexar compilado”. Clique nele e escolha o arquivo .xlsx. Em algumas abas dá para anexar vários compilados de uma vez."),
        new("O que é o TMA?", new[] { "tma", "tempo medio", "atendimento", "sla" },
            "O TMA (tempo médio de atendimento) usa só os fluxos com início e fim no mesmo dia. Para cada um, conto o tempo de fim − início e tiro a média, cortando os segundos quebrados (sem arredondar). Ele aparece na Estratégia do dia.", "estrategia"),
        new("Como funciona a Estratégia do dia?", new[] { "estrategia", "teams", "mensagem", "bom dia", "centralizada", "autorizar", "paginas" },
            "Anexe o compilado do dia: aparecem o resumo dos serviços, o TMA e o volume. Embaixo, preencha as páginas da Centralizada e da Autorizar (os fluxos são calculados como páginas × 15), escolha um modelo de estratégia e clique em Copiar ou Abrir no Teams.", "estrategia"),
        new("Como calcula os fluxos da Centralizada e Autorizar?", new[] { "15", "fluxos", "multiplica", "quinze" },
            "Ao digitar o número de páginas, o QSmart multiplica por 15 e preenche os fluxos sozinho. Se precisar, você pode ajustar o número à mão.", "estrategia"),
        new("Como vejo aprovações e reprovações?", new[] { "aprova", "reprova", "resumo", "motivo", "devolutiva", "segmento", "decis" },
            "Na aba Resumo, abra a planilha de aprovações. Você vê a barra aprovado × reprovado, o motivo nº 1 e os mapas de motivos e segmentos. Dá para filtrar por usuários, segmentos e dias, e gerar o Relatório PDF.", "resumo"),
        new("Quem mais devolve?", new[] { "ranking", "usuario", "quem", "devolve", "mais" },
            "Na aba Usuários fica o ranking de quem mais devolve, com busca e ordenação. Ela usa a mesma planilha aberta no Resumo.", "usuarios"),
        new("Como vejo os desvios da inspeção?", new[] { "desvio", "evolucao", "interno", "externo", "chamado", "inspetor", "autorizacao" },
            "Na aba Evolução, abra a planilha de inspeção. Ela mostra os desvios Interno × Externo × Chamados por tipo de autorização e por inspetor. Depois dá para clicar em “Gravar no histórico”.", "evolucao"),
        new("Como gravo um mês no histórico?", new[] { "historico", "gravar", "mes", "meses", "evolucao mensal" },
            "Abra a planilha na Evolução e clique em “Gravar no histórico”. Os meses gravados aparecem na aba Histórico, com a evolução mês a mês e a mensagem pronta para o Teams.", "historico"),
        new("Como confiro os pareceres?", new[] { "parecer", "pareceres", "justificativa", "padrao", "conformidade" },
            "Na aba Pareceres, anexe o documento com os pareceres padrão e a planilha a conferir. O QSmart compara as justificativas com o padrão e aponta as divergências.", "pareceres"),
        new("Como vejo quanto cada um executou?", new[] { "volume", "executou", "produc", "quantidade", "lista de usuarios" },
            "Na aba Volume, anexe o compilado e (se quiser) uma lista de usuários. Ela mostra quanto cada pessoa executou.", "volume"),
        new("Como vejo o tempo ocioso?", new[] { "ocioso", "ociosidade", "monitor", "parado", "pausa" },
            "Na aba Monitor de ociosidade você vê o tempo ocioso por colaborador e por dia, calculado a partir das finalizações do compilado.", "monitor"),
        new("Como uno compilados ou PDFs?", new[] { "unir", "juntar", "corredor", "pdf", "consolidar", "mesclar" },
            "Na aba Corredor você une vários compilados num só (ordenando e tirando linhas repetidas) e junta PDFs na ordem que escolher.", "corredor"),
        new("O que é a aba Serviços 3069?", new[] { "3069", "servicos 3069", "analisador", "analisar resposta", "relacionamento", "cooperativas" },
            "Fica na seção Relacionamento e cooperativas. Você cola a pergunta da 3069 e a nossa resposta antes de enviar pelo Teams, e o QSmart confere: se responde a pergunta, objetividade, discordância, pontos que podem ser contestados, tom e escrita. Dá uma nota de 0 a 100 e diz se está pronta para enviar.", "servicos3069"),
        new("Como o analisador sabe se a resposta responde a pergunta?", new[] { "responde a pergunta", "respondeu", "pergunta respondida", "parcial", "nao respondida" },
            "Ele separa cada pergunta da mensagem (pelo “?” ou por tópicos) e vê o tipo: sim ou não, prazo, valor, responsável, como fazer, motivo ou onde. Depois confere se a resposta traz isso (ex.: uma data para “qual o prazo?”) e se retoma os assuntos da pergunta. Cada pergunta fica como respondida, em parte ou não respondida.", "servicos3069"),
        new("O que é um ponto que pode ser contestado?", new[] { "contestado", "contestar", "contestavel", "vago", "em breve", "promessa" },
            "É algo que a cooperativa pode questionar porque não ficou bem explicado: termos vagos (“em breve”, “alguns”), promessa sem data ou sem responsável, palavras absolutas (“sempre”, “nunca”), regra citada sem número, prazo sem dizer se são dias úteis, “depende” sem dizer de quê, culpa jogada na cooperativa e siglas sem explicação.", "servicos3069"),
        new("O que o analisador olha no tom da resposta?", new[] { "tom", "formal", "formalidade", "informal", "saudacao", "educado", "cordial" },
            "Para a 3069 o tom é de comunicação oficial: começa com saudação, termina se colocando à disposição, fala como instituição (“nós”), pede em vez de mandar e não usa gírias, abreviações (vc, pq, pra), emojis, “!!” nem expressões como “como já informado”. Negativas precisam vir com uma alternativa.", "servicos3069"),
        new("A resposta da 3069 fica salva ou vai para algum lugar?", new[] { "salva a resposta", "fica salva", "enviada", "privacidade 3069", "sigilo" },
            "Não. A análise é feita no seu navegador, por regras do próprio QSmart. A pergunta e a resposta não são enviadas para nenhum lugar e não ficam salvas: ao sair da tela, somem. Use “Copiar resposta” para levar ao Teams.", "servicos3069"),
        new("O que é o One a One?", new[] { "one a one", "one on one", "1:1", "1 a 1", "esteira", "conversa individual" },
            "É a aba da Coordenação para acompanhar os 1:1 com cada colaborador. Cada pessoa é um cartão numa esteira da sua liderança: Em dia → Chegando (vence em até 14 dias) → Atrasado. Quando você registra o 1:1, o cartão volta para o começo.", "oneaone"),
        new("Como funciona o prazo do 1:1?", new[] { "prazo do 1:1", "novo", "antigo", "30 dias", "90 dias", "ciclo", "proximo 1:1" },
            "Novo: 1:1 a cada 30 dias. Antigo: a cada 90 dias. O Novo vira Antigo sozinho 90 dias depois de entrar (ou do primeiro 1:1). Quem muda de liderança ganha um 1:1 em até 30 dias, mesmo sendo Antigo. Quem nunca teve 1:1 registrado aparece como atrasado.", "oneaone"),
        new("Como registro um 1:1 feito?", new[] { "registrar 1:1", "1:1 feito", "anotar", "observacao", "acao combinada" },
            "Clique no cartão da pessoa. Na ficha, em “Registrar 1:1 feito”, informe a data, o que foi conversado e, se quiser, a ação combinada com prazo. Clique em “1:1 feito”: o próximo 1:1 é calculado e o cartão volta para “Em dia”. As ações em aberto aparecem na lateral e na ficha.", "oneaone"),
        new("Como mudo alguém de liderança no One a One?", new[] { "mudar de lideranca", "trocar de lideranca", "arrastar", "transferir", "mudou de time" },
            "Arraste o cartão para a esteira da outra liderança, ou abra a ficha e troque a liderança. O histórico de 1:1 vai junto com a pessoa e fica registrada a passagem pelos times.", "oneaone"),
        new("E quando entram ou saem pessoas da coordenação?", new[] { "entrou", "saiu", "nova pessoa", "lideranca nova", "arquivar", "o que mudou", "atualizar planilha" },
            "Anexe a planilha atualizada: o QSmart mostra o que mudou (quem entrou, quem saiu, quem mudou de liderança e nomes parecidos) e só aplica quando você confirmar. Quem sai é arquivado com o histórico. Também dá para usar “+ Pessoa” e “+ Liderança” direto na tela; as esteiras se montam sozinhas.", "oneaone"),
        new("Como baixo o modelo do One a One?", new[] { "modelo one a one", "modelo em branco", "como preencher", "exportar planilha" },
            "Em One a One, “Modelo em branco” baixa a planilha vazia com uma aba “Como preencher”, passo a passo. “Exportar planilha” baixa a carteira no mesmo formato, com uma aba Histórico de todos os 1:1 (serve para passar para outra coordenação).", "oneaone"),
        new("Onde ficam salvos os dados do One a One?", new[] { "salvo", "salva", "abrir de novo", "outro computador", "senha", "backup one" },
            "Ficam neste navegador, criptografados. Se você clicar em “Proteger com senha”, também são salvos na pasta de dados (e nos backups) protegidos pela senha. Em outro computador, aponte para a mesma pasta e digite a senha. Nada vai para servidor.", "oneaone"),
        new("Como organizo as férias da equipe?", new[] { "ferias", "folga", "vender", "choque", "datas" },
            "Na aba Férias, clique em “Baixar modelo” e envie para a equipe preencher (usuário, time, data de início, total de dias e se deseja vender). Depois anexe a planilha preenchida: aparecem choques, datas próximas, quem quer vender e quem sai em breve.", "ferias"),
        new("O que é o controle automatizado de férias?", new[] { "automatizado", "automatico", "controle de ferias", "planilha de controle", "segunda aba" },
            "É a segunda aba dentro de Férias. Você anexa a planilha completa de controle (o mesmo formato da planilha do time, com admissão, aquisitivo, concessivo, início, dias, venda e agendamento). O QSmart acha as datas em choque, sugere outras datas para cada uma e, se você aceitar, já troca a data na planilha. O controle de férias de sempre continua igual na primeira aba.", "ferias"),
        new("Como funcionam as sugestões de datas?", new[] { "sugest", "sugere", "sugerida", "nova data", "outra data", "datas proximas", "evitar choque" },
            "Para cada período em choque o QSmart procura a data mais perto da original que tire ou diminua o choque com o mesmo time. A data sugerida começa em dia permitido (não na sexta, sábado, domingo, feriado ou 2 dias antes de feriado), não passa do fim do período concessivo e não bate com outro período da mesma pessoa. Também mostra uma data próxima antes e uma depois, para você escolher.", "ferias"),
        new("Como aceito ou recuso uma sugestão?", new[] { "aceitar", "aceito", "aceita", "manter", "pular", "desfazer", "recusar" },
            "Na revisão aparece uma data em choque por vez (1 de N). Clique em “Mudar para esta data” na opção que quiser, “Manter a data atual” para tirar da revisão ou “Pular por agora”. As aceitas ficam em “Alterações aceitas”, onde dá para desfazer. Há também “Aceitar todas as melhores”.", "ferias"),
        new("O que significa o impacto e os dias de choque?", new[] { "impacto", "dias de choque", "diminuiu", "reducao", "porcentagem" },
            "Dias de choque é a soma dos dias em que duas pessoas do mesmo time ficam fora juntas (cada par conta uma vez). Em cada sugestão você vê o choque da pessoa antes → depois, o choque do time antes → depois e quanto o impacto diminui em %. No topo, a barra mostra o choque atual comparado ao da planilha anexada.", "ferias"),
        new("Como baixo o modelo em branco ou a planilha ajustada?", new[] { "modelo em branco", "planilha ajustada", "baixar ajustada", "baixar modelo", "download" },
            "Na aba Controle automatizado de férias: “Modelo em branco” baixa a planilha de controle vazia no mesmo formato, já com fórmulas, validações e cores. “Baixar planilha ajustada” devolve a sua planilha com as datas aceitas trocadas e uma nota na coluna de observações; o resto da planilha fica igual.", "ferias"),
        new("A planilha de férias fica pública ou vai para algum servidor?", new[] { "privacidade", "publico", "publica", "seguranca", "criptografia", "servidor", "lgpd" },
            "Não. A planilha é lida no seu navegador e não é enviada para nenhum servidor. Ela fica guardada só neste navegador, criptografada (AES-256) com uma chave que não sai dele. Para apagar, use “Limpar”. Outra pessoa só vê se você mandar o arquivo.", "ferias"),
        new("Por que uma data em choque não entrou na revisão?", new[] { "nao entrou", "nao aparece", "sem sugestao", "ja tirou", "ja comecou" },
            "Só entram na revisão os períodos que ainda podem mudar: que começam depois de hoje e não estão como “Já tirou”. Se nenhuma data diminuir o choque dentro das regras, o QSmart avisa que não achou sugestão e você pode manter a data.", "ferias"),
        new("Quais prazos e regras da CLT o controle confere?", new[] { "prazo", "concessivo", "aquisitivo", "vencido", "vencendo", "clt", "fracionamento" },
            "O controle mostra quem tem o período concessivo vencido, quem vence em até 180 dias sem férias marcadas e quem está sem data de admissão. Também lembra as regras: cada período com pelo menos 5 dias, um deles com 14 ou mais, no máximo 3 períodos e não começar nos 2 dias antes de folga ou feriado.", "ferias"),
        new("Como justifico quem ficou abaixo da meta?", new[] { "pontos", "meta", "150", "abaixo", "justific", "lider", "ponto focal" },
            "Na aba Pontos de ação mensal, anexe o compilado do mês. Na tabela de detalhe, escolha a justificativa (Líder técnico, Ponto focal, Prioridades ou Feedback e acompanhamento). Quem é justificado sai de Crítico/Atenção.", "pontos"),
        new("A planilha não abre ou dá erro", new[] { "erro", "nao abre", "problema", "falha", "nao le", "travou", "depurador" },
            "Confira se o arquivo é .xlsx e se tem as colunas esperadas. Se continuar, use a aba Depurador de leitura: ela mostra, linha a linha, o que o sistema conseguiu ler e o que ficou de fora.", "depurador"),
        new("Como faço backup?", new[] { "backup", "salvar", "perder", "copia", "restaurar", "outro computador" },
            "Em Configurações (no rodapé do menu) você salva um backup dos seus dados e pode restaurar depois, por exemplo ao trocar de computador."),
        new("Como ativo o modo escuro?", new[] { "escuro", "modo claro", "tema", "dark" },
            "No rodapé do menu lateral, clique em “Modo escuro” (ou “Modo claro”). A escolha fica salva neste navegador."),
        new("Como escondo abas que não uso?", new[] { "esconder", "ocultar", "abas", "menu", "configura" },
            "Em Configurações dá para desligar as abas que você não usa. Elas somem do menu e da tela inicial."),
        new("O sistema não atualizou", new[] { "atualiz", "versao", "nova", "nao mudou", "cache", "ctrl" },
            "Recarregue a página com Ctrl+F5 para baixar a versão mais nova do QSmart."),
        new("O que é o InsiTalent e a Mesa de Inspeção?", new[] { "insitalent", "talent", "mesa", "pulso" },
            "São outros sistemas da Insi. Em Ferramentas há atalhos para eles: ao clicar, abrem em uma nova aba do navegador."),
    };

    public static readonly string[] Sugestoes = { "Como abro o compilado?", "O que é o TMA?", "Como faço backup?", "Como organizo as férias da equipe?", "O que é o controle automatizado de férias?" };

    /// <summary>Melhor resposta para a pergunta (ou null quando nada combina).</summary>
    public static RespostaMr? Responder(string pergunta)
    {
        var q = " " + Texto.NormalizarBusca(pergunta) + " ";
        if (q.Trim().Length < 2) return null;
        var exata = Base.FirstOrDefault(r => Texto.NormalizarBusca(r.Pergunta) == q.Trim());
        if (exata != null) return exata;
        RespostaMr? melhor = null; int pontos = 0;
        foreach (var r in Base)
        {
            int p = 0;
            foreach (var c in r.Chaves)
            {
                var k = Texto.NormalizarBusca(c);
                if (k.Length <= 3 ? q.Contains(" " + k + " ") || q.Contains(" " + k) : q.Contains(k)) p += k.Length >= 6 ? 3 : 2;
            }
            foreach (var w in Texto.NormalizarBusca(r.Pergunta).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (w.Length >= 5 && q.Contains(w)) p += 1;
            if (p > pontos) { pontos = p; melhor = r; }
        }
        return pontos >= 2 ? melhor : null;
    }

    /// <summary>Saudação e cumprimentos simples.</summary>
    public static string? Conversa(string pergunta)
    {
        var q = Texto.NormalizarBusca(pergunta);
        if (q is "oi" or "ola" or "bom dia" or "boa tarde" or "boa noite" or "e ai" or "opa") return "Olá! 😊 Em que posso ajudar? Pode perguntar sobre qualquer ferramenta do QSmart.";
        if (q.StartsWith("obrigad") || q is "valeu" or "vlw" or "brigado") return "Por nada! Se precisar de mais alguma coisa, é só chamar. 🤖";
        return null;
    }
}
