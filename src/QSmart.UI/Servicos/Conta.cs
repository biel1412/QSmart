using System.Text.Json;
using System.Text.Json.Nodes;

namespace QSmart.UI.Servicos;

public sealed class PerfilConta
{
    public int Id { get; set; }
    public string Usuario { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Sobrenome { get; set; } = "";
    public string Papel { get; set; } = "usuario";
    public string Lideranca { get; set; } = "";
    public string Status { get; set; } = "";
    public string Foto { get; set; } = "";
    public string CriadoEm { get; set; } = "";
    public string? UltimoAcesso { get; set; }

    public string NomeCompleto => $"{Nome} {Sobrenome}".Trim();
    public string Iniciais => ((Nome.Length > 0 ? Nome[..1] : "") + (Sobrenome.Length > 0 ? Sobrenome[..1] : "")).ToUpperInvariant();
    public bool Admin => Papel == "admin";
    public bool VeTudoOO => Papel is "admin" or "coordenador";
    public bool TemOO => VeTudoOO || (Papel == "lideranca" && Lideranca != "");
    public string PapelNome => NomePapel(Papel);
    public static string NomePapel(string p) => p switch { "admin" => "Administrador", "coordenador" => "Coordenador", "lideranca" => "Liderança", _ => "Usuário" };
}

public sealed class UsuarioAdmin
{
    public int Id { get; set; }
    public string Usuario { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Sobrenome { get; set; } = "";
    public string Papel { get; set; } = "";
    public string Lideranca { get; set; } = "";
    public string Status { get; set; } = "";
    public string Criado_em { get; set; } = "";
    public string? Ultimo_acesso { get; set; }
}

public sealed record RespostaApi(bool Ok, int Status, JsonNode? Json, string Erro);

/// <summary>Conta do usuário no QSmart: login, perfil, uso (medalhas) e acesso ao banco (One a One).</summary>
public sealed class Conta
{
    readonly Estado _e;
    public Conta(Estado e) => _e = e;

    /// <summary>Endereço da API (intermediário no Cloudflare). Nos testes locais, um intermediário de mentira.</summary>
#if QSMART_LOCAL
    public static string Base = "http://127.0.0.1:8787/api/";
#else
    public static string Base = Estado.ProxyPadrao + "/api/";
#endif

    const string CofreLembrar = "conta_lembrar";
    static readonly JsonSerializerOptions Opc = new() { PropertyNameCaseInsensitive = true };

    public string? Token { get; private set; }
    public PerfilConta? Perfil { get; private set; }
    public bool Logado => Token != null && Perfil != null;
    public Dictionary<string, int> Uso { get; private set; } = new();
    public List<DateOnly> Dias { get; private set; } = new();
    public event Action? Mudou;

    // ---------------- chamadas ----------------

    public async Task<RespostaApi> Chamar(string metodo, string rota, JsonObject? corpo = null)
    {
        var cab = new Dictionary<string, string> { ["Content-Type"] = "application/json" };
        if (Token != null) cab["Authorization"] = "Bearer " + Token;
        var r = await _e.Nav.Req(Base + rota, metodo, cab, corpo?.ToJsonString());
        JsonNode? j = null;
        try { if (!string.IsNullOrWhiteSpace(r.Texto)) j = JsonNode.Parse(r.Texto); } catch { }
        var erro = r.Status == 0 ? "Não consegui falar com o servidor do QSmart. Confira a internet e tente de novo."
            : j?["erro"]?.GetValue<string>() ?? (r.Status >= 400 ? $"Erro {r.Status} no servidor." : "");
        if (r.Status == 401 && j?["motivo"]?.GetValue<string>() == "sessao" && Token != null) { Limpar(); }
        return new RespostaApi(r.Status is >= 200 and < 300, r.Status, j, erro);
    }

    static string S(JsonNode? n, string k) { try { return n?[k] is JsonValue v ? (v.TryGetValue<string>(out var t) ? t : v.ToJsonString()) : ""; } catch { return ""; } }
    static int I(JsonNode? n, string k) { try { return n?[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : int.TryParse(S(n, k), out var j) ? j : 0; } catch { return 0; } }

    static PerfilConta? LerPerfil(JsonNode? n) => n == null ? null : new PerfilConta
    {
        Id = I(n, "id"), Usuario = S(n, "usuario"), Nome = S(n, "nome"), Sobrenome = S(n, "sobrenome"), Papel = S(n, "papel"),
        Lideranca = S(n, "lideranca"), Status = S(n, "status"), Foto = S(n, "foto"), CriadoEm = S(n, "criadoEm"), UltimoAcesso = S(n, "ultimoAcesso"),
    };

    static JsonObject Obj(params (string k, object? v)[] campos)
    {
        var o = new JsonObject();
        foreach (var (k, v) in campos)
            if (v != null) o[k] = v switch { int i => JsonValue.Create(i), string t => JsonValue.Create(t), _ => JsonValue.Create(v.ToString()) };
        return o;
    }

    void Limpar() { Token = null; Perfil = null; Uso = new(); Dias = new(); _e.OneBase = null; _e.OneErro = null; _e.FeDados = null; _e.FeErro = null; Mudou?.Invoke(); _e.Notificar(); }

    // ---------------- lembrar usuário (cifrado no navegador) ----------------

    public async Task<(string usuario, string nome, string foto)?> Lembrado()
    {
        try
        {
            var b = await _e.Nav.CofreLer(CofreLembrar);
            if (b is not { Length: > 0 }) return null;
            var j = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(b));
            return (j?["u"]?.GetValue<string>() ?? "", j?["n"]?.GetValue<string>() ?? "", j?["f"]?.GetValue<string>() ?? "");
        }
        catch { return null; }
    }

    public async Task Lembrar(bool sim)
    {
        if (!sim || Perfil == null) { await _e.Nav.CofreApagar(CofreLembrar); return; }
        var foto = Perfil.Foto.Length < 60000 ? Perfil.Foto : "";
        var j = new JsonObject { ["u"] = Perfil.Usuario, ["n"] = Perfil.Nome, ["f"] = foto };
        await _e.Nav.CofreGravar(CofreLembrar, System.Text.Encoding.UTF8.GetBytes(j.ToJsonString()));
    }

    // ---------------- conta ----------------

    public async Task<RespostaApi> Cadastrar(string nome, string sobrenome, string usuario, string senha)
        => await Chamar("POST", "cadastro", new JsonObject { ["nome"] = nome, ["sobrenome"] = sobrenome, ["usuario"] = usuario, ["senha"] = senha });

    public async Task<RespostaApi> Entrar(string usuario, string senha)
    {
        var r = await Chamar("POST", "entrar", new JsonObject { ["usuario"] = usuario, ["senha"] = senha });
        if (r.Ok && r.Json != null)
        {
            Token = r.Json["token"]?.GetValue<string>();
            Perfil = LerPerfil(r.Json["perfil"]);
            await CarregarEu();
            _ = Registrar("inicio");
            Mudou?.Invoke(); _e.Notificar();
        }
        return r;
    }

    public async Task<RespostaApi> Recuperar(string usuario, string codigo, string nova)
        => await Chamar("POST", "recuperar", new JsonObject { ["usuario"] = usuario, ["codigo"] = codigo, ["nova"] = nova });

    public async Task Sair()
    {
        if (Token != null) await Chamar("POST", "sair");
        Limpar();
    }

    public async Task CarregarEu()
    {
        var r = await Chamar("GET", "eu");
        if (!r.Ok || r.Json == null) return;
        Perfil = LerPerfil(r.Json["perfil"]) ?? Perfil;
        var uso = new Dictionary<string, int>();
        if (r.Json["uso"] is JsonArray a)
            foreach (var x in a) { var k = S(x, "chave"); if (k != "") uso[k] = I(x, "qtd"); }
        Uso = uso;
        var dias = new List<DateOnly>();
        if (r.Json["dias"] is JsonArray d)
            foreach (var x in d) if (DateOnly.TryParseExact(x?.GetValue<string>() ?? "", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var dd)) dias.Add(dd);
        Dias = dias;
        Mudou?.Invoke();
    }

    readonly Dictionary<string, DateTime> _ultimoUso = new();

    /// <summary>Conta uso para as medalhas. Abrir a mesma aba de novo em menos de 10 min não conta.</summary>
    public async Task Registrar(string chave, bool acao = false)
    {
        if (!Logado) return;
        if (!acao && _ultimoUso.TryGetValue(chave, out var t) && (DateTime.Now - t).TotalMinutes < 10) return;
        _ultimoUso[chave] = DateTime.Now;
        Uso[chave] = Uso.GetValueOrDefault(chave) + 1;
        var hoje = DateOnly.FromDateTime(DateTime.Now);
        if (!Dias.Contains(hoje)) Dias.Insert(0, hoje);
        try { await Chamar("POST", "uso", new JsonObject { ["chave"] = chave }); } catch { }
        Mudou?.Invoke();
    }

    public async Task<RespostaApi> SalvarFoto(string dataUrl)
    {
        var r = await Chamar("POST", "perfil", new JsonObject { ["foto"] = dataUrl });
        if (r.Ok && Perfil != null) { Perfil.Foto = dataUrl; Mudou?.Invoke(); _e.Notificar(); }
        return r;
    }

    public async Task<RespostaApi> TrocarSenha(string atual, string nova) => await Chamar("POST", "senha", new JsonObject { ["atual"] = atual, ["nova"] = nova });

    // ---------------- administração ----------------

    public async Task<List<UsuarioAdmin>?> Usuarios()
    {
        var r = await Chamar("GET", "admin/usuarios");
        if (!r.Ok || r.Json?["usuarios"] is not JsonArray a) return null;
        return a.Select(x => new UsuarioAdmin
        {
            Id = I(x, "id"), Usuario = S(x, "usuario"), Nome = S(x, "nome"), Sobrenome = S(x, "sobrenome"), Papel = S(x, "papel"),
            Lideranca = S(x, "lideranca"), Status = S(x, "status"), Criado_em = S(x, "criado_em"), Ultimo_acesso = S(x, "ultimo_acesso"),
        }).ToList();
    }

    public async Task<RespostaApi> AtualizarUsuario(int id, string? status = null, string? papel = null, string? lideranca = null)
        => await Chamar("POST", "admin/usuario", Obj(("id", id), ("status", status), ("papel", papel), ("lideranca", lideranca)));

    public async Task<RespostaApi> NovoCodigo(int id) => await Chamar("POST", "admin/recuperacao", new JsonObject { ["id"] = id });
    public async Task<RespostaApi> Excluir(int id) => await Chamar("POST", "admin/excluir", new JsonObject { ["id"] = id });
}
