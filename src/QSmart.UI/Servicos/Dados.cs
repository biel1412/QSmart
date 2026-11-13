using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using QSmart.Core;

namespace QSmart.UI.Servicos;

public sealed class ConfigBackup
{
    public bool Ativo { get; set; } = true;
    public int Manter { get; set; } = 20;
    /// <summary>Caminho de pasta gravado pelo programa original (mantido para não se perder quando os dois usam a mesma pasta).</summary>
    public string PastaOriginal { get; set; } = "";
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
                if (b["pasta"] is JsonValue pv && pv.TryGetValue<string>(out var ps)) p.Backup.PastaOriginal = ps;
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
            ["backup"] = new JsonObject { ["ativo"] = Backup.Ativo, ["manter"] = Backup.Manter, ["pasta"] = Backup.PastaOriginal },
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

/// <summary>Dados (histórico, preferências, pareceres padrão) e backups automáticos.
/// Ficam na pasta de dados escolhida pelo usuário (como no original) ou, sem pasta escolhida, no próprio navegador.</summary>
public sealed class Dados
{
    public const string ChHistorico = "historico_qualidade.json", ChPrefs = "preferencias.json", ChPareceres = "pareceres_padrao.json";
    public static readonly string[] Arquivos = { ChHistorico, ChPrefs, ChPareceres };
    const string ChIndiceBackup = "backups/indice";
    readonly Navegador _nav;
    public Dados(Navegador nav) { _nav = nav; }

    // ---------------- pastas ----------------
    /// <summary>O navegador permite escolher pastas (Chrome e Edge).</summary>
    public bool PastaSuportada { get; private set; }
    /// <summary>Nome da pasta de dados em uso (null = dados no navegador).</summary>
    public string? PastaDados { get; private set; }
    /// <summary>Pasta de dados escolhida, mas ainda sem permissão de acesso nesta sessão.</summary>
    public string? PastaDadosPendente { get; private set; }
    public string? PastaBackup { get; private set; }
    public string? PastaBackupPendente { get; private set; }

    public async Task Iniciar()
    {
        try { PastaSuportada = await _nav.PastaSuportada(); } catch { PastaSuportada = false; }
        await AtualizarPastas();
    }

    public async Task AtualizarPastas()
    {
        PastaDados = PastaDadosPendente = PastaBackup = PastaBackupPendente = null;
        if (!PastaSuportada) return;
        foreach (var papel in new[] { "dados", "backup" })
        {
            string est, nome;
            try { est = await _nav.PastaEstado(papel); nome = await _nav.PastaNome(papel) ?? ""; }
            catch { continue; }
            if (est == "nenhuma") continue;
            var ok = est == "granted";
            if (papel == "dados") { if (ok) PastaDados = nome; else PastaDadosPendente = nome; }
            else { if (ok) PastaBackup = nome; else PastaBackupPendente = nome; }
        }
    }

    /// <summary>Pede acesso às pastas escolhidas (precisa de um clique do usuário). Devolve se ficou tudo liberado.</summary>
    public async Task<bool> PermitirPastas()
    {
        if (PastaDadosPendente != null) await _nav.PastaPermitir("dados");
        if (PastaBackupPendente != null) await _nav.PastaPermitir("backup");
        await AtualizarPastas();
        return PastaDadosPendente == null && PastaBackupPendente == null;
    }

    /// <summary>Texto da pasta de dados, para as Configurações.</summary>
    public string DescricaoPastaDados => PastaDados != null ? $"Pasta “{PastaDados}”"
        : PastaDadosPendente != null ? $"Pasta “{PastaDadosPendente}” (sem acesso nesta sessão: os dados estão vindo deste navegador)"
        : "Neste navegador (nenhuma pasta escolhida)";

    public string DescricaoPastaBackup => PastaBackup != null ? $"Pasta “{PastaBackup}”"
        : PastaBackupPendente != null ? $"Pasta “{PastaBackupPendente}” (sem acesso nesta sessão)"
        : PastaDados != null ? $"Pasta “{PastaDados}/backups”  (padrão)"
        : "Neste navegador  (padrão)";

    /// <summary>Escolhe a pasta de dados, copiando para ela os arquivos que ainda não existirem lá
    /// (definir_pasta_dados do original). Devolve null se cancelou, senão (nome, copiados).</summary>
    public async Task<(string nome, List<string> copiados)?> DefinirPastaDados()
    {
        var nome = await _nav.PastaEscolher("dados");      // lança erro se a pasta não aceitar gravação
        if (nome == null) return null;
        var copiados = new List<string>();
        foreach (var a in Arquivos)
        {
            var atual = await Ler(a);
            if (atual == null) continue;
            if (await _nav.PastaLer("dados", a, true) != null) continue;
            await _nav.PastaGravar("dados", a, atual, true);
            copiados.Add(a);
        }
        await _nav.PastaConfirmar("dados");
        await AtualizarPastas();
        return (nome, copiados);
    }

    /// <summary>Volta a guardar os dados no navegador (copia para ele os arquivos que ainda não existirem lá).</summary>
    public async Task<List<string>> UsarNavegadorParaDados()
    {
        var copiados = new List<string>();
        if (PastaDados != null)
            foreach (var a in Arquivos)
            {
                var atual = await Ler(a);
                if (atual == null || await _nav.Ler(a) != null) continue;
                await _nav.Gravar(a, atual);
                copiados.Add(a);
            }
        await _nav.PastaEsquecer("dados");
        await AtualizarPastas();
        return copiados;
    }

    public async Task<string?> DefinirPastaBackup()
    {
        var nome = await _nav.PastaEscolher("backup");
        if (nome == null) return null;
        await _nav.PastaConfirmar("backup");
        await AtualizarPastas();
        return nome;
    }

    public async Task BackupPadrao()
    {
        await _nav.PastaEsquecer("backup");
        await AtualizarPastas();
    }

    // ---------------- leitura e gravação ----------------
    /// <summary>Onde um arquivo de dados fica gravado (para as mensagens).</summary>
    public string Local(string arquivo) => PastaDados != null ? $"Pasta “{PastaDados}” › {arquivo}" : "Neste navegador";

    async Task<string?> Ler(string arquivo) => PastaDados != null ? await _nav.PastaLer("dados", arquivo) : await _nav.Ler(arquivo);
    async Task<bool> Gravar(string arquivo, string texto)
    {
        try { return PastaDados != null ? await _nav.PastaGravar("dados", arquivo, texto) : await _nav.Gravar(arquivo, texto); }
        catch { return false; }
    }
    async Task Apagar(string arquivo)
    {
        if (PastaDados != null) await _nav.PastaApagar("dados", arquivo); else await _nav.Apagar(arquivo);
    }
    public string OndeGrava => PastaDados != null ? $"na pasta “{PastaDados}”" : "neste navegador";

    public async Task<Preferencias> CarregarPreferencias()
    {
        try { return Preferencias.DeJson(await Ler(ChPrefs)); } catch { return new Preferencias(); }
    }
    public async Task<bool> SalvarPreferencias(Preferencias p) => await Gravar(ChPrefs, p.ParaJson());

    public async Task<List<RegistroHist>> CarregarHistorico()
    {
        string? j;
        try { j = await Ler(ChHistorico); } catch { return new(); }
        try { return Historico.Ler(j); }
        catch
        {
            // arquivo corrompido: preserva uma cópia e começa vazio
            try
            {
                if (j != null) await Gravar(ChHistorico + ".corrompido", j);
                await Apagar(ChHistorico);
            }
            catch { }
            return new();
        }
    }

    public async Task SalvarHistorico(List<RegistroHist> regs)
    {
        if (!await Gravar(ChHistorico, Historico.Gravar(regs)))
            throw new IOException($"Não foi possível gravar {OndeGrava} (sem permissão, pasta indisponível ou armazenamento cheio).");
    }

    public async Task<ConfigPareceres> CarregarPareceres()
    {
        try { return ConfigPareceres.DeJson(await Ler(ChPareceres)); } catch { return new ConfigPareceres(); }
    }
    public async Task<bool> SalvarPareceres(ConfigPareceres c) => await Gravar(ChPareceres, c.ParaJson());

    // ---------------- backup ----------------
    /// <summary>Onde ficam os backups: pasta de backup escolhida; senão a subpasta “backups” da pasta de dados; senão o navegador.</summary>
    (string papel, string sub)? DestinoBackup => PastaBackup != null ? ("backup", "") : PastaDados != null ? ("dados", "backups") : null;
    static string Junta(string sub, string nome) => sub == "" ? nome : sub + "/" + nome;

    async Task<Dictionary<string, string>> DadosAtuais()
    {
        var d = new Dictionary<string, string>();
        foreach (var a in Arquivos)
        {
            var v = await Ler(a);
            if (v != null) d[a] = v;
        }
        return d;
    }

    static string HashDe(Dictionary<string, string> d)
        => Hash.De(string.Concat(Arquivos.Where(d.ContainsKey).Select(a => a + "\u0000" + d[a] + "\u0001")));

    public async Task<List<InfoBackup>> ListarBackups()
    {
        var dest = DestinoBackup;
        if (dest != null)
        {
            var (papel, sub) = dest.Value;
            var lista = new List<InfoBackup>();
            string[] nomes;
            try { nomes = await _nav.PastaListar(papel, sub); } catch { return lista; }
            foreach (var n in nomes)
            {
                try
                {
                    var man = await _nav.PastaLer(papel, Junta(sub, n + "/manifest.json"));
                    if (man == null || JsonNode.Parse(man) is not JsonObject m) continue;
                    lista.Add(new InfoBackup
                    {
                        Nome = n, CriadoEm = (string?)m["criado_em"] ?? "", Motivo = (string?)m["motivo"] ?? "",
                        Arquivos = (m["arquivos"] as JsonArray)?.Select(x => (string?)x ?? "").ToList() ?? new(), Hash = (string?)m["hash"] ?? "",
                    });
                }
                catch { }
            }
            return lista.OrderByDescending(b => b.Nome, StringComparer.Ordinal).ToList();
        }
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
        var dest = DestinoBackup;
        if (dest != null)
        {
            foreach (var b in l.Skip(Math.Max(1, manter)))
                try { await _nav.PastaApagar(dest.Value.papel, Junta(dest.Value.sub, b.Nome)); } catch { }
            return;
        }
        foreach (var b in l.Skip(Math.Max(1, manter))) await _nav.Apagar("backups/" + b.Nome);
        await SalvarIndice(l.Take(Math.Max(1, manter)).ToList());
    }

    /// <summary>Copia os dados para um backup datado. Não repete se nada mudou. Devolve onde foi salvo, ou null.</summary>
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
        var criado = agora.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var dest = DestinoBackup;
        if (dest != null)
        {
            // mesmo formato do original: uma subpasta datada com os arquivos e o manifest.json
            var (papel, sub) = dest.Value;
            var pastaBk = Junta(sub, nome);
            foreach (var (k, v) in atuais) await _nav.PastaGravar(papel, pastaBk + "/" + k, v);
            await _nav.PastaGravar(papel, pastaBk + "/manifest.json", new JsonObject
            {
                ["criado_em"] = criado, ["motivo"] = motivo,
                ["arquivos"] = new JsonArray(atuais.Keys.Select(a => (JsonNode?)a).ToArray()), ["hash"] = h,
            }.ToJsonString(Json.Opcoes));
            await Podar(prefs.Backup.Manter);
            return (papel == "backup" ? PastaBackup : PastaDados + "/backups") + "/" + nome;
        }
        var pacote = new JsonObject();
        foreach (var (k, v) in atuais) pacote[k] = v;
        await _nav.Gravar("backups/" + nome, pacote.ToJsonString());
        lista.Insert(0, new InfoBackup { Nome = nome, CriadoEm = criado, Motivo = motivo, Arquivos = atuais.Keys.ToList(), Hash = h });
        await SalvarIndice(lista);
        await Podar(prefs.Backup.Manter);
        return nome;
    }

    /// <summary>Restaura os arquivos de um backup (antes, guarda uma cópia do estado atual).</summary>
    public async Task Restaurar(string nome, Preferencias prefs)
    {
        var dest = DestinoBackup;
        var conteudo = new Dictionary<string, string>();
        if (dest != null)
        {
            foreach (var a in Arquivos)
            {
                var v = await _nav.PastaLer(dest.Value.papel, Junta(dest.Value.sub, nome + "/" + a));
                if (v != null) conteudo[a] = v;
            }
            if (conteudo.Count == 0) throw new IOException("Backup não encontrado.");
        }
        else
        {
            var j = await _nav.Ler("backups/" + nome) ?? throw new IOException("Backup não encontrado.");
            if (JsonNode.Parse(j) is not JsonObject o) throw new IOException("Backup inválido.");
            foreach (var a in Arquivos) if (o[a] != null) conteudo[a] = o[a]!.GetValue<string>();
        }
        await FazerBackup("antes de restaurar", prefs, true);
        foreach (var (a, v) in conteudo)
            if (!await Gravar(a, v)) throw new IOException($"Não foi possível gravar {a} {OndeGrava}.");
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
                if (n[a] != null) { await Gravar(a, n[a]!.ToJsonString(Json.Opcoes)); importados.Add(a); }
            return importados;
        }
        string? destino = null;
        if (n["registros"] != null) destino = ChHistorico;
        else if (n["padroes"] != null || n["documento"] != null || n["ignorar"] != null) destino = ChPareceres;
        else if (n["classes"] != null || n["tema"] != null || n["abas_desativadas"] != null) destino = ChPrefs;
        else if (Arquivos.Contains(Path.GetFileName(nomeArquivo))) destino = Path.GetFileName(nomeArquivo);
        if (destino == null) throw new InvalidDataException("Não reconheci o conteúdo: use um arquivo exportado pelo QSmart ou historico_qualidade.json, preferencias.json ou pareceres_padrao.json.");
        if (destino == ChHistorico) Historico.Ler(json);                // valida
        if (!await Gravar(destino, json)) throw new IOException($"Não foi possível gravar {destino} {OndeGrava}.");
        importados.Add(destino);
        return importados;
    }
}
