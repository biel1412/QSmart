using System.Text.Json.Nodes;

namespace QSmart.UI.Servicos;

public sealed class Notificacao
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "";
    public int PostId { get; set; }
    public string CriadoEm { get; set; } = "";
    public bool Lida { get; set; }
    public int De { get; set; }
    public string Nome { get; set; } = "";
    public string Trecho { get; set; } = "";
    public string Foto { get; set; } = "";
    public string Ref { get; set; } = "";
    public string Href => Tipo switch
    {
        "caderno" => $"smart-note?c={Uri.EscapeDataString(Ref)}",
        "ferias" => $"ferias?mes={Uri.EscapeDataString(Ref.Split('|')[0])}",
        "dayoff_pedido" or "dayoff_cancelado" => $"day-off?d={Uri.EscapeDataString(Ref.Split('|')[0])}",
        "dayoff_ok" or "dayoff_nao" => $"day-off-pedidos?d={Uri.EscapeDataString(Ref.Split('|')[0])}",
        "aniversario" => LinkAniversario,
        _ => $"feed?post={PostId}",
    };
    public const string LinkAniversario = "https://insi.com/br/gerador-post-aniversario";
    /// <summary>Notificações que abrem fora do QSmart (nova aba).</summary>
    public bool Externo => Tipo == "aniversario";
    public string IconeTipo => Tipo switch { "aniversario" => "estrela", "curtida" => "coracao-cheio", "post" => "feed", "caderno" => "caderno", "ferias" => "calendario", "dayoff_ok" => "check", "dayoff_nao" => "alerta", "dayoff_pedido" or "dayoff_cancelado" => "estrela", _ => "conversa" };

    public string Texto => Tipo switch
    {
        "post" => $"{Nome} publicou no Feed",
        "curtida" => $"{Nome} curtiu a sua publicação",
        "comentario" => $"{Nome} comentou na sua publicação",
        "resposta" => $"{Nome} também comentou numa publicação que você comentou",
        "caderno" => $"{Nome} compartilhou um caderno do Smart Note com você",
        "dayoff_pedido" => $"{Nome} pediu um day off",
        "dayoff_cancelado" => $"{Nome} cancelou um day off",
        "dayoff_ok" => $"{Nome} aprovou o seu pedido de day off",
        "dayoff_nao" => $"{Nome} reprovou o seu pedido de day off",
        "aniversario" => $"🎂 Hoje é aniversário de {Nome}!",
        "ferias" => $"Férias acima do limite em {QSmart.Core.Coordenacao.FeriasClt.MesNome(Ref.Split('|')[0])}",
        _ => Nome,
    };
}

/// <summary>O sininho: notificações do Feed (do banco) e feriados próximos (calculados aqui).</summary>
public sealed class Sininho
{
    readonly Estado _e;
    public Sininho(Estado e) { _e = e; }

    public List<Notificacao> Itens { get; private set; } = new();
    public int NaoLidasFeed { get; private set; }
    HashSet<string> _feriadosVistos = new();
    bool _vistosLidos;
    public event Action? Mudou;
    public const int DiasFeriado = 7;

    public List<QSmart.Core.FeriadoDia> FeriadosProximos
    {
        get
        {
            var hoje = DateOnly.FromDateTime(DateTime.Today);
            return _e.FeriadosLista.Where(f => f.Data >= hoje && f.Data.DayNumber - hoje.DayNumber <= DiasFeriado).OrderBy(f => f.Data).ToList();
        }
    }

    public int FeriadosNovos => FeriadosProximos.Count(f => !_feriadosVistos.Contains(f.Data.ToString("yyyy-MM-dd")));
    public int Total => NaoLidasFeed + FeriadosNovos;

    public async Task Carregar()
    {
        if (!_e.Conta.Logado) { Limpar(); return; }
        if (!_vistosLidos)
        {
            _vistosLidos = true;
            try { _feriadosVistos = ((await _e.Nav.Ler("sino_feriados")) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(); } catch { }
        }
        var r = await _e.Conta.Chamar("GET", "notif");
        if (!r.Ok || r.Json == null) return;
        var fotos = r.Json["fotos"] as JsonObject;
        var l = new List<Notificacao>();
        foreach (var x in r.Json["itens"] as JsonArray ?? new())
        {
            var n = new Notificacao
            {
                Id = I(x, "id"), Tipo = S(x, "tipo"), PostId = I(x, "postId"), CriadoEm = S(x, "criadoEm"), Lida = x?["lida"]?.GetValue<bool>() ?? false,
                De = I(x, "de"), Nome = S(x, "nome"), Trecho = S(x, "trecho"), Ref = S(x, "ref"),
            };
            if (fotos?[n.De.ToString()] is JsonValue fv && fv.TryGetValue<string>(out var f)) n.Foto = f;
            l.Add(n);
        }
        Itens = l;
        NaoLidasFeed = I(r.Json, "naoLidas");
        Mudou?.Invoke();
    }

    /// <summary>Abriu o sininho: tudo vira lido.</summary>
    public async Task MarcarLidas()
    {
        bool mudou = false;
        foreach (var f in FeriadosProximos) mudou |= _feriadosVistos.Add(f.Data.ToString("yyyy-MM-dd"));
        if (mudou)
        {
            var hoje = DateOnly.FromDateTime(DateTime.Today).AddDays(-30).ToString("yyyy-MM-dd");
            _feriadosVistos = _feriadosVistos.Where(d => string.CompareOrdinal(d, hoje) >= 0).ToHashSet();
            await _e.Nav.Gravar("sino_feriados", string.Join(",", _feriadosVistos));
        }
        if (NaoLidasFeed > 0)
        {
            await _e.Conta.Chamar("POST", "notif/lidas");
            NaoLidasFeed = 0;
        }
        Mudou?.Invoke();
    }

    public void MarcarItensLidos() { foreach (var i in Itens) i.Lida = true; Mudou?.Invoke(); }

    public void Limpar() { Itens = new(); NaoLidasFeed = 0; Mudou?.Invoke(); }

    static string S(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
    static int I(JsonNode? n, string k) => n?[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : 0;
}
