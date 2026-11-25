using System.Text.Json.Nodes;

namespace QSmart.UI.Servicos;

public sealed record ContatoChat(int Id, string Nome, string Papel, string Foto);
public sealed record ResumoChat(int Com, string Ultima, string Em, bool Minha, bool Lida, int NaoLidas);
public sealed record MsgChat(int Id, int De, int Para, string Texto, DateTime Em, bool Lida, DateTime Expira);
/// <summary>Grupo temporário: some sozinho (com as mensagens) na data de expiração.</summary>
public sealed record GrupoChat(int Id, string Nome, string Foto, int Criador, DateTime Expira, DateTime CriadoEm, List<int> Membros, string Ultima, string Em, int De, int NaoLidas);

/// <summary>Chat entre os usuários do QSmart: conversas 1 a 1 (cada mensagem se apaga 10 dias depois de enviada)
/// e grupos temporários (duram até 5 dias). <see cref="Com"/> negativo indica um grupo (-id).</summary>
public sealed class Chat
{
    readonly Estado _e;
    public Chat(Estado e) { _e = e; }

    public List<ContatoChat> Contatos { get; private set; } = new();
    public Dictionary<int, ResumoChat> Conversas { get; private set; } = new();
    public HashSet<int> Online { get; private set; } = new();
    public Dictionary<int, DateTime> Vistos { get; private set; } = new();
    public int Total { get; private set; }
    public int Dias { get; private set; } = 10;
    public bool Aberto { get; set; }
    public int? Com { get; set; }
    public List<MsgChat> Msgs { get; private set; } = new();
    public bool ComOnline { get; private set; }
    public DateTime? ComVisto { get; private set; }
    public string Erro { get; private set; } = "";
    public Dictionary<int, GrupoChat> Grupos { get; private set; } = new();
    public int GrupoMaxDias { get; private set; } = 5;
    /// <summary>Aviso rápido (ex.: o grupo expirou ou você foi removido).</summary>
    public string Aviso { get; set; } = "";
    public int MeuId => _e.Conta.Perfil?.Id ?? 0;
    public GrupoChat? GrupoAberto => Com is int c && c < 0 && Grupos.TryGetValue(-c, out var g) ? g : null;

    /// <summary>Quem mandou a última mensagem nova (para o aviso rápido quando o chat está fechado).</summary>
    public event Action<ContatoChat, string>? Chegou;
    public event Action? Mudou;

    static string S(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<string>(out var x) ? x : "";
    static int I(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<int>(out var x) ? x : 0;
    static bool B(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<bool>(out var x) && x;
    static DateTime D(string s) => DateTime.TryParse(s, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d.ToLocalTime() : DateTime.Now;

    public void Limpar() { Grupos = new(); Contatos = new(); Conversas = new(); Online = new(); Vistos = new(); Total = 0; Msgs = new(); Com = null; Aberto = false; Mudou?.Invoke(); }

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
        var gAntes = Grupos;
        Grupos = (r.Json["grupos"] as JsonArray ?? new()).Where(x => x != null).Select(LerGrupo).ToDictionary(g => g.Id);
        if (I(r.Json, "grupoMaxDias") is > 0 and var gm) GrupoMaxDias = gm;
        Total = I(r.Json, "total");
        Dias = I(r.Json, "dias") is > 0 and var d ? d : 10;
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
        foreach (var g in Grupos.Values)
        {
            int eram = gAntes.TryGetValue(g.Id, out var a) ? a.NaoLidas : 0;
            if (!_primeiro && g.NaoLidas > eram && !(Aberto && Com == -g.Id))
                Chegou?.Invoke(new ContatoChat(-g.Id, g.Nome, "grupo", g.Foto), UltimaDoGrupo(g));
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

    static GrupoChat LerGrupo(JsonNode? x) => new(I(x, "id"), S(x, "nome"), S(x, "foto"), I(x, "criador"), D(S(x, "expira")), D(S(x, "criadoEm")),
        (x?["membros"] as JsonArray ?? new()).Select(m => m?.GetValue<int>() ?? 0).Where(m => m > 0).ToList(),
        S(x, "ultima"), S(x, "em"), I(x, "de"), I(x, "naoLidas"));

    /// <summary>"Ana: texto" (ou o aviso do grupo, sem nome) para a lista e o aviso rápido.</summary>
    public string UltimaDoGrupo(GrupoChat g)
    {
        if (g.De == 0) return g.Ultima;
        if (g.De == MeuId) return "Você: " + g.Ultima;
        var n = Contato(g.De)?.Nome.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return (n != null ? n + ": " : "") + g.Ultima;
    }

    public string NomePessoa(int id) => id == MeuId ? "Você" : Contato(id)?.Nome ?? "Alguém";

    public async Task AtualizarConversa()
    {
        if (Com is not int com) return;
        if (com < 0) { await AtualizarGrupo(-com); return; }
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

    async Task AtualizarGrupo(int gid)
    {
        var r = await _e.Conta.Chamar("GET", $"chat/grupo?id={gid}");
        if (Com != -gid) return;
        if (r.Status == 404) { SumiuGrupo(gid, "Esse grupo expirou ou você não faz mais parte dele."); return; }
        if (!r.Ok || r.Json == null) return;
        var g = LerGrupo(r.Json["grupo"]);
        Grupos[gid] = (Grupos.TryGetValue(gid, out var a) ? g with { Ultima = a.Ultima, Em = a.Em, De = a.De } : g) with { NaoLidas = 0 };
        if (a?.NaoLidas > 0) Total = Math.Max(0, Total - a.NaoLidas);
        Msgs = (r.Json["msgs"] as JsonArray ?? new()).Where(x => x != null)
            .Select(x => new MsgChat(I(x, "id"), I(x, "de"), 0, S(x, "texto"), D(S(x, "em")), false, g.Expira)).ToList();
        if (Contatos.Count == 0) await CarregarContatos();
        Mudou?.Invoke();
    }

    void SumiuGrupo(int gid, string aviso)
    {
        Grupos.Remove(gid);
        if (Com == -gid) { Com = null; Msgs = new(); }
        Aviso = aviso;
        Mudou?.Invoke();
    }

    public async Task<(string? erro, int id)> CriarGrupo(string nome, int dias, IEnumerable<int> membros, string foto = "")
    {
        var r = await _e.Conta.Chamar("POST", "chat/grupo", new JsonObject { ["nome"] = nome.Trim(), ["dias"] = dias, ["foto"] = foto, ["membros"] = new JsonArray(membros.Select(m => (JsonNode)m).ToArray()) });
        if (!r.Ok) return (r.Erro, 0);
        var id = I(r.Json, "id");
        await AtualizarResumo();
        return (null, id);
    }

    public async Task<string?> AdicionarAoGrupo(int gid, IEnumerable<int> ids)
    {
        var r = await _e.Conta.Chamar("POST", "chat/grupo/membros", new JsonObject { ["id"] = gid, ["adicionar"] = new JsonArray(ids.Select(m => (JsonNode)m).ToArray()) });
        if (!r.Ok) return r.Erro;
        await AtualizarGrupo(gid); return null;
    }

    public async Task<string?> RemoverDoGrupo(int gid, int uid)
    {
        var r = await _e.Conta.Chamar("POST", "chat/grupo/membros", new JsonObject { ["id"] = gid, ["remover"] = uid });
        if (!r.Ok) return r.Erro;
        await AtualizarGrupo(gid); return null;
    }

    public async Task<string?> FotoDoGrupo(int gid, string foto)
    {
        var r = await _e.Conta.Chamar("POST", "chat/grupo/foto", new JsonObject { ["id"] = gid, ["foto"] = foto });
        if (!r.Ok) return r.Erro;
        await AtualizarGrupo(gid); return null;
    }

    public async Task<string?> RenomearGrupo(int gid, string nome)
    {
        var r = await _e.Conta.Chamar("POST", "chat/grupo/nome", new JsonObject { ["id"] = gid, ["nome"] = nome.Trim() });
        if (!r.Ok) return r.Erro;
        await AtualizarGrupo(gid); return null;
    }

    /// <summary>Sai do grupo; para quem criou, encerra (apaga o grupo para todos).</summary>
    public async Task<string?> SairDoGrupo(int gid)
    {
        var r = await _e.Conta.Chamar("POST", "chat/grupo/sair", new JsonObject { ["id"] = gid });
        if (!r.Ok) return r.Erro;
        SumiuGrupo(gid, "");
        await AtualizarResumo(); return null;
    }

    public async Task<string?> Enviar(string texto)
    {
        if (Com is not int com || string.IsNullOrWhiteSpace(texto)) return null;
        if (com < 0) return await EnviarGrupo(-com, texto);
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

    async Task<string?> EnviarGrupo(int gid, string texto)
    {
        var r = await _e.Conta.Chamar("POST", "chat/grupo/msg", new JsonObject { ["id"] = gid, ["texto"] = texto.Trim() });
        if (r.Status == 404) { SumiuGrupo(gid, "Esse grupo expirou ou você não faz mais parte dele."); return null; }
        if (!r.Ok) return r.Erro;
        var x = r.Json?["msg"];
        if (x != null && Com == -gid && Grupos.TryGetValue(gid, out var g))
        {
            Msgs.Add(new MsgChat(I(x, "id"), I(x, "de"), 0, S(x, "texto"), D(S(x, "em")), false, g.Expira));
            var t = texto.Trim().Replace('\n', ' ');
            Grupos[gid] = g with { Ultima = t.Length > 70 ? t[..70] + "…" : t, Em = S(x, "em"), De = MeuId, NaoLidas = 0 };
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
