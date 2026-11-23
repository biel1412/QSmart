using System.Text.Json.Nodes;

namespace QSmart.UI.Servicos;

public sealed record ContatoChat(int Id, string Nome, string Papel, string Foto);
public sealed record ResumoChat(int Com, string Ultima, string Em, bool Minha, bool Lida, int NaoLidas);
public sealed record MsgChat(int Id, int De, int Para, string Texto, DateTime Em, bool Lida, DateTime Expira);

/// <summary>Chat entre os usuários do QSmart: conversas 1 a 1; cada mensagem se apaga 3 dias depois de enviada.</summary>
public sealed class Chat
{
    readonly Estado _e;
    public Chat(Estado e) { _e = e; }

    public List<ContatoChat> Contatos { get; private set; } = new();
    public Dictionary<int, ResumoChat> Conversas { get; private set; } = new();
    public HashSet<int> Online { get; private set; } = new();
    public Dictionary<int, DateTime> Vistos { get; private set; } = new();
    public int Total { get; private set; }
    public int Dias { get; private set; } = 3;
    public bool Aberto { get; set; }
    public int? Com { get; set; }
    public List<MsgChat> Msgs { get; private set; } = new();
    public bool ComOnline { get; private set; }
    public DateTime? ComVisto { get; private set; }
    public string Erro { get; private set; } = "";

    /// <summary>Quem mandou a última mensagem nova (para o aviso rápido quando o chat está fechado).</summary>
    public event Action<ContatoChat, string>? Chegou;
    public event Action? Mudou;

    static string S(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
    static int I(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<int>(out var x) ? x : 0;
    static bool B(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<bool>(out var x) && x;
    static DateTime D(string s) => DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime() : DateTime.Now;

    public void Limpar() { Contatos = new(); Conversas = new(); Online = new(); Vistos = new(); Total = 0; Msgs = new(); Com = null; Aberto = false; Mudou?.Invoke(); }

    public ContatoChat? Contato(int id) => Contatos.FirstOrDefault(c => c.Id == id);

    public async Task CarregarContatos()
    {
        var r = await _e.Conta.Chamar("GET", "chat/contatos");
        if (!r.Ok || r.Json == null) { Erro = r.Erro; Mudou?.Invoke(); return; }
        Erro = "";
        Contatos = (r.Json["contatos"] as JsonArray ?? new()).Where(x => x != null)
            .Select(x => new ContatoChat(I(x, "id"), S(x, "nome"), S(x, "papel"), S(x, "foto"))).ToList();
        Mudou?.Invoke();
    }

    public async Task AtualizarResumo()
    {
        if (!_e.Conta.Logado) return;
        var r = await _e.Conta.Chamar("GET", "chat/resumo");
        if (!r.Ok || r.Json == null) return;
        var antes = Conversas;
        Online = (r.Json["online"] as JsonArray ?? new()).Select(x => x?.GetValue<int>() ?? 0).ToHashSet();
        var v = new Dictionary<int, DateTime>();
        if (r.Json["vistos"] is JsonObject vo) foreach (var (k, val) in vo) if (int.TryParse(k, out var id)) v[id] = D(val?.GetValue<string>() ?? "");
        Vistos = v;
        Conversas = (r.Json["conversas"] as JsonArray ?? new()).Where(x => x != null)
            .Select(x => new ResumoChat(I(x, "com"), S(x, "ultima"), S(x, "em"), B(x, "minha"), B(x, "lida"), I(x, "naoLidas"))).ToDictionary(c => c.Com);
        Total = I(r.Json, "total");
        Dias = I(r.Json, "dias") is > 0 and var d ? d : 3;
        // aviso de mensagem nova (fora da conversa aberta)
        foreach (var c in Conversas.Values)
        {
            int eram = antes.TryGetValue(c.Com, out var a) ? a.NaoLidas : 0;
            if (!_primeiro && c.NaoLidas > eram && !(Aberto && Com == c.Com))
            {
                if (Contatos.Count == 0) await CarregarContatos();
                if (Contato(c.Com) is { } ct) Chegou?.Invoke(ct, c.Ultima);
            }
        }
        _primeiro = false;
        Mudou?.Invoke();
    }
    bool _primeiro = true;

    public async Task AbrirConversa(int com)
    {
        if (Com != com) Msgs = new();
        Com = com;
        await AtualizarConversa();
    }

    public async Task AtualizarConversa()
    {
        if (Com is not int com) return;
        var r = await _e.Conta.Chamar("GET", $"chat/conversa?com={com}");
        if (!r.Ok || r.Json == null || Com != com) return;
        Msgs = (r.Json["msgs"] as JsonArray ?? new()).Where(x => x != null)
            .Select(x => new MsgChat(I(x, "id"), I(x, "de"), I(x, "para"), S(x, "texto"), D(S(x, "em")), B(x, "lida"), D(S(x, "expira")))).ToList();
        ComOnline = B(r.Json, "online");
        ComVisto = S(r.Json, "visto") is { Length: > 0 } vs ? D(vs) : null;
        // abrir a conversa marca como lidas
        if (Conversas.TryGetValue(com, out var c) && c.NaoLidas > 0) { Total = Math.Max(0, Total - c.NaoLidas); Conversas[com] = c with { NaoLidas = 0 }; }
        Mudou?.Invoke();
    }

    public async Task<string?> Enviar(string texto)
    {
        if (Com is not int com || string.IsNullOrWhiteSpace(texto)) return null;
        var r = await _e.Conta.Chamar("POST", "chat", new JsonObject { ["para"] = com, ["texto"] = texto.Trim() });
        if (!r.Ok) return r.Erro;
        var x = r.Json?["msg"];
        if (x != null && Com == com)
        {
            Msgs.Add(new MsgChat(I(x, "id"), I(x, "de"), I(x, "para"), S(x, "texto"), D(S(x, "em")), false, D(S(x, "expira"))));
            var t = texto.Trim().Replace('\n', ' ');
            Conversas[com] = new ResumoChat(com, t.Length > 70 ? t[..70] + "…" : t, S(x, "em"), true, false, 0);
        }
        Mudou?.Invoke();
        return null;
    }

    /// <summary>"online", "visto há 5 min", "visto ontem às 18:20"…</summary>
    public string Status(int id)
    {
        if (Online.Contains(id)) return "online";
        if (!Vistos.TryGetValue(id, out var v)) return "ainda não abriu o chat";
        var dif = DateTime.Now - v;
        if (dif.TotalMinutes < 60) return $"visto há {Math.Max(1, (int)dif.TotalMinutes)} min";
        if (v.Date == DateTime.Today) return $"visto hoje às {v:HH:mm}";
        if (v.Date == DateTime.Today.AddDays(-1)) return $"visto ontem às {v:HH:mm}";
        return $"visto em {v:dd/MM}";
    }
}
