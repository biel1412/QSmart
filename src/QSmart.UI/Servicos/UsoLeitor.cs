using System.Text.Json.Nodes;
using QSmart.Core.Admin;

namespace QSmart.UI.Servicos;

/// <summary>Lê a resposta de admin/uso (servidor) para o modelo do painel de uso.</summary>
public static class UsoLeitor
{
    static string S(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
    static long L(JsonNode? n, string k)
    {
        if (n?[k] is not JsonValue v) return 0;
        if (v.TryGetValue<long>(out var x)) return x;
        return v.TryGetValue<double>(out var d) ? (long)d : 0;
    }
    static IEnumerable<JsonNode> A(JsonNode? n, string k) => (n?[k] as JsonArray ?? new JsonArray()).Where(x => x != null)!;
    static DateOnly D(string s) => DateOnly.TryParseExact(s, "yyyy-MM-dd", out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow);

    public static UsoSistema Ler(JsonNode j)
    {
        var lim = j["limites"];
        var u = new UsoSistema
        {
            GeradoEm = DateTime.TryParse(S(j, "agora"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var g) ? g : DateTime.UtcNow,
            DiaUtc = D(S(j, "diaUtc")),
            Lim = new LimitesUso(S(lim, "plano"), L(lim, "reqDia"), L(lim, "lerDia"), L(lim, "gravarDia"), L(lim, "bancoBytes"), L(lim, "contaBancoBytes"),
                L(lim, "r2Bytes"), L(lim, "r2AMes"), L(lim, "r2BMes"), L(lim, "iaReqDia"), L(lim, "iaTokDia"), L(lim, "arquivoMaxBytes")),
            BancoBytes = L(j["banco"], "bytes"),
            Indices = (int)L(j["banco"], "indices"),
            Tabelas = A(j["banco"], "tabelas").Select(t => new TabelaUso(S(t, "nome"), S(t, "modulo"), L(t, "linhas"), L(t, "bytes"))).ToList(),
            R2 = j["arquivos"]?["ligado"]?.GetValue<bool>() ?? false,
            R2Bytes = L(j["arquivos"], "bytes"),
            R2Objetos = L(j["arquivos"], "objetos"),
            Pastas = A(j["arquivos"], "pastas").Select(p => new PastaUso(S(p, "pasta"), L(p, "objetos"), L(p, "bytes"))).ToList(),
            Maiores = A(j["arquivos"], "maiores").Select(a => new ArquivoUso(S(a, "nome"), S(a, "titulo"), L(a, "bytes"), S(a, "em"))).ToList(),
            Papeis = A(j["pessoas"], "papeis").Select(p => new PapelUso(S(p, "papel"), S(p, "status"), L(p, "n"))).ToList(),
            AtivosHoje = L(j["pessoas"], "ativosHoje"), Ativos7 = L(j["pessoas"], "ativos7"), Ativos30 = L(j["pessoas"], "ativos30"), Sessoes = L(j["pessoas"], "sessoes"),
            Ferramentas = A(j, "ferramentas").Select(f => new FerramentaUso(S(f, "chave"), L(f, "n"), L(f, "pessoas"), S(f, "ultimo"))).ToList(),
            MedindoDesde = D(S(j, "medindoDesde")),
        };
        u.Historico = A(j, "historico").Select(h =>
        {
            var v = new Dictionary<string, long>();
            if (h["v"] is JsonObject o) foreach (var kv in o) v[kv.Key] = L(o, kv.Key);
            return new DiaUso(D(S(h, "dia")), v);
        }).OrderBy(d => d.Dia).ToList();
        return u;
    }
}
