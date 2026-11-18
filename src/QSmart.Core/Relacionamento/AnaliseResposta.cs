using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QSmart.Core.Relacionamento;

// =====================================================================================
//  Analisador de resposta (Serviços 3069).
//  Recebe a pergunta da cooperativa e a NOSSA resposta, antes de ir pelo Teams, e confere:
//  se responde, objetividade, discordância, pontos que podem ser contestados, tom e escrita.
//  Tudo por regras locais: o texto não sai do navegador.
// =====================================================================================

public enum Gravidade { Leve, Atencao, Problema }

public static class Cat
{
    public const string Responde = "responde", Objetividade = "objetividade", Discordancia = "discordancia",
        Contestavel = "contestavel", Tom = "tom", Escrita = "escrita";

    public static string Nome(string c) => c switch
    {
        Responde => "Responde a pergunta",
        Objetividade => "Objetividade",
        Discordancia => "Discordância",
        Contestavel => "Pode ser contestado",
        Tom => "Tom e formalidade",
        _ => "Escrita",
    };
}

/// <summary>Algo encontrado na resposta. Ini/Fim marcam o trecho na resposta (-1 quando é da resposta toda).</summary>
public sealed record Achado(string Categoria, Gravidade Grav, string Titulo, string Explicacao, string? Trecho, int Ini, int Fim, string? Sugestao);

public enum StatusPergunta { Atendida, Parcial, NaoAtendida }

/// <summary>Cada pergunta contida no texto da cooperativa e se a resposta atende.</summary>
public sealed record SubPergunta(string Texto, string Tipo, StatusPergunta Status, string Motivo, List<string> TermosFaltando);

public sealed class Analise
{
    public List<SubPergunta> Perguntas { get; } = new();
    public List<Achado> Achados { get; } = new();
    public int Nota { get; set; }
    public string Veredito { get; set; } = "";
    public string VereditoClasse { get; set; } = "";
    public int Palavras { get; set; }
    public int Frases { get; set; }
    public double MediaPalavrasFrase { get; set; }
    public int SegundosLeitura { get; set; }
    public IEnumerable<Achado> Da(string cat) => Achados.Where(a => a.Categoria == cat);
}

public static class AnaliseResposta
{
    // ======================== texto ========================

    /// <summary>Minúsculas sem acento, com o MESMO tamanho do original (as posições batem).</summary>
    public static string Norm(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            char b = ch;
            if (ch >= 'À' && !char.IsSurrogate(ch))
            {
                var d = Texto.SemAcento(ch.ToString());
                if (d.Length == 1) b = d[0];
            }
            sb.Append(char.ToLowerInvariant(b));
        }
        return sb.ToString();
    }

    static readonly HashSet<string> Vazias = new(StringComparer.Ordinal)
    {
        "a","o","as","os","um","uma","uns","umas","de","da","do","das","dos","em","na","no","nas","nos","por","pela","pelo","pelas","pelos","para","pra","com","sem",
        "e","ou","que","se","como","mais","menos","muito","muita","ja","ainda","tambem","so","mas","porem","entao","isso","isto","esse","essa","este","esta","esses","essas",
        "aquele","aquela","ele","ela","eles","elas","nos","voce","voces","seu","sua","seus","suas","nosso","nossa","meu","minha","lhe","me","te","ao","aos","a","ate","sobre",
        "entre","quando","onde","qual","quais","quem","quanto","quanta","quantos","quantas","porque","porque","por que","pois","sim","nao","ser","estar","ter","haver","foi","sao",
        "era","sera","seria","tem","tinha","teria","ha","houve","esta","estao","estava","pode","podem","poderia","poderiam","podemos","posso","consegue","conseguem","conseguimos",
        "gostaria","gostariamos","saber","favor","gentileza","duvida","duvidas","pergunta","bom","boa","dia","tarde","noite","ola","oi","prezados","prezado","prezada","pessoal",
        "obrigado","obrigada","att","atenciosamente","cooperativa","coop","sicoob","time","equipe","gente","alguem","algum","alguma","fazer","feito","existe","existem","caso",
        "vez","vezes","agora","hoje","aqui","ali","la","forma","jeito","coisa","coisas","tudo","todos","todas","cada","outro","outra","mesmo","mesma","sobre","assim","bem",
        "precisa","precisamos","preciso","devemos","deve","devem","temos","tenho","vamos","vai","vao","tipo","algo","novo","nova","qualquer","favor","poderiam","seriam",
    };

    static readonly Regex RxPalavra = new(@"\p{L}[\p{L}\-]*", RegexOptions.Compiled);
    static readonly Regex RxData = new(@"\b\d{1,2}/\d{1,2}(/\d{2,4})?\b|\b\d{1,2}\s+de\s+(janeiro|fevereiro|marco|abril|maio|junho|julho|agosto|setembro|outubro|novembro|dezembro)\b|\bate\s+(amanha|hoje|segunda|terca|quarta|quinta|sexta|sabado|domingo|o\s+fim\s+do\s+dia|o\s+final\s+do\s+dia|o\s+fim\s+da\s+semana|o\s+final\s+da\s+semana|o\s+fim\s+do\s+mes)\b|\b(amanha|hoje)\b|\b\d+\s*(dias?|horas?|h\b|semanas?|meses|mes|minutos?)\b|\b(um|dois|duas|tres|quatro|cinco|seis|sete|oito|nove|dez|quinze|trinta)\s+(dias?|horas?|semanas?|meses|mes)\b|\b\d{1,2}h\d{0,2}\b|\b(segunda|terca|quarta|quinta|sexta)(-feira)?\b", RegexOptions.Compiled);
    static readonly Regex RxNumero = new(@"\d|r\$|%|\b(um|uma|dois|duas|tres|quatro|cinco|seis|sete|oito|nove|dez|cem|mil|metade|dobro|nenhum|nenhuma)\b", RegexOptions.Compiled);

    static List<(int ini, int fim)> Frases(string t)
    {
        var l = new List<(int, int)>();
        int ini = 0;
        for (int i = 0; i < t.Length; i++)
        {
            bool fimFrase = (t[i] is '.' or '!' or '?' || t[i] == '\n')
                && (i + 1 >= t.Length || char.IsWhiteSpace(t[i + 1]) || t[i] == '\n')
                && !(t[i] == '.' && i > 0 && char.IsDigit(t[i - 1]) && i + 1 < t.Length && char.IsDigit(t[i + 1]));
            if (fimFrase)
            {
                if (t[ini..(i + 1)].Trim().Length > 0) l.Add((ini, i + 1));
                ini = i + 1;
            }
        }
        if (ini < t.Length && t[ini..].Trim().Length > 0) l.Add((ini, t.Length));
        // tira espaços das pontas
        return l.Select(f =>
        {
            int a = f.Item1, b = f.Item2;
            while (a < b && char.IsWhiteSpace(t[a])) a++;
            while (b > a && char.IsWhiteSpace(t[b - 1])) b--;
            return (a, b);
        }).Where(f => f.b > f.a).ToList();
    }

    static int ContarPalavras(string s) => RxPalavra.Matches(s).Count + Regex.Matches(s, @"\b\d+\b").Count;

    static string Radical(string w) => w.Length <= 4 ? w : w.Length <= 6 ? w[..^1] : w[..Math.Max(5, w.Length - 3)];

    static List<string> Termos(string normTexto)
        => RxPalavra.Matches(normTexto).Select(m => m.Value).Where(w => w.Length >= 4 && !Vazias.Contains(w)).Distinct().ToList();

    static bool TemTermo(HashSet<string> radicaisResposta, IEnumerable<string> tokensResposta, string termo)
    {
        var r = Radical(termo);
        return radicaisResposta.Contains(r) || tokensResposta.Any(t => t.Length >= 4 && (t.StartsWith(r) || r.StartsWith(Radical(t))));
    }

    static string Trecho(string t, int ini, int fim, int max = 90)
    {
        var s = t[ini..fim].Replace('\n', ' ').Trim();
        return s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
    }

    // ======================== análise ========================

    public static Analise Analisar(string pergunta, string resposta)
    {
        var a = new Analise();
        pergunta ??= ""; resposta ??= "";
        var r = resposta;
        var rn = Norm(r);
        var frases = Frases(r);
        a.Palavras = ContarPalavras(r);
        a.Frases = frases.Count;
        a.MediaPalavrasFrase = frases.Count == 0 ? 0 : Math.Round((double)a.Palavras / frases.Count, 1);
        a.SegundosLeitura = (int)Math.Ceiling(a.Palavras / 3.3);
        if (r.Trim() == "") { a.Nota = 0; a.Veredito = "Cole a resposta para analisar"; a.VereditoClasse = "vazio"; return a; }

        var add = a.Achados;
        void Por(string cat, Gravidade g, string titulo, string expl, Regex rx, string? sugestao = null, Func<Match, bool>? filtro = null, int max = 6)
        {
            int n = 0;
            foreach (Match m in rx.Matches(rn))
            {
                if (filtro != null && !filtro(m)) continue;
                add.Add(new Achado(cat, g, titulo, expl, Trecho(r, m.Index, m.Index + m.Length), m.Index, m.Index + m.Length, sugestao));
                if (++n >= max) break;
            }
        }
        (int ini, int fim) FraseDe(int pos) { foreach (var f in frases) if (pos >= f.ini && pos < f.fim) return f; return (0, r.Length); }

        // ---------- 1. responde a pergunta ----------
        var tokensR = RxPalavra.Matches(rn).Select(m => m.Value).ToList();
        var radR = tokensR.Select(Radical).ToHashSet();
        bool adia = Regex.IsMatch(rn, @"\b(vamos|iremos|vou|irei|vai|vao)\s+(verificar|analisar|checar|averiguar|confirmar|levantar|consultar)|\bestamos\s+(verificando|analisando|checando|levantando|aguardando)|\bretornaremos|\bretorno\s+(em\s+breve|assim\s+que)|\bassim\s+que\s+(tivermos|houver|possivel)|\bem\s+analise\b|\bencaminhad[oa]s?\b|\babr(ir|a|am)\s+um\s+chamado");
        int? primeiraUtil = null;
        for (int i = 0; i < frases.Count; i++)
        {
            var fn = rn[frases[i].ini..frases[i].fim];
            if (!Regex.IsMatch(fn, @"^\s*(bom dia|boa tarde|boa noite|ola|oi|prezad[oa]s?|car[oa]s?|senhor|sr\.?|sra\.?|tudo bem)[^\p{L}]*([\p{L} ]{0,25})?[,.!]?\s*$")) { primeiraUtil = i; break; }
        }
        string inicioN = primeiraUtil is int pu ? rn[frases[pu].ini..frases[Math.Min(frases.Count - 1, pu + 1)].fim] : rn;

        foreach (var sp in SubPerguntas(pergunta))
        {
            var spn = Norm(sp);
            var tipo = Tipo(spn);
            var termos = Termos(spn).Take(8).ToList();
            var faltam = termos.Where(t => !TemTermo(radR, tokensR, t))
                .Select(t => { var m = Regex.Match(spn, $@"\b{Regex.Escape(t)}\b"); return m.Success ? sp.Substring(m.Index, m.Length) : t; }).ToList();
            double cob = termos.Count == 0 ? 1 : 1 - (double)faltam.Count / termos.Count;
            bool criterio; string falta;
            switch (tipo)
            {
                case "Sim ou não":
                    criterio = Regex.IsMatch(rn, @"\b(sim|nao|positivo|negativo|correto|confirmamos|confirmado|procede|exato|isso mesmo|e possivel|podem?|pode ser|existe|existem|ha\b|havera|tem como|conseguimos|consegue|e permitido|esta liberad|esta autorizad|nao esta)\b");
                    falta = "a pergunta pede um sim ou não claro";
                    if (criterio && !Regex.IsMatch(inicioN, @"\b(sim|nao|positivo|negativo|correto|confirmamos|procede|e possivel|pode|podem|existe|ha\b|conseguimos|e permitido)\b"))
                        add.Add(new Achado(Cat.Objetividade, Gravidade.Atencao, "O sim ou não não está no começo",
                            $"A pergunta “{Curto(sp)}” é de sim ou não. Quem lê no Teams quer a resposta logo na primeira frase.", null, -1, -1,
                            "Comece com “Sim, …” ou “Não, …” e explique em seguida."));
                    break;
                case "Prazo / data":
                    criterio = RxData.IsMatch(rn);
                    falta = "a pergunta pede prazo ou data e a resposta não traz uma data, dia ou prazo concreto";
                    break;
                case "Valor / quantidade":
                    criterio = RxNumero.IsMatch(rn);
                    falta = "a pergunta pede um valor ou quantidade e a resposta não traz número";
                    break;
                case "Responsável":
                    criterio = Regex.IsMatch(rn, @"\b(setor|area|equipe|time|gerencia|central|departamento|responsavel|responsaveis|coordenac|supervis|analista|gestor|diretoria|superintend|nossa equipe|a cooperativa|o associado|cabe a|fica com|fica a cargo)") || Regex.IsMatch(r, @"(?<![.!?]\s)(?<!^)\b[A-ZÁÉÍÓÚÂÊÔÃÕ][a-záéíóúâêôãõç]{2,}\s+[A-ZÁÉÍÓÚÂÊÔÃÕ][a-záéíóúâêôãõç]{2,}");
                    falta = "a pergunta pede quem é o responsável e a resposta não cita pessoa, setor ou área";
                    break;
                case "Procedimento":
                    criterio = Regex.IsMatch(r, @"(?m)^\s*(\d+[.)-]|[-•*])\s+") || Regex.IsMatch(rn, @"\b(primeiro|primeiramente|depois|em seguida|apos|por fim|por ultimo|acesse|acessar|clique|clicar|selecione|selecionar|informe|informar|envie|enviar|preencha|preencher|abra|abrir|anexe|anexar|solicite|solicitar|registre|registrar|basta)\b");
                    falta = "a pergunta pede como fazer e a resposta não traz o passo a passo";
                    break;
                case "Motivo":
                    criterio = Regex.IsMatch(rn, @"\b(porque|pois|devido|em razao|em funcao|por conta|por causa|motivo|causa|uma vez que|ja que|visto que|isso ocorre|isso ocorreu|ocorreu|aconteceu|decorre|em decorrencia)\b");
                    falta = "a pergunta pede o motivo e a resposta não explica o porquê";
                    break;
                case "Local / sistema":
                    criterio = Regex.IsMatch(rn, @"\b(sistema|tela|menu|link|http|portal|aba|modulo|site|aplicativo|app|plataforma|sisbr|caminho|opcao|pasta|campo|relatorio|planilha)\b|>");
                    falta = "a pergunta pede onde e a resposta não indica sistema, tela ou caminho";
                    break;
                default:
                    criterio = cob >= .6;
                    falta = "a resposta retoma pouco do que foi perguntado";
                    break;
            }
            StatusPergunta st; string motivo;
            if (criterio && cob >= .5) { st = StatusPergunta.Atendida; motivo = "respondida"; }
            else if (criterio) { st = StatusPergunta.Parcial; motivo = "o tipo de resposta está lá, mas ela não retoma pontos da pergunta"; }
            else if (cob >= .5 || (tipo == "Aberta" && cob >= .3)) { st = StatusPergunta.Parcial; motivo = falta; }
            else { st = StatusPergunta.NaoAtendida; motivo = falta; }
            if (st != StatusPergunta.Atendida && adia) motivo += "; a resposta adia (“vamos verificar”, “retornaremos”…) sem responder agora";
            a.Perguntas.Add(new SubPergunta(sp, tipo, st, motivo, st == StatusPergunta.Atendida ? new() : faltam));
        }

        // ---------- 2. objetividade ----------
        Por(Cat.Objetividade, Gravidade.Atencao, "Frase de enchimento", "Não acrescenta informação e atrasa a resposta.",
            new Regex(@"\b(gostariamos de (informar|esclarecer|comunicar)( que)?|vimos por meio desta|venho por meio desta|informamos que|cabe (ressaltar|destacar|informar)( que)?|vale (ressaltar|lembrar|destacar)( que)?|e importante (ressaltar|destacar|lembrar)( que)?|de certa forma|de modo geral|de maneira geral|basicamente|na verdade|ou seja|de fato|sendo assim|diante do exposto|neste sentido|nesse sentido|em relacao ao questionamento|em resposta ao seu questionamento|referente a sua (duvida|pergunta|solicitacao))\b"),
            "Corte e vá direto ao ponto.");
        foreach (var f in frases)
        {
            int n = ContarPalavras(r[f.ini..f.fim]);
            if (n > 35) add.Add(new Achado(Cat.Objetividade, n > 50 ? Gravidade.Problema : Gravidade.Atencao, $"Frase longa ({n} palavras)",
                "Frases longas no Teams cansam e abrem espaço para interpretações diferentes.", Trecho(r, f.ini, f.fim), f.ini, f.fim, "Quebre em duas ou três frases curtas (até 25 palavras)."));
        }
        int palP = Math.Max(1, ContarPalavras(pergunta));
        if (a.Palavras > 160 && a.Palavras > palP * 6)
            add.Add(new Achado(Cat.Objetividade, Gravidade.Atencao, $"Resposta longa ({a.Palavras} palavras)",
                $"A pergunta tem {palP} palavras. Respostas longas escondem a informação principal.", null, -1, -1, "Deixe a resposta principal nas duas primeiras linhas e o detalhe depois, em tópicos."));
        foreach (var g in tokensR.Where(t => t.Length >= 5 && !Vazias.Contains(t)).GroupBy(t => t).Where(g => g.Count() >= 4).Take(3))
            add.Add(new Achado(Cat.Objetividade, Gravidade.Leve, $"“{g.Key}” repetida {g.Count()} vezes", "Repetição deixa o texto cansativo.", null, -1, -1, "Troque algumas por sinônimos ou junte as frases."));
        var vistas = new HashSet<string>();
        foreach (var f in frases)
        {
            var k = Regex.Replace(rn[f.ini..f.fim], @"[^\p{L}\d]+", " ").Trim();
            if (k.Length > 25 && !vistas.Add(k))
                add.Add(new Achado(Cat.Objetividade, Gravidade.Atencao, "Frase repetida", "A mesma frase aparece duas vezes.", Trecho(r, f.ini, f.fim), f.ini, f.fim, "Apague a repetição."));
        }

        // ---------- 3. discordância ----------
        Por(Cat.Discordancia, Gravidade.Problema, "Discorda da cooperativa de forma direta",
            "Com a 3069, discordar de forma seca soa como confronto. A discordância precisa vir com cuidado e com o fato que a sustenta.",
            new Regex(@"\b(nao procede|discordamos|nao concordamos|nao e bem assim|ao contrario|equivocad[oa]s?|incorret[oa]s?|errad[oa]s?|nao e verdade|nao foi isso|voces se enganaram|engano de voces|nao faz sentido)\b"),
            "Prefira: “Verificamos que a situação ocorreu de forma um pouco diferente: …” e traga o fato.");
        var fs = frases.Select(f => rn[f.ini..f.fim]).ToList();
        bool comecaSim = fs.Any(f => Regex.IsMatch(f, @"^\W*sim\b")), comecaNao = fs.Any(f => Regex.IsMatch(f, @"^\W*nao\b"));
        if (comecaSim && comecaNao)
            add.Add(new Achado(Cat.Discordancia, Gravidade.Problema, "A resposta diz “sim” e “não”", "Uma frase começa com “Sim” e outra com “Não”. Quem lê pode entender o contrário do que queremos.", null, -1, -1, "Deixe uma resposta só e explique as exceções numa frase separada."));
        foreach (var par in new[] { ("e possivel", "nao e possivel"), ("pode", "nao pode"), ("existe", "nao existe"), ("ha", "nao ha"), ("esta liberad", "nao esta liberad"), ("foi realizad", "nao foi realizad"), ("e permitido", "nao e permitido"), ("sempre", "nunca") })
        {
            var pos = Regex.Matches(rn, $@"(?<!nao )\b{Regex.Escape(par.Item1)}\b").Count;
            if (par.Item1 == "sempre") pos = Regex.Matches(rn, @"\bsempre\b").Count;
            var neg = par.Item1 == "sempre" ? Regex.Matches(rn, @"\b(nunca|jamais|as vezes)\b").Count : Regex.Matches(rn, $@"\b{Regex.Escape(par.Item2)}\b").Count;
            if (pos > 0 && neg > 0)
            {
                var m = Regex.Match(rn, par.Item1 == "sempre" ? @"\b(nunca|jamais|as vezes)\b" : $@"\b{Regex.Escape(par.Item2)}\b");
                add.Add(new Achado(Cat.Discordancia, Gravidade.Problema, "Afirmações que se contradizem",
                    $"A resposta usa “{par.Item1.Trim()}” e também “{(par.Item1 == "sempre" ? m.Value : par.Item2.Trim())}”. Pode parecer que a resposta muda no meio.",
                    Trecho(r, m.Index, m.Index + m.Length), m.Index, m.Index + m.Length, "Separe claramente a regra da exceção: “Em regra, sim. A exceção é …”."));
            }
        }
        var datas = Regex.Matches(rn, @"\b\d{1,2}/\d{1,2}(/\d{2,4})?\b").Select(m => m.Value).Distinct().ToList();
        if (datas.Count >= 2)
            add.Add(new Achado(Cat.Discordancia, Gravidade.Atencao, $"Datas diferentes ({string.Join(", ", datas.Take(4))})", "Mais de uma data na resposta pode gerar dúvida sobre qual vale.", null, -1, -1, "Deixe claro o que acontece em cada data."));
        var prazos = Regex.Matches(rn, @"\b(\d+)\s*(dias?|horas?)\b").Select(m => m.Groups[1].Value + " " + (m.Groups[2].Value.StartsWith("dia") ? "dias" : "horas")).Distinct().ToList();
        if (prazos.Count >= 2)
            add.Add(new Achado(Cat.Discordancia, Gravidade.Atencao, $"Prazos diferentes ({string.Join(", ", prazos.Take(4))})", "Dois prazos diferentes na mesma resposta são um ponto fácil de cobrança.", null, -1, -1, "Confirme se os dois estão certos e diga a que cada um se refere."));
        Por(Cat.Discordancia, Gravidade.Atencao, "“Sim, mas…” enfraquece a resposta",
            "Uma ressalva logo depois da resposta muda o sentido para quem lê rápido.",
            new Regex(@"^\W*(sim|e possivel|pode|podem|conseguimos)\b[^.!?\n]{0,80}\b(porem|mas|entretanto|contudo|no entanto|todavia)\b", RegexOptions.Multiline),
            "Separe: “Sim, é possível.” Na frase seguinte: “Para isso, é necessário …”.", max: 2);

        // ---------- 4. pontos que podem ser contestados ----------
        Por(Cat.Contestavel, Gravidade.Atencao, "Termo vago", "Sem número ou data, abre espaço para cobrança (“quando é em breve?”).",
            new Regex(@"\b(em breve|assim que possivel|o quanto antes|o mais rapido possivel|nos proximos dias|proximamente|alguns|algumas|diversos|diversas|varios|varias|muitos|muitas|pode ser que|talvez|possivelmente|provavelmente|a principio|em tese|normalmente|geralmente|na maioria das vezes|eventualmente|aproximadamente|cerca de|mais ou menos|de vez em quando|pouco tempo|algum tempo|em algum momento)\b"),
            "Troque por um número, uma data ou um nome.");
        Por(Cat.Contestavel, Gravidade.Atencao, "Transmite incerteza", "Para a 3069, a resposta precisa ser confirmada antes de ir.",
            new Regex(@"\b(acreditamos|achamos|acho|parece|aparentemente|ao que tudo indica|supostamente|pelo que vimos|pelo que entendi|pelo que entendemos|imagino|creio|deve ser|deveria ser)\b"),
            "Confirme a informação e afirme: “Confirmamos que …”.");
        Por(Cat.Contestavel, Gravidade.Atencao, "Palavra absoluta", "Basta uma exceção para a resposta ser contestada.",
            new Regex(@"\b(sempre|nunca|jamais|todos|todas|nenhum|nenhuma|garantimos|garantido|garantida|com certeza|certamente|100%|impossivel|totalmente|definitivamente|em hipotese alguma|qualquer situacao)\b"),
            "Use “em regra”, “nos casos analisados” ou cite o critério.", max: 4);
        foreach (Match m in Regex.Matches(rn, @"\b(vamos|iremos|vou|irei|vai|vao)\s+(verificar|analisar|checar|averiguar|confirmar|retornar|providenciar|ajustar|corrigir|resolver|enviar|encaminhar)\b|\bretornaremos\b|\bestamos\s+(verificando|analisando|tratando|providenciando)\b|\bsera(o)?\s+(verificad|analisad|resolvid|ajustad|corrigid|enviad|providenciad|tratad|feit)\w*"))
        {
            var f = FraseDe(m.Index);
            var fn = rn[f.ini..f.fim];
            bool temData = RxData.IsMatch(fn);
            bool temQuem = Regex.IsMatch(fn, @"\b(pel[oa]s?|nossa equipe|equipe|setor|area|responsavel)\b");
            if (temData && (temQuem || !m.Value.StartsWith("ser"))) continue;
            add.Add(new Achado(Cat.Contestavel, Gravidade.Problema, temData ? "Compromisso sem responsável" : "Compromisso sem data",
                temData ? "A ação tem prazo, mas não diz quem fará." : "Promessa sem prazo é o ponto mais fácil de ser cobrado: “quando?”.",
                Trecho(r, m.Index, m.Index + m.Length), m.Index, m.Index + m.Length,
                temData ? "Diga quem: “Nossa equipe de … fará até …”." : "Informe a data: “… e retornaremos até quinta-feira, 20/11, às 17h.”"));
        }
        Por(Cat.Contestavel, Gravidade.Atencao, "Regra citada sem identificação", "Se citar norma ou regra, a cooperativa pode pedir qual é.",
            new Regex(@"\b(conforme|de acordo com|segundo|previsto n[oa])\s+(o |a |os |as |nosso |nossa )?(normativo|norma|regulamento|manual|politica|procedimento|regra|circular|resolucao|instrucao|regulamentacao|legislacao|contrato)s?\b(?!\s*(n[oº°.]?\s*)?\d|\s+(numero|item|capitulo|art|da circular|de numero))"),
            "Cite o número, o item ou envie o link do documento.");
        Por(Cat.Contestavel, Gravidade.Atencao, "Dias úteis ou corridos?", "Prazo em dias sem dizer se são úteis ou corridos gera discussão depois.",
            new Regex(@"\b\d+\s*dias?\b(?!\s+(uteis|util|corridos?))"), "Escreva “5 dias úteis” ou “5 dias corridos”.", max: 3);
        Por(Cat.Contestavel, Gravidade.Problema, "Transfere responsabilidade ou culpa", "Com a 3069 isso soa como acusação e costuma gerar contestação.",
            new Regex(@"\b(cabe a cooperativa|e responsabilidade d[ae] (cooperativa|voces)|nao e (nossa )?responsabilidade|nao e conosco|nao e com a gente|voces (deveriam|deviam|erraram|esqueceram|nao enviaram|nao fizeram)|erro d[ae] (cooperativa|voces)|falha d[ae] (cooperativa|voces)|por culpa|culpa d[ae])\b"),
            "Descreva o fato sem atribuir culpa: “Identificamos que o documento não foi anexado. Para seguirmos, pedimos a gentileza de enviá-lo.”");
        Por(Cat.Contestavel, Gravidade.Atencao, "“Depende” sem dizer de quê", "Abre a pergunta seguinte: depende de quê?",
            new Regex(@"\b(depende|dependendo|vai depender)\b(?![^.\n]{0,60}\b(se|de|do|da|dos|das)\b)"), "Explique as condições: “Depende do tipo de conta: se for …, …; se for …, …”.");
        var comunsSigla = new HashSet<string> { "CPF", "CNPJ", "PIX", "TED", "DOC", "SLA", "PDF", "OK", "RH", "TI", "PJ", "PF", "CEP", "ID", "URL", "LGPD", "BACEN", "SICOOB", "CCS", "QR", "SMS", "API", "FAQ", "PA", "PAS", "UF", "DDA", "IOF", "IR" };
        var siglasPergunta = Regex.Matches(pergunta, @"\b[A-Z]{2,6}\b").Select(m => m.Value).ToHashSet();
        int ns = 0;
        foreach (Match m in Regex.Matches(r, @"\b[A-Z]{2,6}\b"))
        {
            if (comunsSigla.Contains(m.Value) || siglasPergunta.Contains(m.Value) || Regex.IsMatch(m.Value, @"^(OBS|ATT|ATTE)$")) continue;
            if (r.IndexOf(m.Value, StringComparison.Ordinal) != m.Index) continue;   // só a primeira vez
            if (Regex.IsMatch(r[Math.Min(r.Length, m.Index + m.Length)..Math.Min(r.Length, m.Index + m.Length + 3)], @"^\s*\(")) continue; // já explicada
            add.Add(new Achado(Cat.Contestavel, Gravidade.Leve, $"Sigla “{m.Value}” sem explicação", "Quem lê pode não conhecer a sigla.", m.Value, m.Index, m.Index + m.Length, $"Na primeira vez, escreva por extenso: “… ({m.Value})”."));
            if (++ns >= 3) break;
        }
        if (primeiraUtil is int pu2 && Regex.IsMatch(rn[frases[pu2].ini..frases[pu2].fim], @"^\W*(isso|esse|essa|este|esta|o mesmo|a mesma|ele|ela)\b"))
        {
            var f = frases[pu2];
            add.Add(new Achado(Cat.Contestavel, Gravidade.Leve, "Começa com “isso/esse…”", "No Teams a mensagem pode ser lida fora de contexto. A que “isso” se refere?", Trecho(r, f.ini, f.fim), f.ini, Math.Min(f.fim, f.ini + 12), "Retome o assunto: “Sobre a liberação do limite, …”."));
        }

        // ---------- 5. tom e formalidade ----------
        var informais = new (string rx, string troca)[]
        {
            (@"\bvc(s)?\b", "você(s)"), (@"\bpq\b", "porque / por que"), (@"\btb(m)?\b", "também"), (@"\bblz\b", "certo"), (@"\bbeleza\b", "certo / combinado"),
            (@"\bok\b", "de acordo / certo"), (@"\bgalera\b", "Prezados"), (@"^\W*((oi|ola)\W+)?pessoal\b", "Prezados"), (@"\b(me chama|me chame|chama a gente|qualquer coisa)\b", "Ficamos à disposição"), (@"\bentao,", "(retire)"), (@"\btipo\b(?! de)", "(retire)"), (@"\bne\b\??", "(retire)"),
            (@"\bta\b", "está"), (@"\bpra\b", "para"), (@"\bpro\b", "para o"), (@"\bpros\b", "para os"), (@"\ba gente\b", "nós / nossa equipe"),
            (@"\boi\b", "Olá / Prezados"), (@"\brs+\b", "(retire)"), (@"\bk{3,}\b", "(retire)"), (@"\b(ha){2,}\b", "(retire)"), 
            (@"\bfica tranquil[oa]\b", "pode ficar seguro(a)"), (@"\bsem problemas?\b", "certamente / sem dúvida"), (@"\bvaleu\b", "agradecemos"), (@"\bobg\b", "obrigado(a)"),
            (@"\bmsg\b", "mensagem"), (@"\bqdo\b", "quando"), (@"\bhj\b", "hoje"), (@"\bp/", "para"), (@"\bc/", "com"), (@"\bq\b", "que"),
        };
        foreach (var (rx, troca) in informais)
        {
            foreach (Match m in Regex.Matches(rn, rx).Cast<Match>().Take(2))
                add.Add(new Achado(Cat.Tom, Gravidade.Problema, $"Linguagem informal: “{r.Substring(m.Index, m.Length).Trim()}”", "Para a 3069 a linguagem precisa ser formal, como numa comunicação oficial.",
                    r.Substring(m.Index, m.Length), m.Index, m.Index + m.Length, $"Troque por: {troca}."));
        }
        foreach (Match m in Regex.Matches(r, @"\p{Cs}{2}|[☀-➿]").Cast<Match>().Take(3))
            add.Add(new Achado(Cat.Tom, Gravidade.Problema, "Emoji", "Emojis tiram a formalidade da resposta.", m.Value, m.Index, m.Index + m.Length, "Retire o emoji."));
        foreach (Match m in Regex.Matches(r, @"[!?]{2,}").Cast<Match>().Take(2))
            add.Add(new Achado(Cat.Tom, Gravidade.Problema, "Pontuação repetida", "“!!” ou “??” passa impaciência.", m.Value, m.Index, m.Index + m.Length, "Use um único ponto."));
        foreach (Match m in Regex.Matches(r, @"\b[A-ZÁÉÍÓÚÂÊÔÃÕÇ]{5,}\b").Cast<Match>().Where(m => !comunsSigla.Contains(m.Value)).Take(2))
            add.Add(new Achado(Cat.Tom, Gravidade.Atencao, "Palavra em maiúsculas", "Em mensagens, letras maiúsculas soam como grito.", m.Value, m.Index, m.Index + m.Length, "Escreva em minúsculas; para destacar, use negrito no Teams."));
        Por(Cat.Tom, Gravidade.Problema, "Soa impaciente ou irônico", "Expressões como “como já informado” passam irritação, mesmo sem querer.",
            new Regex(@"\b(como ja (foi )?(dito|informado|explicado|mencionado|falado|orientado)|conforme (ja )?(informado|explicado|mencionado|orientado) anteriormente|conforme ja (informado|explicado|mencionado|orientado)|(novamente|mais uma vez),? (informamos|reforcamos|explicamos|esclarecemos|orientamos)|como (voces )?(ja )?sabem|obviamente|claramente|evidentemente|e claro que|simplesmente|basta apenas|e so)\b"),
            "Retire e explique de novo com paciência, como se fosse a primeira vez.");
        Por(Cat.Tom, Gravidade.Atencao, "Tom de ordem", "Para a 3069, peça em vez de mandar.",
            new Regex(@"\b(voces (devem|precisam|tem que|terao que|deverao)|e obrigatorio|e necessario que voces|favor (enviar|verificar|fazer|encaminhar|conferir|corrigir)|tem que|devem enviar|precisam enviar)\b"),
            "Use: “Pedimos, por gentileza, que …” ou “Solicitamos, por gentileza, …”.");
        Por(Cat.Tom, Gravidade.Atencao, "Primeira pessoa do singular", "A resposta fala em nome da instituição; “eu” deixa pessoal.",
            new Regex(@"\b(eu|acho|vou|meu|minha|me parece|irei)\b"), "Use “nós” / “nossa equipe”.", max: 3);
        bool negativa = Regex.IsMatch(rn, @"\b(nao (e possivel|podemos|conseguimos|sera possivel|temos como|ha como)|impossivel|nao atendemos|nao fazemos)\b");
        bool alternativa = Regex.IsMatch(rn, @"\b(como alternativa|alternativa|o que podemos|(?<!nao )podemos|sugerimos|sugestao|uma opcao|outra opcao|o que e possivel|(?<!nao )e possivel|recomendamos)\b");
        if (negativa && !alternativa)
            add.Add(new Achado(Cat.Tom, Gravidade.Atencao, "Negativa sem alternativa", "Um “não” seco para a 3069 deixa a conversa sem saída.", null, -1, -1, "Depois do não, ofereça o que é possível: “Como alternativa, podemos …”."));
        bool saudacao = Regex.IsMatch(rn, @"^\W*(bom dia|boa tarde|boa noite|ola|prezad[oa]s?|car[oa]s?|senhor|senhora|sr\.?|sra\.?)\b");
        if (!saudacao)
            add.Add(new Achado(Cat.Tom, Gravidade.Atencao, "Sem saudação", "Começar direto, sem cumprimentar, soa seco.", null, -1, -1, "Comece com: “Prezados, boa tarde.” ou “Olá, [nome], boa tarde.”"));
        bool fechamento = Regex.IsMatch(rn, @"\b(atenciosamente|cordialmente|a disposicao|seguimos a disposicao|ficamos a disposicao|permanecemos a disposicao|qualquer duvida|estamos a disposicao|agradecemos|obrigad[oa])\b");
        if (!fechamento)
            add.Add(new Achado(Cat.Tom, Gravidade.Atencao, "Sem fechamento cordial", "Terminar sem se colocar à disposição deixa a resposta fria.", null, -1, -1, "Termine com: “Seguimos à disposição.” ou “Ficamos à disposição para qualquer dúvida.”"));
        if (a.Palavras > 40 && !Regex.IsMatch(rn, @"\b(por gentileza|por favor|agradecemos|obrigad[oa]|gentileza|contamos com)\b"))
            add.Add(new Achado(Cat.Tom, Gravidade.Leve, "Pouca cordialidade", "Nenhuma palavra de cortesia na resposta.", null, -1, -1, "Inclua um “agradecemos o contato” ou “por gentileza”."));

        // ---------- 6. escrita ----------
        var erros = new (string rx, string troca)[]
        {
            (@"\bà partir\b", "a partir"), (@"\ba nivel de\b", "em relação a / quanto a"), (@"\bseje\b", "seja"), (@"\bmenas\b", "menos"),
            (@"\bhouveram\b", "houve"), (@"\bfazem (\d+|alguns|varios) (dias|meses|anos|semanas)\b", "faz …"), (@"\bconcerteza\b", "com certeza"),
            (@"\bderrepente\b", "de repente"), (@"\bencima\b", "em cima"), (@"\bporisso\b", "por isso"), (@"\bagente\b", "a gente → nós"),
            (@"\b(o|a) mesm[oa] (foi|esta|sera|e|encontra|possui|tem)\b", "use o nome da coisa ou “ele/ela”"),
            (@"\bem anexo segue\b", "segue anexo / anexamos"), (@"\bvoces podem estar\b", "vocês podem (evite gerundismo)"), (@"\bvamos estar \w+ndo\b", "vamos + verbo (evite gerundismo)"),
            (@"\biremos estar\b", "iremos + verbo (evite gerundismo)"), (@"\bpodemos estar \w+ndo\b", "podemos + verbo (evite gerundismo)"),
        };
        foreach (var (rx, troca) in erros)
        {
            if (troca == "") continue;
            foreach (Match m in Regex.Matches(rn, rx).Cast<Match>().Take(2))
                add.Add(new Achado(Cat.Escrita, Gravidade.Atencao, $"Correção: “{r.Substring(m.Index, m.Length)}”", "Erro de português ou vício de linguagem.", r.Substring(m.Index, m.Length), m.Index, m.Index + m.Length, $"Use: {troca}."));
        }
        var semAcento = new Dictionary<string, string>
        {
            ["nao"] = "não", ["voce"] = "você", ["voces"] = "vocês", ["tambem"] = "também", ["ja"] = "já", ["ate"] = "até", ["entao"] = "então", ["possivel"] = "possível",
            ["informacao"] = "informação", ["informacoes"] = "informações", ["solicitacao"] = "solicitação", ["numero"] = "número", ["alem"] = "além", ["apos"] = "após",
            ["disponivel"] = "disponível", ["servico"] = "serviço", ["servicos"] = "serviços", ["credito"] = "crédito", ["analise"] = "análise", ["codigo"] = "código",
            ["periodo"] = "período", ["proximo"] = "próximo", ["proxima"] = "próxima", ["necessario"] = "necessário", ["responsavel"] = "responsável", ["duvida"] = "dúvida",
            ["duvidas"] = "dúvidas", ["sera"] = "será", ["atencao"] = "atenção", ["previsao"] = "previsão", ["sao"] = "são", ["estao"] = "estão", ["util"] = "útil", ["uteis"] = "úteis",
        };
        int na = 0;
        foreach (Match m in RxPalavra.Matches(r))
        {
            if (!semAcento.TryGetValue(m.Value.ToLowerInvariant(), out var certo) || m.Value.Any(ch => ch > 127)) continue;
            add.Add(new Achado(Cat.Escrita, Gravidade.Atencao, $"Falta acento: “{m.Value}”", "Palavra sem acento.", m.Value, m.Index, m.Index + m.Length, $"Escreva “{certo}”."));
            if (++na >= 5) break;
        }
        foreach (Match m in Regex.Matches(r, @"\b(\p{L}+)\s+\1\b", RegexOptions.IgnoreCase).Cast<Match>().Take(2))
            add.Add(new Achado(Cat.Escrita, Gravidade.Atencao, $"Palavra repetida: “{m.Value}”", "A mesma palavra duas vezes seguidas.", m.Value, m.Index, m.Index + m.Length, "Apague uma."));
        foreach (Match m in Regex.Matches(r, @"[ \t]{2,}").Cast<Match>().Take(1))
            add.Add(new Achado(Cat.Escrita, Gravidade.Leve, "Espaço duplo", "Dois espaços seguidos.", null, m.Index, m.Index + m.Length, "Deixe um espaço só."));
        foreach (Match m in Regex.Matches(r, @"\s+[,.;:!?](?!\d)").Cast<Match>().Take(2))
            add.Add(new Achado(Cat.Escrita, Gravidade.Leve, "Espaço antes da pontuação", "A pontuação vem colada na palavra.", null, m.Index, m.Index + m.Length, "Tire o espaço antes da vírgula ou do ponto."));
        foreach (Match m in Regex.Matches(r, @"[,;](?=\p{L})").Cast<Match>().Take(2))
            add.Add(new Achado(Cat.Escrita, Gravidade.Leve, "Falta espaço depois da vírgula", "Depois de vírgula vem um espaço.", null, m.Index, m.Index + 1, "Coloque um espaço depois da vírgula."));
        foreach (var f in frases)
        {
            var c = r[f.ini];
            if (char.IsLetter(c) && char.IsLower(c))
            {
                add.Add(new Achado(Cat.Escrita, Gravidade.Leve, "Frase começa com minúscula", "Início de frase vai com letra maiúscula.", Trecho(r, f.ini, f.fim, 40), f.ini, f.ini + 1, "Use maiúscula no início da frase."));
                break;
            }
        }
        var ult = r.TrimEnd();
        if (ult.Length > 0 && char.IsLetterOrDigit(ult[^1]))
            add.Add(new Achado(Cat.Escrita, Gravidade.Leve, "Falta o ponto final", "A resposta termina sem pontuação.", null, -1, -1, "Termine com ponto final."));

        // tira repetidos: mesmo trecho (ou sobreposto) na mesma categoria
        var limpos = new List<Achado>();
        foreach (var x in a.Achados)
        {
            if (x.Ini >= 0 && limpos.Any(y => y.Categoria == x.Categoria && y.Ini >= 0 && x.Ini < y.Fim && y.Ini < x.Fim)) continue;
            limpos.Add(x);
        }
        a.Achados.Clear(); a.Achados.AddRange(limpos);

        // ---------- nota ----------
        int nota = 100;
        foreach (var p in a.Perguntas) nota -= p.Status switch { StatusPergunta.NaoAtendida => 28, StatusPergunta.Parcial => 12, _ => 0 };
        foreach (var x in a.Achados) nota -= x.Grav switch { Gravidade.Problema => 10, Gravidade.Atencao => 4, _ => 1 };
        a.Nota = Math.Clamp(nota, 0, 100);
        bool todas = a.Perguntas.All(p => p.Status == StatusPergunta.Atendida);
        int problemas = a.Achados.Count(x => x.Grav == Gravidade.Problema);
        if (a.Nota >= 85 && todas && problemas == 0) { a.Veredito = "Pronta para enviar"; a.VereditoClasse = "ok"; }
        else if (a.Nota >= 60 && a.Perguntas.All(p => p.Status != StatusPergunta.NaoAtendida)) { a.Veredito = "Ajuste antes de enviar"; a.VereditoClasse = "ajuste"; }
        else { a.Veredito = "Não enviar ainda"; a.VereditoClasse = "nao"; }
        return a;
    }

    static string Curto(string s) { s = s.Replace('\n', ' ').Trim(); return s.Length <= 70 ? s : s[..69] + "…"; }

    /// <summary>Separa as perguntas do texto (por “?”, numeração ou tópicos).</summary>
    public static List<string> SubPerguntas(string texto)
    {
        var l = new List<string>();
        texto = (texto ?? "").Trim();
        if (texto == "") return l;
        var partes = Regex.Split(texto, @"(?<=\?)|\n\s*(?=(\d+[.)-]|[-•*])\s+)").Select(p => p.Trim()).Where(p => p != "").ToList();
        foreach (var p in partes)
        {
            var s = Regex.Replace(p, @"^\s*(\d+[.)-]|[-•*])\s+", "");
            // tira saudações do começo do bloco
            s = Regex.Replace(s, @"^(bom dia|boa tarde|boa noite|ol[aá]|oi|prezad[oa]s?|pessoal)[^\p{L}]*(\p{L}+[,!.]\s*)?", "", RegexOptions.IgnoreCase).Trim();
            if (Termos(Norm(s)).Count == 0 && !s.Contains('?')) continue;
            if (s.Length < 6) continue;
            // trecho sem "?" junto de outro com "?": só entra se parece pergunta
            if (!s.Contains('?') && partes.Any(x => x.Contains('?')) && !Regex.IsMatch(Norm(s), @"\b(gostaria|preciso|precisamos|poderiam|podem|solicito|solicitamos|qual|quais|como|quando|onde|quem|por que|quanto)\b")) continue;
            l.Add(s);
        }
        if (l.Count == 0) l.Add(texto);
        return l.Take(8).ToList();
    }

    public static string Tipo(string n)
    {
        n = Regex.Replace(n, @"^\W*(bom dia|boa tarde|boa noite|ola|oi|prezad[oa]s?|pessoal)[^\p{L}]*", "");
        n = Regex.Replace(n, @"^\W*(e tambem|e|mas|alem disso|tambem|outra duvida|outra pergunta)[,:]?\s+", "");
        n = " " + n.Trim() + " ";
        bool simNao = Regex.IsMatch(n, @"^\W*(e possivel|sera possivel|seria possivel|pode|podem|posso|podemos|poderia|poderiam|tem como|teria como|existe|existem|havera|procede|esta correto|e correto|confere)\b");
        if (simNao) return "Sim ou não";
        if (Regex.IsMatch(n, @"\b(quanto tempo|em quanto tempo|quando|prazo|ate quando|ate que dia|que dia|qual (a )?data|previsao|que horas|qual horario)\b")) return "Prazo / data";
        if (Regex.IsMatch(n, @"\b(quanto|quantos|quantas|valor|custo|taxa|tarifa|percentual|porcentagem|qual o limite|limite de)\b")) return "Valor / quantidade";
        if (Regex.IsMatch(n, @"\b(quem|qual setor|qual area|qual equipe|a quem|responsavel|responsaveis)\b")) return "Responsável";
        if (Regex.IsMatch(n, @"\b(por que|porque|por qual motivo|qual (o|foi o) motivo|motivo|qual a razao|razao)\b")) return "Motivo";
        if (Regex.IsMatch(n, @"\b(como|de que forma|de que maneira|qual (o )?procedimento|passo a passo|o que (devo|devemos|preciso|precisamos|temos que) fazer|o que fazer)\b")) return "Procedimento";
        if (Regex.IsMatch(n, @"\b(onde|em qual sistema|qual sistema|em que tela|qual tela|qual (o )?link|em qual menu|qual menu|qual caminho)\b")) return "Local / sistema";
        if (Regex.IsMatch(n, @"^\W*(e possivel|sera possivel|seria possivel|pode|podem|posso|podemos|poderia|poderiam|tem como|teria como|existe|existem|ha |havera|vai|vao|sera|seria|foi|ja |esta |estao|e |sao |devemos|deve|devem|precisa|precisamos|preciso|consegue|conseguem|conseguimos|voces|houve|teria|tem |temos|procede|esta correto|e correto|confere|o (senhor|sr)|a (senhora|sra))") ) return "Sim ou não";
        return "Aberta";
    }
}
