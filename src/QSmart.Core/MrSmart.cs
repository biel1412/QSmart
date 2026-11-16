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
        new("Como organizo as férias da equipe?", new[] { "ferias", "folga", "vender", "choque", "datas" },
            "Na aba Férias, clique em “Baixar modelo” e envie para a equipe preencher (usuário, time, data de início, total de dias e se deseja vender). Depois anexe a planilha preenchida: aparecem choques, datas próximas, quem quer vender e quem sai em breve.", "ferias"),
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

    public static readonly string[] Sugestoes = { "Como abro o compilado?", "O que é o TMA?", "Como faço backup?", "Como organizo as férias da equipe?" };

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
