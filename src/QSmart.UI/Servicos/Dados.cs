using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using QSmart.Core;

namespace QSmart.UI.Servicos;

public sealed class ConfigBackup
{
    public bool Ativo { get; set; } = true;
    public int Manter { get; set; } = 20;
}

/// <summary>Preferências (mesmo conteúdo do preferencias.json do original, sem a pasta).</summary>
public sealed class Preferencias
{
    public Dictionary<string, string> Classes { get; set; } = new();
    public string Tema { get; set; } = "claro";
    public bool Recolhido { get; set; }
    public List<string> AbasOff { get; set; } = new();
    public ConfigBackup Backup { get; set; } = new();

    public static Preferencias DeJson(string? json)
    {
        var p = new Preferencias();
        if (string.IsNullOrWhiteSpace(json)) return p;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject o) return p;
            if (o["classes"] is JsonObject c)
                foreach (var kv in c)
                {
                    var v = kv.Value?.GetValue<string>();
                    if (v != null && Core.Classes.Rotulo.ContainsKey(v)) p.Classes[kv.Key.Trim().ToUpperInvariant()] = v;
                }
            var tema = o["tema"]?.GetValue<string>();
            p.Tema = tema is "claro" or "escuro" ? tema : "claro";
            p.Recolhido = o["menu_recolhido"]?.GetValue<bool>() ?? false;
            var validas = Abas.Todas.Select(a => a.Chave).ToHashSet();
            if (o["abas_desativadas"] is JsonArray ab) p.AbasOff = ab.Select(x => x?.GetValue<string>() ?? "").Where(validas.Contains).ToList();
            if (o["backup"] is JsonObject b)
            {
                if (b["ativo"] != null) p.Backup.Ativo = b["ativo"]!.GetValue<bool>();
                if (b["manter"] != null) p.Backup.Manter = Math.Clamp((int)b["manter"]!.GetValue<double>(), 1, 365);
            }
        }
        catch { return new Preferencias(); }
        return p;
    }

    /// <summary>Grava só o que difere da regra padrão (se a regra padrão mudar, o resto acompanha).</summary>
    public string ParaJson()
    {
        var dif = new JsonObject();
        foreach (var (k, v) in Classes) if (v != Core.Classes.Padrao(k)) dif[k] = v;
        return new JsonObject
        {
            ["classes"] = dif, ["tema"] = Tema, ["menu_recolhido"] = Recolhido,
            ["abas_desativadas"] = new JsonArray(AbasOff.Select(a => (JsonNode?)a).ToArray()),
            ["backup"] = new JsonObject { ["ativo"] = Backup.Ativo, ["manter"] = Backup.Manter },
        }.ToJsonString(Json.Opcoes);
    }
}

public static class Json
{
    public static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
}

public sealed class InfoBackup
{
    public string Nome { get; set; } = "";
    public string CriadoEm { get; set; } = "";
    public string Motivo { get; set; } = "";
    public List<string> Arquivos { get; set; } = new();
    public string Hash { get; set; } = "";
}

/// <summary>Dados gravados no navegador (histórico, preferências, pareceres padrão) e backups automáticos.</summary>
public sealed class Dados
{
    public const string ChHistorico = "historico_qualidade.json", ChPrefs = "preferencias.json", ChPareceres = "pareceres_padrao.json";
    public static readonly string[] Arquivos = { ChHistorico, ChPrefs, ChPareceres };
    const string ChIndiceBackup = "backups/indice";
    readonly Navegador _nav;
    public Dados(Navegador nav) { _nav = nav; }

    public async Task<Preferencias> CarregarPreferencias() => Preferencias.DeJson(await _nav.Ler(ChPrefs));
    public async Task<bool> SalvarPreferencias(Preferencias p) => await _nav.Gravar(ChPrefs, p.ParaJson());

    public async Task<List<RegistroHist>> CarregarHistorico()
    {
        var j = await _nav.Ler(ChHistorico);
        try { return Historico.Ler(j); }
        catch
        {
            // arquivo corrompido: preserva uma cópia e começa vazio
            if (j != null) await _nav.Gravar(ChHistorico + ".corrompido", j);
            await _nav.Apagar(ChHistorico);
            return new();
        }
    }

    public async Task SalvarHistorico(List<RegistroHist> regs)
    {
        if (!await _nav.Gravar(ChHistorico, Historico.Gravar(regs)))
            throw new IOException("O navegador não permitiu gravar (armazenamento cheio ou bloqueado).");
    }

    public async Task<ConfigPareceres> CarregarPareceres() => ConfigPareceres.DeJson(await _nav.Ler(ChPareceres));
    public async Task<bool> SalvarPareceres(ConfigPareceres c) => await _nav.Gravar(ChPareceres, c.ParaJson());

    // ---------------- backup ----------------
    async Task<Dictionary<string, string>> DadosAtuais()
    {
        var d = new Dictionary<string, string>();
        foreach (var a in Arquivos)
        {
            var v = await _nav.Ler(a);
            if (v != null) d[a] = v;
        }
        return d;
    }

    static string HashDe(Dictionary<string, string> d)
        => Hash.De(string.Concat(Arquivos.Where(d.ContainsKey).Select(a => a + "\u0000" + d[a] + "\u0001")));

    public async Task<List<InfoBackup>> ListarBackups()
    {
        var j = await _nav.Ler(ChIndiceBackup);
        if (string.IsNullOrWhiteSpace(j)) return new();
        try
        {
            var lista = (JsonNode.Parse(j) as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(o => new InfoBackup
            {
                Nome = (string?)o["Nome"] ?? "", CriadoEm = (string?)o["CriadoEm"] ?? "", Motivo = (string?)o["Motivo"] ?? "",
                Arquivos = (o["Arquivos"] as JsonArray)?.Select(x => (string?)x ?? "").ToList() ?? new(), Hash = (string?)o["Hash"] ?? "",
            }).ToList();
            return lista.OrderByDescending(b => b.Nome, StringComparer.Ordinal).ToList();
        }
        catch { return new(); }
    }

    async Task SalvarIndice(List<InfoBackup> l) => await _nav.Gravar(ChIndiceBackup, new JsonArray(l.Select(b => (JsonNode?)new JsonObject
    {
        ["Nome"] = b.Nome, ["CriadoEm"] = b.CriadoEm, ["Motivo"] = b.Motivo,
        ["Arquivos"] = new JsonArray(b.Arquivos.Select(a => (JsonNode?)a).ToArray()), ["Hash"] = b.Hash,
    }).ToArray()).ToJsonString());

    async Task Podar(int manter)
    {
        var l = await ListarBackups();
        foreach (var b in l.Skip(Math.Max(1, manter))) await _nav.Apagar("backups/" + b.Nome);
        await SalvarIndice(l.Take(Math.Max(1, manter)).ToList());
    }

    /// <summary>Copia os dados para um backup datado. Não repete se nada mudou. Devolve o nome criado ou null.</summary>
    public async Task<string?> FazerBackup(string motivo, Preferencias prefs, bool forcar = false)
    {
        var atuais = await DadosAtuais();
        if (atuais.Count == 0) return null;
        var h = HashDe(atuais);
        var lista = await ListarBackups();
        if (!forcar && lista.Count > 0 && lista[0].Hash == h) return null;
        var agora = DateTime.Now;
        var nome = agora.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
        while (lista.Any(b => b.Nome == nome)) { agora = agora.AddSeconds(1); nome = agora.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture); }
        var pacote = new JsonObject();
        foreach (var (k, v) in atuais) pacote[k] = v;
        await _nav.Gravar("backups/" + nome, pacote.ToJsonString());
        lista.Insert(0, new InfoBackup
        {
            Nome = nome, CriadoEm = agora.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), Motivo = motivo,
            Arquivos = atuais.Keys.ToList(), Hash = h,
        });
        await SalvarIndice(lista);
        await Podar(prefs.Backup.Manter);
        return nome;
    }

    /// <summary>Restaura os arquivos de um backup (antes, guarda uma cópia do estado atual).</summary>
    public async Task Restaurar(string nome, Preferencias prefs)
    {
        var j = await _nav.Ler("backups/" + nome) ?? throw new IOException("Backup não encontrado.");
        await FazerBackup("antes de restaurar", prefs, true);
        if (JsonNode.Parse(j) is not JsonObject o) throw new IOException("Backup inválido.");
        foreach (var a in Arquivos)
            if (o[a] != null) await _nav.Gravar(a, o[a]!.GetValue<string>());
    }

    /// <summary>Pacote com todos os dados, para guardar fora do navegador ou levar para outro computador.</summary>
    public async Task<string> Exportar()
    {
        var o = new JsonObject { ["formato"] = "qsmart-dados", ["versao"] = 1, ["exportado_em"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) };
        foreach (var (k, v) in await DadosAtuais()) o[k] = JsonNode.Parse(v);
        return o.ToJsonString(Json.Opcoes);
    }

    /// <summary>Importa um pacote do QSmart ou os arquivos do programa original
    /// (historico_qualidade.json, preferencias.json, pareceres_padrao.json). Devolve o que foi importado.</summary>
    public async Task<List<string>> Importar(string nomeArquivo, string json, Preferencias prefs)
    {
        var n = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("O arquivo não é um JSON válido.");
        var importados = new List<string>();
        await FazerBackup("antes de importar", prefs, true);
        if ((string?)n["formato"] == "qsmart-dados")
        {
            foreach (var a in Arquivos)
                if (n[a] != null) { await _nav.Gravar(a, n[a]!.ToJsonString(Json.Opcoes)); importados.Add(a); }
            return importados;
        }
        string? destino = null;
        if (n["registros"] != null) destino = ChHistorico;
        else if (n["padroes"] != null || n["documento"] != null || n["ignorar"] != null) destino = ChPareceres;
        else if (n["classes"] != null || n["tema"] != null || n["abas_desativadas"] != null) destino = ChPrefs;
        else if (Arquivos.Contains(Path.GetFileName(nomeArquivo))) destino = Path.GetFileName(nomeArquivo);
        if (destino == null) throw new InvalidDataException("Não reconheci o conteúdo: use um arquivo exportado pelo QSmart ou historico_qualidade.json, preferencias.json ou pareceres_padrao.json.");
        if (destino == ChHistorico) Historico.Ler(json);                // valida
        await _nav.Gravar(destino, json);
        importados.Add(destino);
        return importados;
    }
}
