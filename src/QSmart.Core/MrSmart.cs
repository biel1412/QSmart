namespace QSmart.Core;

/// <summary>Uma resposta pronta do Mr Smart: palavras que a disparam, o texto e (opcional) a aba para abrir.</summary>
public sealed record RespostaMr(string Pergunta, string[] Chaves, string Texto, string? Aba = null);

/// <summary>Suporte do QSmart: perguntas e respostas prontas, tudo no navegador (nada é enviado para fora).</summary>
public static class MrSmart
{
    public static readonly RespostaMr[] Base =
    {
        new("O que é o QSmart?", new[] { "qsmart", "plataforma", "sistema", "serve", "o que e" },
            "O QSmart é a plataforma de qualidade da Insi. Ele junta decisões, inspeções, pareceres, operação, coordenação e relacionamento com as cooperativas num só lugar, lê as planilhas direto no seu navegador e tem o Mr. Smart com IA para analisar respostas, escrever a estratégia do dia e dar parecer no Radar Smart360."),
        new("Meus arquivos vão para algum servidor?", new[] { "seguro", "segur", "servidor", "privacidade", "enviado", "nuvem", "dados", "sigilo" },
            "As planilhas não. Elas são lidas no seu próprio navegador e não são enviadas para nenhum servidor; histórico, férias e justificativas ficam neste navegador. Vão para o banco do QSmart (Cloudflare, criptografado): a sua conta (com a senha protegida por criptografia de mão única), o uso para as medalhas e o One a One. Só saem do computador: as consultas do Radar Smart360 (o CNPJ ou CPF vai para fontes públicas, como Receita e Portal da Transparência) e os textos que você pede para o Mr. Smart analisar ou escrever (vão para a IA do Groq, com CPF, CNPJ, e-mail e telefone mascarados)."),
        new("Como abro o compilado?", new[] { "abrir", "compilado", "anexar", "carregar", "planilha", "arquivo", "upload" },
            "Em cada aba há um botão no canto superior direito, como “Abrir planilha” ou “Anexar compilado”. Clique nele e escolha o arquivo .xlsx. Em algumas abas dá para anexar vários compilados de uma vez."),
        new("O que é o SLA do dia?", new[] { "sla", "tma", "tempo medio", "atendimento" },
            "O SLA do dia (tempo médio dos fluxos) usa só os fluxos com início e fim no mesmo dia. Para cada um, conto o tempo de fim − início e tiro a média, cortando os segundos quebrados (sem arredondar). Ele aparece na Estratégia do dia.", "estrategia"),
        new("Como funciona a Estratégia do dia?", new[] { "estrategia", "teams", "mensagem", "bom dia", "centralizada", "autorizar", "paginas" },
            "Anexe o compilado do dia: aparecem o resumo dos serviços, o SLA e o volume. Embaixo, preencha as páginas da Centralizada e da Autorizar (os fluxos são calculados como páginas × 15), escolha um modelo de estratégia (ou peça para o Mr. Smart escrever) e clique em Copiar ou Abrir no Teams.", "estrategia"),
        new("O Mr. Smart escreve a estratégia do dia?", new[] { "mr smart estrategia", "escrever estrategia", "gerar estrategia", "ia estrategia", "pedir que mr smart" },
            "Sim. Na Estratégia do dia, embaixo da caixa do texto, clique em “Pedir que Mr. Smart faça a estratégia do dia”. Ele lê as páginas da Centralizada e da Autorizar, o volume, o SLA e, com o compilado anexado, os serviços que mais entraram e as devoluções, e escreve 3 a 5 frases dizendo onde pôr a força do time, o que priorizar e um ponto de atenção. Se já houver texto, ele pergunta antes e aproveita as suas ideias. Depois é só ajustar.", "estrategia"),
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
        new("O que é o Radar Smart360?", new[] { "radar", "smart360", "radar 360", "consulta cnpj", "cnpj", "consultar empresa", "ficha 360", "cpf" },
            "Fica em Relacionamento e cooperativas. Digite um CNPJ para ver a ficha 360: situação na Receita, abertura, natureza, porte, capital, atividades, sócios e dirigentes, linha do tempo, Junta Comercial do estado, mapa e checagens públicas (TCU, CNJ, CGU, Banco Central, contratos públicos, diários oficiais). Tem placar por assunto, parecer do Mr. Smart, ficha em PDF e texto pronto para o Teams. Também aceita CPF, com outra análise.", "radar360"),
        new("De onde vêm os dados do Radar Smart360?", new[] { "fonte radar", "brasilapi", "receita", "atualizado", "desatualizado", "30 dias", "radar fonte" },
            "Só de fontes públicas. O cadastro vem dos dados abertos da Receita (pela BrasilAPI, com Minha Receita e CNPJá de reserva), que podem atrasar até uns 30 dias. As checagens vêm do TCU, CGU (Portal da Transparência), Banco Central, Querido Diário e Registro.br. O que exige captcha ou login (certidões da Receita, TST, FGTS, protestos, IBAMA, lista suja) aparece como atalho para o site oficial.", "radar360"),
        new("O Radar Smart360 salva o que eu consulto?", new[] { "radar salva", "historico radar", "cpf salvo", "lgpd radar", "privacidade radar", "monitorar" },
            "CNPJ: guarda a lista de recentes e um retrato do cadastro a cada consulta, só neste navegador e criptografado, para mostrar o que mudou e para os monitorados. Nomes de sócios não são guardados (só uma impressão digital para saber se alguém entrou ou saiu). CPF: nada é salvo, o número aparece mascarado e é preciso dizer a finalidade antes de consultar.", "radar360"),
        new("Como funciona a consulta de CPF no Radar?", new[] { "cpf radar", "pep", "beneficio social", "bolsa familia", "servidor", "inabilitado", "empresas ligadas" },
            "Escolha CPF, digite o número, escolha a finalidade e, se quiser, o nome (ajuda nas buscas). O Radar valida o CPF e confere sanções da CGU (CEIS, CNEP, expulsões), PEP, vínculo de servidor federal, benefícios sociais e o quadro de inabilitados do Banco Central, além de procurar a pessoa no quadro de sócios dos CNPJs que você já consultou. Nada disso precisa de configuração: o QSmart já consulta o Portal da Transparência por um intermediário com a chave guardada.", "radar360"),
        new("Como monitoro um CNPJ no Radar?", new[] { "monitorar cnpj", "monitorado", "estrela", "verificar monitorados", "avisar mudanca" },
            "Clique na estrela do CNPJ nos Recentes (ou em “Monitorar” na ficha). Depois, “Verificar monitorados” consulta de novo cada um e mostra o que mudou desde a última vez: situação, capital, endereço, atividades, entrada ou saída de sócios. As mudanças também entram na linha do tempo.", "radar360"),
        new("Por que o parecer do Radar não aparece?", new[] { "parecer radar", "parecer do mr smart", "parecer 360", "sem parecer" },
            "O parecer sai sozinho quando as checagens terminam. Se não aparecer ou der erro, normalmente é o limite gratuito da IA (Groq) atingido: espere um minuto e clique em “Refazer”. O resto do Radar funciona normalmente mesmo sem o parecer.", "radar360"),
        new("O que é a Junta Comercial no Radar?", new[] { "junta", "junta comercial", "certidao junta", "jucemg", "historico junta", "drei" },
            "O cartão Juntas Comerciais destaca a Junta do estado da empresa, com o site dela e a página oficial no DREI, e tem as 27 Juntas do Brasil. Peça a certidão simplificada ou de inteiro teor no site da Junta e use “Anexar certidão da Junta (PDF)”: o Radar lê as datas e põe os arquivamentos na linha do tempo (só nesta consulta).", "radar360"),
        new("O que é a aba Serviços 3069?", new[] { "3069", "servicos 3069", "analisador", "analisar resposta", "relacionamento", "cooperativas" },
            "Fica na seção Relacionamento e cooperativas. Você cola a pergunta da 3069 e a nossa resposta e clica em “Analisar com Mr. Smart”. A IA (Groq) avalia a resposta diante da pergunta: se responde, objetividade, discordância, o que pode ser contestado, tom e escrita. Dá uma nota de 0 a 100, marca os trechos no texto com sugestões, diz como a 3069 deve reagir e sugere uma resposta reescrita.", "servicos3069"),
        new("O que precisa da chave do Groq?", new[] { "groq", "chave", "api key", "contate o suporte", "bloqueada", "liberar", "modo desenvolvedor", "habilitar recursos" },
            "Tudo que usa a IA do Mr. Smart: a análise do Serviços 3069, o parecer do Radar Smart360 e o botão que escreve a Estratégia do dia. Ninguém precisa configurar: a chave já vem pronta, guardada no intermediário do QSmart (Cloudflare), e funciona em qualquer máquina. O limite gratuito do Groq é dividido entre todos; se aparecer “limite atingido”, espere um minuto.", "servicos3069"),
        new("Como aplico as sugestões da IA?", new[] { "aplicar", "ignorar", "destaque", "numero no texto", "corrigir resposta", "resposta sugerida" },
            "Cada trecho com problema fica sublinhado e numerado; o mesmo número aparece no cartão ao lado, com o que sai (riscado) e o que entra (em verde). “Aplicar” troca na hora e “Ignorar” esconde. A “Resposta sugerida pela IA” pode ser copiada ou usada no lugar da sua. O que estiver entre colchetes, como [data] ou [motivo], precisa ser completado por você. Depois de mudar o texto, clique em “Analisar de novo”.", "servicos3069"),
        new("Os textos da 3069 ficam salvos ou vão para algum lugar?", new[] { "privacidade", "fica salva", "enviada", "sigilo", "treinar", "dados pessoais", "mascarar" },
            "A pergunta e a resposta são enviadas ao Groq só para a análise. Antes do envio, CPF, CNPJ, e-mail e telefone são trocados por marcadores. O Groq não usa os textos para treinar modelos e, com Zero Data Retention ligado, não guarda nem registros temporários. No QSmart nada fica salvo: ao sair da tela, os textos somem.", "servicos3069"),
        new("Deu limite atingido no Groq. O que faço?", new[] { "limite", "429", "rate limit", "erro groq", "nao analisou" },
            "O plano gratuito do Groq tem limite por minuto e por dia (por conta, não por chave). Espere um minuto e clique em “Analisar de novo”. Se acontecer sempre, o dono da conta pode ativar o plano Developer no Groq, que aumenta os limites.", "servicos3069"),
        new("Como funciona o login do QSmart?", new[] { "login", "entrar", "criar conta", "cadastro", "usuario", "senha", "aprovacao", "aguardando aprovacao" },
            "Na primeira vez, clique em “Criar conta” e informe nome, sobrenome, usuário e senha (mínimo 8 caracteres). O administrador aprova o acesso. Depois, sempre que abrir o QSmart, digite a senha; com “Lembrar meu usuário” ligado, o usuário já vem preenchido. A senha é guardada com criptografia de mão única: ninguém consegue ler."),
        new("Esqueci a senha, e agora?", new[] { "esqueci a senha", "esqueci minha senha", "resetar senha", "codigo de recuperacao", "perdi o codigo", "trocar senha" },
            "Na tela de entrada, clique em “Esqueci a senha” e use o código de recuperação que apareceu quando você criou a conta. Ao trocar, você recebe um código novo. Perdeu o código? Peça ao administrador: no Perfil, em Equipe e acessos, ele gera um código novo para você."),
        new("Como ganho medalhas e troféus?", new[] { "medalha", "medalhas", "trofeu", "trofeus", "nivel", "xp", "perfil", "conquista" },
            "Usando o QSmart. Cada dia de uso, cada ferramenta aberta e ações como consultar no Radar, registrar 1:1 ou pedir a estratégia ao Mr. Smart dão XP e avançam as medalhas. Os troféus sobem de bronze para prata e ouro. Clique na sua foto, no canto de cima, para ver o perfil, trocar a foto e acompanhar o progresso."),
        new("O que cada papel pode acessar?", new[] { "papel", "papeis", "acesso", "permissao", "usuario comum", "o que o usuario ve", "nao aparece no menu" },
            "Administrador: tudo, inclusive aprovar contas e publicar Downloads. Coordenador: tudo, inclusive a seção Coordenação (Férias, Pontos de ação, One a One e Day off). Liderança: tudo menos a seção Coordenação, e pode pedir day off e cadastrar a própria equipe. Usuário: só Inspeção, Conformidade, Extensão e Ferramentas (onde ficam os Downloads). Feed, chat e Perfil valem para todos. Quem define o papel é o administrador, em Perfil › Equipe e acessos.", null),
        new("Onde vejo o uso de dados e armazenamento?", new[] { "uso de dados", "armazenamento", "memoria", "limite", "limites", "consumo", "cloudflare", "espaco", "quanto falta" },
            "Só o administrador: no botão de gráfico no canto de cima (ao lado do chat) fica a página Uso e armazenamento. Ela mostra o tamanho do banco e dos arquivos, os pedidos, leituras e gravações do dia, o uso da IA, os últimos 30 dias e avisos quando algo passa de 70% (atenção) ou 90% (crítico) do limite do plano grátis. Um ponto de exclamação no botão indica que há algo para olhar. Dá para baixar tudo em PDF ou Excel. Os limites diários zeram às 21h (Brasília).", null),
        new("Como funciona o aviso de aniversário?", new[] { "aniversario", "aniversariante", "parabens", "nascimento", "post de aniversario", "gerador" },
            "A liderança informa a data de nascimento de cada pessoa em Perfil › Minha equipe. No dia do aniversário, todas as contas (menos as de papel Usuário) recebem no sininho \"Hoje é aniversário de…\" com o link do gerador de post de aniversário da Insi (insi.com/br/gerador-post-aniversario). Quem nasceu em 29/02 é lembrado em 28/02 nos anos que não são bissextos.", "perfil"),
        new("Como funciona o Downloads?", new[] { "download", "downloads", "baixar", "automacao", "automacoes", "materiais", "arquivo zip" },
            "Em Ferramentas › Downloads ficam automações e materiais para baixar: cada item tem título, imagem e o botão Baixar. Só o administrador publica, edita e exclui (botão Novo download), com arquivos de até 100 MB. Os arquivos ficam no armazenamento de arquivos da Cloudflare (R2) e só quem está logado no QSmart consegue baixar.", "downloads"),
        new("Quem vê o One a One?", new[] { "quem ve one a one", "permissao one a one", "acesso one a one", "lideranca ve", "coordenador ve" },
            "Só coordenador e administrador entram na seção Coordenação. Cada coordenador vê no One a One as pessoas da planilha que ele carregou no Perfil; o administrador vê todas. Liderança e usuário usam o resto do QSmart. Os dados ficam no banco do QSmart, criptografados.", "oneaone"),
        new("O que é o One a One?", new[] { "one a one", "one on one", "1:1", "1 a 1", "esteira", "conversa individual" },
            "É a aba da Coordenação para acompanhar os 1:1 com cada colaborador. Cada pessoa é um cartão numa esteira da sua liderança: Em dia → Chegando (vence em até 14 dias) → Atrasado. Quando você registra o 1:1, o cartão volta para o começo.", "oneaone"),
        new("Como funciona o prazo do 1:1?", new[] { "prazo do 1:1", "novo", "antigo", "30 dias", "90 dias", "ciclo", "proximo 1:1" },
            "Novo: 1:1 a cada 30 dias. Antigo: a cada 90 dias. O Novo vira Antigo sozinho 90 dias depois de entrar (ou do primeiro 1:1). Quem muda de liderança ganha um 1:1 em até 30 dias, mesmo sendo Antigo. Quem nunca teve 1:1 registrado aparece como atrasado.", "oneaone"),
        new("Como registro um 1:1 feito?", new[] { "registrar 1:1", "1:1 feito", "anotar", "observacao", "acao combinada" },
            "Clique no cartão da pessoa. Na ficha, em “Registrar 1:1 feito”, informe a data, o que foi conversado e, se quiser, a ação combinada com prazo. Clique em “1:1 feito”: o próximo 1:1 é calculado e o cartão volta para “Em dia”. As ações em aberto aparecem na lateral e na ficha.", "oneaone"),
        new("Como mudo alguém de liderança no One a One?", new[] { "mudar de lideranca", "trocar de lideranca", "arrastar", "transferir", "mudou de time" },
            "Arraste o cartão para a esteira da outra liderança, ou abra a ficha e troque a liderança. O histórico de 1:1 vai junto com a pessoa e fica registrada a passagem pelos times.", "oneaone"),
        new("E quando entram ou saem pessoas da coordenação?", new[] { "entrou", "saiu", "nova pessoa", "lideranca nova", "arquivar", "o que mudou", "atualizar planilha" },
            "Cadastre direto na tela: “+ Pessoa” para quem entrou e “+ Liderança” para um time novo. O jeito mais rápido é cada liderança carregar a planilha da equipe em Perfil › Minha equipe. Quem saiu: abra a ficha e clique em Arquivar; o histórico fica guardado e dá para reativar. Tudo é salvo no banco do QSmart na hora.", "oneaone"),
        new("Como exporto o One a One?", new[] { "exportar one a one", "planilha one a one", "modelo one a one", "exportar planilha" },
            "Em One a One, “Exportar planilha” baixa a carteira em Excel, com uma aba Histórico de todos os 1:1. A planilha serve só para exportar: os cadastros e registros são feitos na própria tela e ficam no banco do QSmart.", "oneaone"),
        new("Onde ficam salvos os dados do One a One?", new[] { "salvo", "salva", "abrir de novo", "outro computador", "senha", "backup one" },
            "Ficam no banco do QSmart, criptografados, e só abrem com login. Cada coordenador vê a sua coordenação e o administrador vê todas. Para ter uma cópia, use “Exportar planilha”.", "oneaone"),
        new("O que o Mr. Smart IA sabe responder?", new[] { "mr smart ia", "o que voce sabe", "o que voce faz", "voce le os cadernos", "pergunta sobre caderno", "assistente" },
            "Duas coisas: dúvidas sobre o QSmart (ferramentas, cálculos, acessos) e perguntas sobre o que está escrito nos seus cadernos do Smart Note, os seus e os compartilhados com você. Quando a resposta vem dos cadernos, eu mostro as páginas usadas como fonte; clique para abrir. Se não encontrar, eu aviso em vez de inventar. Imagens dentro das páginas eu não consigo ler.", "smartnote"),
        new("O que é o Smart Note?", new[] { "smart note", "smartnote", "onenote", "one note", "caderno", "anotacao", "anotacoes", "bloco de notas" },
            "É o seu caderno de anotações dentro do QSmart, como o OneNote. Crie cadernos, páginas e subpáginas; formate o texto (títulos, negrito, cores, marca-texto), faça listas de tarefas, tabelas e cole prints com Ctrl+V. Salva sozinho, criptografado. Cada caderno é de quem criou; outras pessoas só veem se você der acesso.", "smartnote"),
        new("Como crio subpáginas e organizo o Smart Note?", new[] { "subpagina", "sub pagina", "mover pagina", "organizar pagina", "nova pagina", "excluir pagina" },
            "Passe o mouse na página e clique no ícone de subpágina para criar uma dentro dela. No menu (☰) da página dá para mover para cima ou para baixo, virar subpágina da anterior, subir um nível, mudar de caderno ou excluir. Excluir uma página apaga também as subpáginas.", "smartnote"),
        new("Como compartilho um caderno do Smart Note?", new[] { "compartilhar caderno", "caderno compartilhado", "dar acesso", "editar junto", "convidar", "link do caderno", "compartilhado comigo" },
            "No caderno, clique no ícone de pessoa. Escolha alguém do QSmart e se a pessoa “Pode editar” ou “Só ler”; ela recebe um aviso no sininho e vê o caderno em “Compartilhados comigo”. Só o dono muda os acessos, renomeia ou exclui o caderno. Para quem não usa o QSmart, gere ali mesmo um link de leitura, que abre sem login e pode ser desativado. Para compartilhar todos os seus cadernos de uma vez, use o ícone de link no topo da lista de cadernos: é um link só para o Smart Note inteiro.", "smartnote"),
        new("E se duas pessoas editarem a mesma página?", new[] { "editar ao mesmo tempo", "conflito", "versao nova", "outra pessoa alterou" },
            "O QSmart avisa. Se alguém salvar a página enquanto você edita, aparece uma faixa vermelha para você escolher entre manter a sua versão ou ver a da outra pessoa. E, se a página que você está vendo mudar, aparece “Ver a versão nova”.", "smartnote"),
        new("Como colo imagens no Smart Note?", new[] { "colar imagem", "print", "imagem no caderno", "ctrl v", "arrastar imagem" },
            "Copie a imagem ou o print e aperte Ctrl+V dentro da página, ou arraste o arquivo para lá. Também tem o botão de imagem na barra. As imagens são reduzidas para caber bem; se uma página ficar cheia de imagens, crie uma subpágina para continuar.", "smartnote"),
        new("O que é o Feed?", new[] { "feed", "mural", "publicar", "postar", "postagem", "curtir", "comentar" },
            "É o mural do QSmart: todo mundo com acesso pode publicar texto e até 4 imagens, curtir e comentar. Abra pelo ícone de mural no canto de cima, ao lado do sininho. Quem publicou (ou um administrador) pode apagar a publicação."),
        new("Para que serve o sininho?", new[] { "sininho", "sino", "notificacao", "notificacoes", "alerta", "aviso" },
            "O sininho, no canto de cima, avisa quando alguém publica no Feed, curte ou comenta a sua publicação, ou comenta numa publicação que você comentou. Também lembra os feriados nacionais dos próximos 7 dias. Ao abrir, tudo fica como lido."),
        new("Como organizo as férias da equipe?", new[] { "ferias", "folga", "vender", "choque", "datas", "calendario" },
            "A aba Férias é o painel geral de todas as coordenações. Cada coordenação marca as férias das suas pessoas (clique no nome → “+ Período”) e vê as das outras. O mapa dos meses mostra quantas pessoas estão de férias em cada mês: até 3 por coordenação e, no geral, o número de coordenações × 3. Os meses que passam do limite ficam vermelhos e todas as coordenações recebem aviso no sininho.", "ferias"),
        new("De onde vêm as pessoas das férias?", new[] { "planilha base", "base de colaboradores", "admissao", "carregar planilha", "automatizado", "planilha" },
            "Das equipes que cada liderança cadastra em Perfil › Minha equipe (planilha com Nome, Data de admissão e Data de nascimento, ou uma pessoa por vez). Cada liderança é ligada a uma coordenação pelo administrador, e as pessoas aparecem nas Férias e no One a One dessa coordenação. Quando a liderança carrega a planilha de novo, quem já existe é atualizado e quem saiu é arquivado com o histórico. A coordenação ajusta pessoa por pessoa em Perfil › Base de colaboradores.", "ferias"),
        new("Como funciona o chat?", new[] { "chat", "conversar", "mensagem", "mensagens", "falar com", "conversa" },
            "Clique no ícone de balões no topo, ao lado do Feed. Aparecem todas as pessoas com conta no QSmart (quem está online tem uma bolinha verde). Clique numa pessoa para conversar: Enter envia e Shift+Enter quebra a linha. As conversas são criptografadas e cada mensagem se apaga sozinha 10 dias depois de enviada. O número verde no ícone mostra as mensagens não lidas.", null),
        new("Como funciona o day off?", new[] { "day off", "dayoff", "folga", "pedir folga", "aprovar folga", "remarcar" },
            "A liderança pede em Operação › Pedir day off: nome completo, aniversário e o dia. Cada dia aceita até 5 pessoas somando todas as coordenações (aguardando + aprovados); dia cheio não deixa pedir. A coordenação da equipe recebe no sininho e em Coordenação › Day off aprova ou reprova com motivo. Aprovado entra no calendário geral; reprovado, a liderança remarca outra data.", "dayoff"),
        new("Como a liderança cadastra a equipe dela?", new[] { "minha equipe", "lider cadastra", "lideranca cadastra", "cadastrar colaborador", "meus colaboradores", "cadastrar equipe" },
            "No Perfil, em Minha equipe: carregue uma planilha com Nome, Data de admissão e Data de nascimento (baixe o modelo; a coluna Liderança, se houver, é ignorada) ou adicione um por um, sempre com nome, admissão e nascimento. Só as lideranças carregam planilha de colaboradores; a coordenação vê tudo no One a One e nas Férias e pode ajustar pessoa por pessoa. No dia do aniversário de alguém da equipe, administradores, coordenação e lideranças recebem um aviso no sininho com o link do gerador de post de aniversário da Insi.", "perfil"),
        new("Como funcionam as sugestões de datas?", new[] { "sugest", "sugere", "sugerida", "nova data", "outra data", "melhor data" },
            "Ao clicar numa pessoa, o QSmart sugere 2 datas próximas que cabem no prazo dela, começando numa segunda-feira, com menos gente da coordenação de férias no mês e ninguém (ou pouca gente) da mesma liderança junto. Clique em “Mudar para esta” para aplicar.", "ferias"),
        new("O que é a pessoa-chave que não impacta?", new[] { "pessoa chave", "nao impacta", "impacto", "desconsiderar", "conflito" },
            "Algumas pessoas podem sair sem pesar na operação. Na ficha da pessoa, ligue “Pessoa-chave: não impacta”: ela continua no calendário, mas não entra na conta de quantos estão de férias no mês, então não gera conflito nem alerta.", "ferias"),
        new("Quais prazos e regras da CLT o QSmart confere?", new[] { "prazo", "concessivo", "aquisitivo", "vencido", "vencendo", "clt", "fracionamento", "vender", "limite" },
            "As férias só liberam depois de 12 meses de trabalho (período aquisitivo) e têm de ser tiradas nos 12 meses seguintes: o fim desse prazo é o limite, marcado em vermelho na linha do tempo. Dá para dividir em até 3 períodos (um com 14 dias ou mais, os outros com 5 ou mais) e vender no máximo 10 dias. O QSmart avisa período sobreposto, que passa do limite, que começa na sexta ou no fim de semana e quem precisa marcar com urgência.", "ferias"),
        new("Quem vê as férias e onde ficam salvas?", new[] { "privacidade", "publico", "publica", "seguranca", "criptografia", "servidor", "lgpd", "quem ve" },
            "Só coordenador e administrador entram na seção Coordenação. Todas as coordenações veem o painel geral de férias, mas cada uma só marca e muda as suas pessoas. Fica tudo no banco do QSmart, criptografado. Para ter uma cópia, use “Exportar planilha”.", "ferias"),
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
        new("De onde vêm a data, a hora e os feriados do Início?", new[] { "feriado", "feriados", "relogio", "hora", "data", "amanha e feriado" },
            "Ao lado da saudação aparecem o dia, a data e a hora do seu computador. Os feriados nacionais vêm da BrasilAPI (e de uma lista própria quando sem internet). Quando há feriado nos próximos 7 dias, aparece o aviso; passe o mouse na data para ver os próximos 3 feriados."),
        new("O que é o InsiTalent, a Mesa de Inspeção e o Selo Qualidade?", new[] { "insitalent", "talent", "mesa", "pulso", "selo qualidade", "certificado", "certificados" },
            "São outros sistemas da Insi (o Selo Qualidade gera certificados). Em Ferramentas há atalhos para eles: ao clicar, abrem em uma nova aba do navegador."),
    };

    public static readonly string[] Sugestoes = { "O que é o Radar Smart360?", "O Mr. Smart escreve a estratégia do dia?", "O que é o SLA do dia?", "O que é o One a One?", "O que precisa da chave do Groq?" };

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
