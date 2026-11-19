using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace QSmart.Core.Relacionamento;

// =====================================================================================
//  Análise da resposta pela IA (Groq, modelo aberto gpt-oss). Aqui fica só o que não depende do navegador:
//  o pedido (instruções + formato JSON), a máscara de dados pessoais e a leitura do resultado.
// =====================================================================================

public sealed class ResultadoIA
{
    public Analise Analise { get; init; } = new();
    public string Resumo { get; init; } = "";
    public string RespostaSugerida { get; init; } = "";
    public string Reacao { get; init; } = "";
    public List<string> PontosFortes { get; init; } = new();
    public string Modelo { get; init; } = "";
}

public static class AnaliseIA
{
    public const string ModeloPadrao = "openai/gpt-oss-120b";
    public const string Url = "https://api.groq.com/openai/v1/chat/completions";

    const string Instrucoes = """
Você é o revisor sênior de comunicação institucional de uma central de serviços do Sicoob.
A cooperativa 3069 é um cliente de altíssima importância: responder a ela é como responder ao Presidente da República.
Nossa equipe escreveu uma RESPOSTA para a PERGUNTA que a 3069 enviou pelo Microsoft Teams. Antes de enviar, você revisa a resposta com olhar crítico e analítico.

Avalie, sempre diante da pergunta:
1. responde: cada pergunta contida na mensagem foi respondida de forma completa e direta? Separe as perguntas (pode haver mais de uma, inclusive implícitas, como um pedido de documento ou uma cobrança). Para cada uma: atendida, parcial ou nao_atendida, com o motivo.
2. objetividade: a resposta principal está logo no início? Há rodeios, frases longas, repetição, informação que não foi pedida?
3. discordancia: a resposta discorda da cooperativa de forma seca ou se contradiz (sim e não, datas/valores/números diferentes dos da pergunta, regra e exceção misturadas)?
4. contestavel: o que a 3069 pode questionar porque não está bem explicado: termos vagos, promessas sem data ou sem responsável, “não” sem motivo, regra/normativo sem identificação, palavras absolutas, prazos sem dizer se são dias úteis, culpa atribuída à cooperativa, pendência sem próximo passo.
5. tom: formal, cordial e institucional (saudação, fechamento, “nós”, pedir em vez de mandar, acolher urgência ou insatisfação, oferecer alternativa a uma negativa). Sem gírias, abreviações, emojis ou ironia.
6. escrita: português correto (acentos, concordância, pontuação, gerundismo).
Avalie também se a resposta é a MELHOR abordagem para o caso: se havia um jeito mais seguro, mais completo ou mais acolhedor de responder.

Regras do retorno:
- “trecho” deve ser uma cópia EXATA, caractere por caractere, de um pedaço da RESPOSTA (para destacarmos no texto). Use trechos curtos (até ~12 palavras). Se o apontamento for sobre a resposta inteira ou sobre algo que falta, deixe “trecho” vazio.
- “troca” é o texto que substitui exatamente o “trecho”, mantendo a frase gramaticalmente correta. Se a correção depender de uma informação que você não tem (data, valor, motivo, nome), use colchetes, por exemplo [data e hora]. Se não houver troca direta, deixe “troca” vazio.
- Nunca invente fatos, prazos, valores ou regras. Na resposta sugerida, use só os fatos da resposta original e marque o que falta com colchetes.
- Textos como [CPF 1] ou [EMAIL 1] são dados mascarados: mantenha-os exatamente assim.
- Escreva tudo em português do Brasil, de forma clara e direta.
- nota: 0 a 100. veredito: “pronta” só se a resposta puder ser enviada como está; “ajustar” se precisar de ajustes; “nao_enviar” se houver problema grave (não responde, erro de fato, tom inadequado, risco de contestação séria).
""";

    static JsonObject Esquema()
    {
        JsonObject S(string tipo, string? desc = null) { var o = new JsonObject { ["type"] = tipo }; if (desc != null) o["description"] = desc; return o; }
        JsonObject Enum(params string[] v) => new() { ["type"] = "string", ["enum"] = new JsonArray(v.Select(x => (JsonNode)x).ToArray()) };
        JsonObject Obj(JsonObject props, params string[] req) => new() { ["type"] = "object", ["properties"] = props, ["required"] = new JsonArray(req.Select(x => (JsonNode)x).ToArray()), ["additionalProperties"] = false };
        JsonObject Arr(JsonObject item) => new() { ["type"] = "array", ["items"] = item };
        var notas = new JsonObject();
        foreach (var c in new[] { "responde", "objetividade", "discordancia", "contestavel", "tom", "escrita" }) notas[c] = S("integer");
        return Obj(new JsonObject
        {
            ["nota"] = S("integer", "0 a 100"),
            ["veredito"] = Enum("pronta", "ajustar", "nao_enviar"),
            ["resumo"] = S("string", "uma ou duas frases com a avaliação geral"),
            ["perguntas"] = Arr(Obj(new JsonObject
            {
                ["texto"] = S("string", "a pergunta, como a 3069 escreveu"),
                ["tipo"] = S("string", "ex.: Sim ou não, Prazo / data, Valor, Procedimento, Motivo, Status, Pedido de documento"),
                ["status"] = Enum("atendida", "parcial", "nao_atendida"),
                ["comentario"] = S("string"),
            }, "texto", "tipo", "status", "comentario")),
            ["notas_criterios"] = Obj(notas, "responde", "objetividade", "discordancia", "contestavel", "tom", "escrita"),
            ["apontamentos"] = Arr(Obj(new JsonObject
            {
                ["categoria"] = Enum("responde", "objetividade", "discordancia", "contestavel", "tom", "escrita"),
                ["gravidade"] = Enum("problema", "atencao", "leve"),
                ["titulo"] = S("string", "curto, até 6 palavras"),
                ["trecho"] = S("string", "cópia exata de um pedaço da resposta, ou vazio"),
                ["explicacao"] = S("string"),
                ["sugestao"] = S("string"),
                ["troca"] = S("string", "texto que substitui o trecho, ou vazio"),
            }, "categoria", "gravidade", "titulo", "trecho", "explicacao", "sugestao", "troca")),
            ["pontos_fortes"] = Arr(S("string")),
            ["reacao_provavel"] = S("string", "como a 3069 provavelmente vai reagir a esta resposta, e o que ela pode perguntar em seguida"),
            ["resposta_sugerida"] = S("string", "a resposta reescrita, pronta para o Teams"),
        }, "nota", "veredito", "resumo", "perguntas", "notas_criterios", "apontamentos", "pontos_fortes", "reacao_provavel", "resposta_sugerida");
    }

    // ------------------------- máscara de dados pessoais -------------------------

    static readonly (string tipo, Regex rx)[] Mascaras =
    {
        ("CNPJ", new Regex(@"\b\d{2}\.?\d{3}\.?\d{3}/?\d{4}-?\d{2}\b")),
        ("CPF", new Regex(@"\b\d{3}\.?\d{3}\.?\d{3}-?\d{2}\b")),
        ("EMAIL", new Regex(@"\b[\w.+-]+@[\w-]+\.[\w.-]+\b")),
        ("TELEFONE", new Regex(@"\(\d{2}\)\s?9?\d{4}-?\d{4}\b|\b\d{2}\s9?\d{4}-\d{4}\b|\b9\d{4}-\d{4}\b")),
    };

    /// <summary>Troca CPF, CNPJ, e-mail e telefone por marcadores antes de enviar. Devolve o texto e o mapa para desfazer.</summary>
    public static (string texto, Dictionary<string, string> mapa) Mascarar(string texto, Dictionary<string, string>? mapa = null)
    {
        mapa ??= new();
        foreach (var (tipo, rx) in Mascaras)
        {
            texto = rx.Replace(texto, m =>
            {
                var ja = mapa.FirstOrDefault(k => k.Value == m.Value).Key;
                if (ja != null) return ja;
                var chave = $"[{tipo} {mapa.Keys.Count(k => k.StartsWith("[" + tipo)) + 1}]";
                mapa[chave] = m.Value;
                return chave;
            });
        }
        return (texto, mapa);
    }

    public static string Desmascarar(string? s, Dictionary<string, string> mapa)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        foreach (var (k, v) in mapa) s = s.Replace(k, v);
        return s;
    }

    // ------------------------- pedido -------------------------

    /// <summary>Corpo do pedido para o Groq. comEsquema=false usa o modo JSON simples (o formato vai descrito nas instruções).</summary>
    public static string Pedido(string pergunta, string resposta, DateTime agora, Dictionary<string, string> mapa, string modelo, bool comEsquema = true)
    {
        var (p, _) = Mascarar(pergunta, mapa);
        var (r, _) = Mascarar(resposta, mapa);
        var texto = $"""
Hoje é {agora:dd/MM/yyyy} ({Dia(agora.DayOfWeek)}), {agora:HH:mm}.

PERGUNTA DA 3069:
<<<
{p.Trim()}
>>>

NOSSA RESPOSTA (vai ser enviada pelo Teams):
<<<
{r.Trim()}
>>>
""";
        var esquema = Esquema();
        var sistema = Instrucoes + "\nResponda SOMENTE com um objeto JSON válido, sem texto antes ou depois, seguindo exatamente este esquema JSON:\n" + esquema.ToJsonString();
        var corpo = new JsonObject
        {
            ["model"] = modelo,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = sistema },
                new JsonObject { ["role"] = "user", ["content"] = texto }),
            ["temperature"] = 0.2,
            ["max_completion_tokens"] = 6000,
            ["response_format"] = comEsquema
                ? new JsonObject { ["type"] = "json_schema", ["json_schema"] = new JsonObject { ["name"] = "analise_resposta", ["strict"] = false, ["schema"] = Esquema() } }
                : new JsonObject { ["type"] = "json_object" },
        };
        return corpo.ToJsonString();
    }

    /// <summary>Pedido mínimo para testar a chave.</summary>
    public static string PedidoTeste(string modelo) => new JsonObject
    {
        ["model"] = modelo,
        ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Responda apenas: ok" }),
        ["max_completion_tokens"] = 200,
    }.ToJsonString();

    static string Dia(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "segunda-feira", DayOfWeek.Tuesday => "terça-feira", DayOfWeek.Wednesday => "quarta-feira",
        DayOfWeek.Thursday => "quinta-feira", DayOfWeek.Friday => "sexta-feira", DayOfWeek.Saturday => "sábado", _ => "domingo",
    };

    /// <summary>Mensagem de erro amigável para a resposta HTTP do Groq.</summary>
    public static string Erro(int status, string corpo)
    {
        string msg = "", code = "";
        try { var e = JsonNode.Parse(corpo)?["error"]; msg = e?["message"]?.GetValue<string>() ?? ""; code = e?["code"]?.ToString() ?? ""; } catch { }
        var espera = Regex.Match(msg, @"try again in ([\d.]+m?[\d.]*s)");
        return status switch
        {
            0 => "Não consegui falar com o Groq. Confira a internet (ou se a rede da empresa bloqueia api.groq.com).",
            401 => "A chave do Groq não é válida. Confira a chave em Configurar.",
            403 => "A chave do Groq não tem permissão para este modelo. " + msg,
            404 => "Modelo não encontrado no Groq. Confira o nome do modelo em Configurar.",
            413 => "O texto ficou grande demais para o limite do plano gratuito. Encurte a pergunta ou a resposta.",
            429 => "Limite do plano gratuito do Groq atingido" + (espera.Success ? $": tente de novo em {espera.Groups[1].Value}." : ". Espere um minuto e tente de novo."),
            400 when code.Contains("json_validate_failed") => "A IA devolveu a análise fora do formato. Tente de novo.",
            400 => "O Groq recusou o pedido: " + msg,
            >= 500 => "O Groq está instável agora. Tente de novo em alguns instantes.",
            _ => $"Erro {status} do Groq. {msg}",
        };
    }

    /// <summary>O erro 400 é de formato (vale tentar de novo sem o esquema)?</summary>
    public static bool ErroDeFormato(int status, string corpo)
        => status == 400 && (corpo.Contains("json_validate_failed") || corpo.Contains("response_format") || corpo.Contains("json_schema"));

    // ------------------------- leitura -------------------------

    public static ResultadoIA Ler(string corpoHttp, string resposta, Dictionary<string, string> mapa, string modelo)
    {
        var raiz = JsonNode.Parse(corpoHttp) ?? throw new InvalidDataException("Resposta vazia do Groq.");
        var ch = raiz["choices"]?[0];
        var motivo = ch?["finish_reason"]?.GetValue<string>();
        var txt = ch?["message"]?["content"]?.GetValue<string>() ?? "";
        if (motivo == "length") throw new InvalidDataException("A análise ficou longa demais e foi cortada. Tente de novo ou encurte a resposta.");
        if (txt.Trim() == "") throw new InvalidDataException($"O Groq não devolveu a análise ({motivo ?? "sem motivo"}).");
        // tira um eventual texto antes/depois do JSON
        int i0 = txt.IndexOf('{'), i1 = txt.LastIndexOf('}');
        if (i0 >= 0 && i1 > i0) txt = txt[i0..(i1 + 1)];
        return LerAnalise(txt, resposta, mapa, modelo);
    }

    public static ResultadoIA LerAnalise(string json, string resposta, Dictionary<string, string> mapa, string modelo)
    {
        json = json.Trim();
        if (json.StartsWith("```")) json = Regex.Replace(json, @"^```(json)?\s*|\s*```$", "");
        var o = JsonNode.Parse(json) ?? throw new InvalidDataException("A análise veio vazia.");
        string S(JsonNode? n) => Desmascarar(n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n?.ToString() ?? "", mapa).Trim();
        int I(JsonNode? n, int d) { try { return n == null ? d : n.GetValue<int>(); } catch { return int.TryParse(n?.ToString(), out var x) ? x : d; } }

        var a = new Analise();
        var frases = Regex.Matches(resposta, @"[^.!?\n]+[.!?]*").Count;
        a.Palavras = Regex.Matches(resposta, @"\p{L}+|\d+").Count;
        a.Frases = Math.Max(1, frases);
        a.SegundosLeitura = (int)Math.Ceiling(a.Palavras / 3.3);
        a.Nota = Math.Clamp(I(o["nota"], 0), 0, 100);
        var ver = S(o["veredito"]);
        (a.Veredito, a.VereditoClasse) = ver switch
        {
            "pronta" => ("Pronta para enviar", "ok"),
            "ajustar" => ("Ajuste antes de enviar", "ajuste"),
            _ => ("Não enviar ainda", "nao"),
        };
        foreach (var p in o["perguntas"]?.AsArray() ?? new JsonArray())
        {
            var st = S(p?["status"]) switch { "atendida" => StatusPergunta.Atendida, "parcial" => StatusPergunta.Parcial, _ => StatusPergunta.NaoAtendida };
            a.Perguntas.Add(new SubPergunta(S(p?["texto"]), S(p?["tipo"]), st, S(p?["comentario"]).TrimEnd('.'), new()));
        }
        var nc = o["notas_criterios"];
        foreach (var c in new[] { Cat.Responde, Cat.Objetividade, Cat.Discordancia, Cat.Contestavel, Cat.Tom, Cat.Escrita })
            a.NotaPorCategoria[c] = Math.Clamp(I(nc?[c], 100), 0, 100);
        var usados = new List<(int, int)>();
        foreach (var x in o["apontamentos"]?.AsArray() ?? new JsonArray())
        {
            var cat = S(x?["categoria"]);
            if (!new[] { Cat.Responde, Cat.Objetividade, Cat.Discordancia, Cat.Contestavel, Cat.Tom, Cat.Escrita }.Contains(cat)) cat = Cat.Contestavel;
            var g = S(x?["gravidade"]) switch { "problema" => Gravidade.Problema, "atencao" => Gravidade.Atencao, _ => Gravidade.Leve };
            var trecho = S(x?["trecho"]);
            var troca = x?["troca"] is null ? "" : Desmascarar(x["troca"] is JsonValue tv && tv.TryGetValue<string>(out var ts) ? ts : "", mapa);
            var (ini, fim) = Localizar(resposta, trecho, usados);
            if (ini >= 0) usados.Add((ini, fim));
            a.Achados.Add(new Achado(cat, g, S(x?["titulo"]), S(x?["explicacao"]), trecho == "" ? null : trecho, ini, ini >= 0 ? fim : -1, NullSeVazio(S(x?["sugestao"])))
            {
                Troca = ini >= 0 && troca.Trim() != "" && troca.Trim() != trecho ? Ajustar(troca, resposta, ini, fim) : null,
            });
        }
        return new ResultadoIA
        {
            Analise = a,
            Resumo = S(o["resumo"]),
            RespostaSugerida = S(o["resposta_sugerida"]),
            Reacao = S(o["reacao_provavel"]),
            PontosFortes = (o["pontos_fortes"]?.AsArray() ?? new JsonArray()).Select(S).Where(s => s != "").ToList(),
            Modelo = modelo,
        };
    }

    static string? NullSeVazio(string s) => s.Trim() == "" ? null : s;

    /// <summary>Mantém espaços e pontuação das pontas do trecho original quando a IA os perde.</summary>
    static string Ajustar(string troca, string resposta, int ini, int fim)
    {
        troca = troca.Trim();
        var orig = resposta[ini..fim];
        if (orig.EndsWith(' ') && !troca.EndsWith(' ')) troca += " ";
        if (orig.StartsWith(' ') && !troca.StartsWith(' ')) troca = " " + troca;
        return troca;
    }

    /// <summary>Acha o trecho na resposta: exato, depois sem diferença de maiúsculas/acentos/espaços. Evita repetir o mesmo lugar.</summary>
    public static (int ini, int fim) Localizar(string resposta, string trecho, List<(int, int)>? usados = null)
    {
        trecho = trecho.Trim().Trim('"', '“', '”', '«', '»');
        if (trecho.Length < 2) return (-1, -1);
        int idx = -1;
        for (int de = 0; ; )
        {
            var i = resposta.IndexOf(trecho, de, StringComparison.Ordinal);
            if (i < 0) break;
            if (usados == null || !usados.Any(u => i < u.Item2 && u.Item1 < i + trecho.Length)) { idx = i; break; }
            de = i + 1;
        }
        if (idx >= 0) return (idx, idx + trecho.Length);
        // tolerante: compara sem acento, minúsculo e com espaços colapsados
        var rn = AnaliseResposta.Norm(resposta);
        var tn = Regex.Replace(AnaliseResposta.Norm(trecho), @"\s+", " ");
        var partes = tn.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
        var m = Regex.Match(rn, string.Join(@"\s+", partes));
        if (m.Success) return (m.Index, m.Index + m.Length);
        return (-1, -1);
    }
}
