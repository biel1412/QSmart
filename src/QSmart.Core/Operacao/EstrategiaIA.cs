using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace QSmart.Core.Operacao;

/// <summary>Estratégia do dia escrita pelo Mr. Smart (Groq) a partir dos números da tela.</summary>
public static class EstrategiaIA
{
    const string Instrucoes = """
        Você é o Mr. Smart, coordenador experiente de uma equipe de análise de fluxos (cadastro e crédito) de uma cooperativa (Sicoob).
        Escreva a ESTRATÉGIA DO DIA que vai na mensagem do Teams para a equipe, a partir dos números recebidos.
        Regras:
        - Português do Brasil, tom de líder próximo e direto, primeira pessoa do plural ("vamos", "nosso foco").
        - 3 a 5 frases, em um único parágrafo, sem saudação (a saudação já existe) e sem repetir a lista de números do dia anterior (ela já vai na mensagem).
        - Diga onde colocar a força do time (Centralizada, Autorizar ou dividir), o que priorizar (mais antigos, serviço que mais entra, reduzir devoluções, SLA) e um ponto de atenção de qualidade.
        - Use os números só quando ajudarem a justificar (ex.: páginas de cada fila, % do serviço mais frequente, devoluções).
        - Não invente números nem nomes de pessoas. Se um dado estiver vazio, ignore.
        - Não use emojis, listas nem markdown.
        Responda só com JSON: {"estrategia": "..."}
        """;

    public static string Fatos(MensagemEstrategia m, ResultadoEstrategia? r, DateTime agora)
    {
        string V(string s) => string.IsNullOrWhiteSpace(s) ? "(vazio)" : s.Trim();
        var sb = new StringBuilder();
        var dias = new[] { "domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado" };
        sb.AppendLine($"Hoje: {dias[(int)agora.DayOfWeek]}, {agora:dd/MM/yyyy}");
        sb.AppendLine($"Início do dia - Centralizada: {V(m.CentralizadaPaginas)} páginas ({V(m.CentralizadaFluxos)} fluxos); Autorizar: {V(m.AutorizarPaginas)} páginas ({V(m.AutorizarFluxos)} fluxos)");
        sb.AppendLine($"Dia anterior ({V(m.DiaDados)}): volume total realizado {V(m.VolumeTotal)} fluxos; com data {V(m.DataFluxos)}: {V(m.VolumeComData)}; entraram no dia: {V(m.Entraram)}; SLA: {V(m.Sla)}");
        if (r != null && r.Registros > 0)
        {
            sb.AppendLine($"Compilado do dia {r.Dia:dd/MM}: {r.Registros} registros, {r.Usuarios} pessoas, {r.Distintos} tipos de serviço");
            sb.AppendLine("Serviços que mais apareceram: " + string.Join("; ", r.Servicos.OrderByDescending(s => s.Quantidade).Take(5)
                .Select(s => $"{s.Servico} {(s.Quantidade * 100.0 / r.Registros).ToString("0.#", CultureInfo.InvariantCulture)}%")));
            var procs = r.Procedimentos.OrderByDescending(p => p.Value).Take(5).Select(p => $"{p.Key} {p.Value}");
            sb.AppendLine("Procedimentos: " + string.Join("; ", procs));
        }
        if (!string.IsNullOrWhiteSpace(m.Texto)) sb.AppendLine("Rascunho atual do coordenador (aproveite as ideias): " + m.Texto.Trim());
        return sb.ToString();
    }

    public static string Pedido(string fatos, string modelo) => new JsonObject
    {
        ["model"] = modelo,
        ["temperature"] = 0.5,
        ["max_completion_tokens"] = 1500,
        ["response_format"] = new JsonObject { ["type"] = "json_object" },
        ["messages"] = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = Instrucoes },
            new JsonObject { ["role"] = "user", ["content"] = fatos }),
    }.ToJsonString();

    public static string? Ler(string corpoHttp)
    {
        try
        {
            var c = JsonNode.Parse(corpoHttp)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
            int i = c.IndexOf('{'), f = c.LastIndexOf('}');
            if (i >= 0 && f > i && JsonNode.Parse(c[i..(f + 1)])?["estrategia"]?.GetValue<string>() is { } e && e.Trim() != "") return e.Trim();
            return string.IsNullOrWhiteSpace(c) ? null : c.Trim();
        }
        catch { return null; }
    }
}
