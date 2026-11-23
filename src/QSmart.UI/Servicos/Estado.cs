using Microsoft.JSInterop;
using QSmart.Core;
using QSmart.Core.Planilhas;
using QSmart.Core.Coordenacao;

namespace QSmart.UI.Servicos;

/// <summary>Estado de toda a aplicação (equivalente aos atributos da janela principal do original).
/// Fica vivo enquanto a aba do navegador estiver aberta, então trocar de página não perde nada.</summary>
public sealed class Estado
{
    public Navegador Nav { get; }
    public Dados Dados { get; }
    public Janelas Janelas { get; }
    public Estado(Navegador nav, Dados dados, Janelas janelas) { Nav = nav; Dados = dados; Janelas = janelas; Conta = new Conta(this); Sino = new Sininho(this); }
    public Conta Conta { get; }
    public Sininho Sino { get; }

    public event Action? Mudou;
    public void Notificar() => Mudou?.Invoke();

    // ----- preferências -----
    public Preferencias Prefs { get; private set; } = new();
    public Dictionary<string, string> ClassesProc => Prefs.Classes;
    public bool Iniciado { get; private set; }
    public string UltimoBackupTexto { get; private set; } = "";

    // ----- carregamento em segundo plano -----
    int _tarefas;
    public bool Carregando => _tarefas > 0;

    // ----- Resumo / Usuários -----
    public ResultadoResumo? Resultado;
    public List<CuboVolume>? CuboResumo;
    public OpcoesVolume? OpcoesResumo;
    public FiltrosVolume FiltrosResumo = new();
    public Arquivo? ArquivoResumo;
    public string SubResumo = "Nenhuma planilha carregada. Clique em “Abrir planilha” para começar.";
    public string Ordem = "mais";
    public string BuscaUsuarios = "";

    // ----- Evolução -----
    public ResultadoInspecao? Evolucao;
    public Arquivo? ArquivoEvolucao;
    public string SubEvo = "Nenhuma planilha de inspeção carregada.";

    // ----- Histórico -----
    public List<RegistroHist> Historico = new();
    public string HistAno = "Todos";
    public HashSet<string> HistSel = new();
    public FiltrosHist HistFiltros = new();

    // ----- Pareceres -----
    public List<string> ParPadroes = new();
    public List<string> ParIgnorar = new(Pareceres.IgnorarInicial);
    public List<string> ParProcExcluidos = new(Pareceres.ProcExcluidosInicial);
    public string ParDocumento = "";
    public bool ParDocGravado;
    public PlanilhaPareceres? ParDf;
    public List<ResultadoParecer>? ParRes;
    public Arquivo? ParArquivo;
    public HashSet<string>? ParUsuariosFiltro;
    public string ParArquivoFiltro = "";
    public string ParDia = "Todos";
    public bool ParAcento;
    public string ParIgnorarTexto = string.Join("\n", Pareceres.IgnorarInicial);
    public string SubPar = "Nenhuma planilha de pareceres carregada.";

    // ----- Inspeção (internas) -----
    public ResultadoInternos? IntRes;
    public string SubInt = "Inspeções do tipo Interno por inspetor e por dia. Abra a planilha de inspeção.";

    // ----- Volume -----
    public BaseCompilados? VolComp;
    public List<string> VolLista = new();
    public string VolListaNome = "";
    public ResultadoVolumeUsuarios? VolRes;
    public string SubVol = "Anexe o compilado e a lista de usuários: o programa mostra quanto cada um executou.";

    // ----- Corredor -----
    public List<ResumoCompilado> CorItens = new();
    public List<(Arquivo arq, int paginas)> CorPdfs = new();
    public string SubCor = "Una vários compilados diários num só (semana, mês…) e junte PDFs.";

    // ----- Monitor -----
    public BaseMonitor? MonBase;
    public ResultadoMonitor? MonRes;
    public List<string> MonSel = new();
    public string? MonFiltro;
    public string SubMon = "Anexe o compilado (diário ou mensal), escolha os colaboradores e informe a jornada.";

    // ----- Depurador -----
    public Bruto? DepBruto;
    public Arquivo? DepArquivo;
    public ResultadoDep? DepRes;
    public string DepModo = "auto";
    public string? DepAba;
    public string? DepIncDesvio, DepIncGrav;
    public string? DepCtxUsado;
    public int DepLimite;
    public string SubDep = "Abra uma planilha para ver, linha a linha, o que o programa lê e o que fica de fora.";

    // ----- Coordenação: pontos de ação -----
    public BaseCompilados? PtsBase;
    public ResultadoPontos? PtsRes;
    public string PtsArquivos = "";
    public double PtsLimite = PontosAcao.LimitePadrao;
    public bool PtsPorDiaTrabalhado = true;
    public int PtsMinDias = 1;
    public string SubPts = "Anexe o compilado do mês: o QSmart calcula a média de ações por dia de cada usuário e mostra quem fica abaixo da meta.";

    // ----- Coordenação: férias e justificativas (guardados só neste navegador, fora da pasta de dados) -----
    // ----- Operação: estratégia do dia
    public QSmart.Core.Operacao.BaseEstrategia? EstBase;
    public QSmart.Core.Operacao.MensagemEstrategia EstMsg = new();
    public string SubEst = "";
    public const string ChEstrategia = "operacao/estrategia.json";
    public async Task SalvarEstMsg() => await Nav.Gravar(ChEstrategia, QSmart.Core.Operacao.Estrategia.Gravar(EstMsg));

    public const string ChFerias = "coordenacao/ferias.json", ChPtsJust = "coordenacao/pontos_justificativas.json";
    /// <summary>Justificativas do Pontos de ação, por mês e usuário.</summary>
    public Dictionary<string, Dictionary<string, string>> PtsJust = new();
    public DadosFerias Ferias = new();

    // ---- Dados pessoais protegidos: cofre do navegador + arquivo com senha na pasta de dados / backups ----
    const string CofreSenha = "senha_pasta";
    string? _senha; bool _senhaLida;
    public bool TemSenhaPasta => _senha != null;
    public async Task<string?> SenhaPasta()
    {
        if (!_senhaLida)
        {
            _senhaLida = true;
            var b = await Nav.CofreLer(CofreSenha);
            if (b != null && b.Length > 0) _senha = System.Text.Encoding.UTF8.GetString(b);
        }
        return _senha;
    }
    public async Task DefinirSenhaPasta(string senha)
    {
        _senha = senha; _senhaLida = true;
        await Nav.CofreGravar(CofreSenha, System.Text.Encoding.UTF8.GetBytes(senha));
    }

    /// <summary>Grava no cofre do navegador e, se houver senha, também na pasta de dados (entra nos backups).</summary>
    public async Task<bool> GuardarSeguro(string cofre, string arquivo, byte[] dados)
    {
        await Nav.CofreGravar(cofre, dados);
        var s = await SenhaPasta();
        if (s == null) return false;
        try
        {
            var c = await Nav.CifrarSenha(dados, s);
            return await Dados.GravarArquivo(arquivo, "QSC1:" + Convert.ToBase64String(c));
        }
        catch { return false; }
    }

    /// <summary>Lê do cofre do navegador; se não houver, procura o arquivo protegido na pasta de dados e pede a senha.</summary>
    public async Task<byte[]?> LerSeguro(string cofre, string arquivo, string oque)
    {
        var b = await Nav.CofreLer(cofre);
        if (b != null && b.Length > 0) return b;
        var t = await Dados.LerArquivo(arquivo);
        if (t == null || !t.StartsWith("QSC1:")) return null;
        byte[] c;
        try { c = Convert.FromBase64String(t[5..]); } catch { return null; }
        var s = await SenhaPasta();
        if (s != null && await Nav.DecifrarSenha(c, s) is { } d0) { await Nav.CofreGravar(cofre, d0); return d0; }
        for (int i = 0; i < 3; i++)
        {
            var sx = await Janelas.Entrada("Dados protegidos", $"Encontrei {oque} salvo(s) {Dados.OndeGrava}, protegido(s) com senha. Digite a senha para abrir neste navegador:", "", senha: true);
            if (string.IsNullOrEmpty(sx)) return null;
            if (await Nav.DecifrarSenha(c, sx) is { } d)
            {
                await DefinirSenhaPasta(sx);
                await Nav.CofreGravar(cofre, d);
                return d;
            }
            await Janelas.Erro("Senha", "Senha incorreta.");
        }
        return null;
    }

    public async Task ApagarSeguro(string cofre, string arquivo)
    {
        await Nav.CofreApagar(cofre);
        await Dados.ApagarArquivo(arquivo);
    }

    /// <summary>Pede (duas vezes) a senha que protege os arquivos na pasta de dados.</summary>
    public async Task<bool> PedirNovaSenha()
    {
        var a = await Janelas.Entrada("Proteger com senha", "Crie uma senha para proteger o One a One e o controle de férias na pasta de dados e nos backups (mínimo 6 caracteres). Guarde-a: sem ela os arquivos não abrem em outro computador.", "", senha: true);
        if (string.IsNullOrEmpty(a)) return false;
        if (a.Length < 6) { await Janelas.Aviso("Senha", "Use pelo menos 6 caracteres."); return false; }
        var b = await Janelas.Entrada("Proteger com senha", "Digite a senha de novo:", "", senha: true);
        if (b != a) { await Janelas.Aviso("Senha", "As senhas não são iguais."); return false; }
        await DefinirSenhaPasta(a);
        if (FeriasAuto != null) await SalvarFeriasAuto();
        if (OneBase != null) await SalvarOne();
        return true;
    }

    // ---- Feriados nacionais (BrasilAPI, com lista própria quando estiver sem internet) ----
    public List<QSmart.Core.FeriadoDia> FeriadosLista { get; private set; } = new();
    bool _feriadosLidos;
    public async Task CarregarFeriados()
    {
        if (_feriadosLidos) return;
        _feriadosLidos = true;
        int ano = DateTime.Today.Year;
        var l = new List<QSmart.Core.FeriadoDia>();
        foreach (var y in new[] { ano, ano + 1 })
        {
            var lista = QSmart.Core.Feriados.Locais(y);
            try
            {
                var cache = await Nav.Ler($"feriados_{y}");
                if (!string.IsNullOrEmpty(cache)) lista = QSmart.Core.Feriados.LerBrasilApi(cache);
            }
            catch { }
            l.AddRange(lista);
        }
        FeriadosLista = l;
        Notificar();
        // atualiza pela BrasilAPI em segundo plano
        var novos = new List<QSmart.Core.FeriadoDia>();
        bool mudou = false;
        foreach (var y in new[] { ano, ano + 1 })
        {
            try
            {
                var r = await Nav.Get(QSmart.Core.Feriados.Url + y);
                var lista = r.Status == 200 ? QSmart.Core.Feriados.LerBrasilApi(r.Texto) : new();
                if (lista.Count > 0) { await Nav.Gravar($"feriados_{y}", r.Texto); novos.AddRange(lista); mudou = true; continue; }
            }
            catch { }
            novos.AddRange(FeriadosLista.Where(f => f.Data.Year == y));
        }
        if (mudou) { FeriadosLista = novos; Notificar(); }
    }

    // ---- Modo desenvolvedor e central de chaves de IA ----
    /// <summary>Liberado só nesta sessão (até fechar ou recarregar a página).</summary>
    public bool DevLiberado;
    const string HashDev = "178374ad0c2ae6657e0a1a2436889293ddb8001196822b5d4c95a0b976e10469";
    /// <summary>Bloqueio simples para o usuário comum (não é segurança forte: o app roda todo no navegador).</summary>
    public bool EntrarDev(string usuario, string senha)
    {
        var h = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(usuario.Trim().ToLowerInvariant() + "\n" + senha))).ToLowerInvariant();
        DevLiberado = h == HashDev;
        return DevLiberado;
    }

    public const string CofreChaveIA = "groq_chave", CofreModeloIA = "groq_modelo";
    public string? ChaveIA { get; private set; }
    public string ModeloIA { get; private set; } = QSmart.Core.Relacionamento.AnaliseIA.ModeloPadrao;
    bool _iaLida;
    public async Task<bool> IAConfigurada()
    {
        if (!_iaLida)
        {
            _iaLida = true;
            try
            {
                var c = await Nav.CofreLer(CofreChaveIA);
                if (c is { Length: > 0 }) ChaveIA = System.Text.Encoding.UTF8.GetString(c);
                var m = await Nav.CofreLer(CofreModeloIA);
                if (m is { Length: > 0 }) ModeloIA = System.Text.Encoding.UTF8.GetString(m);
            }
            catch { }
        }
        return IAPronta;
    }
    /// <summary>IA disponível: chave própria neste navegador ou a chave guardada no intermediário (Cloudflare).</summary>
    public bool IAPronta => !string.IsNullOrEmpty(ChaveIA) || IAPeloIntermediario;
    /// <summary>Usa a chave do Groq guardada no intermediário quando a máquina não tem chave própria.</summary>
    public const bool IAPeloIntermediario = true;
    public static string UrlIAIntermediario => ProxyPadrao + "/groq/openai/v1/chat/completions";
    /// <summary>Chama a IA: direto com a chave deste navegador, ou pelo intermediário (que põe a chave dele).</summary>
    public async Task<Navegador.RespostaHttp> ChamarIA(string corpo)
    {
        await IAConfigurada();
        return !string.IsNullOrEmpty(ChaveIA)
            ? await Nav.IA(QSmart.Core.Relacionamento.AnaliseIA.Url, ChaveIA, corpo)
            : await Nav.IA(UrlIAIntermediario, "", corpo);
    }
    public async Task SalvarChaveIA(string chave, string modelo)
    {
        await Nav.CofreGravar(CofreChaveIA, System.Text.Encoding.UTF8.GetBytes(chave));
        await Nav.CofreGravar(CofreModeloIA, System.Text.Encoding.UTF8.GetBytes(modelo));
        ChaveIA = chave; ModeloIA = modelo; _iaLida = true;
        Notificar();
    }
    public async Task RemoverChaveIA()
    {
        await Nav.CofreApagar(CofreChaveIA);
        ChaveIA = null; _iaLida = true;
        Notificar();
    }
    // ---- Portal da Transparência (chave gratuita, usada pelo Radar Smart360) ----
    public const string CofreChaveTransp = "transp_chave";
    public string? ChaveTransp { get; private set; }
    bool _transpLida;
    public async Task<bool> TranspConfigurada()
    {
        if (!_transpLida)
        {
            try { var c = await Nav.CofreLer(CofreChaveTransp); if (c is { Length: > 0 }) ChaveTransp = System.Text.Encoding.UTF8.GetString(c); } catch { }
            try { var x = await Nav.CofreLer(CofreProxyTransp); if (x is { Length: > 0 }) ProxyTransp = System.Text.Encoding.UTF8.GetString(x); } catch { }
            _transpLida = true;
        }
        return PortalPronto;
    }
    /// <summary>Intermediário (proxy) para o Portal: o Portal não aceita consulta direta do navegador.</summary>
    public const string CofreProxyTransp = "transp_proxy";
    /// <summary>Intermediário padrão (Cloudflare Worker com a chave guardada nele): funciona em qualquer máquina sem configurar.</summary>
    public const string ProxyPadrao = "https://qsmart-portal.gabrielmarinmilitz.workers.dev";
    public string? ProxyTransp { get; private set; } = ProxyPadrao;
    /// <summary>Há como consultar o Portal: chave neste navegador ou intermediário (que pode guardar a chave).</summary>
    public bool PortalPronto => !string.IsNullOrEmpty(ChaveTransp) || !string.IsNullOrEmpty(ProxyTransp);
    public async Task SalvarProxyTransp(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) { await Nav.CofreApagar(CofreProxyTransp); ProxyTransp = ProxyPadrao; return; }
        url = url.Trim().TrimEnd('/', '.', ' ');
        await Nav.CofreGravar(CofreProxyTransp, System.Text.Encoding.UTF8.GetBytes(url));
        ProxyTransp = url;
    }
    /// <summary>Endereço da consulta ao Portal, passando pelo intermediário se houver.</summary>
    public string UrlPortal(string url) => ProxyTransp is { Length: > 0 } px ? url.Replace(QSmart.Core.Relacionamento.Radar.PortalBase, px + "/api-de-dados/") : url;
    /// <summary>Consulta o Portal. Se a chave deste navegador for recusada, tenta de novo sem ela (o intermediário usa a chave dele).</summary>
    public async Task<Navegador.RespostaHttp> GetPortal(string url)
    {
        var r = await Nav.GetH(UrlPortal(url), CabecalhoPortal());
        if (r.Status == 401 && !string.IsNullOrEmpty(ChaveTransp) && ProxyTransp != null)
            r = await Nav.GetH(UrlPortal(url), new Dictionary<string, string> { ["Accept"] = "application/json" });
        return r;
    }
    public Dictionary<string, string> CabecalhoPortal()
    {
        var d = new Dictionary<string, string> { ["Accept"] = "application/json" };
        if (!string.IsNullOrEmpty(ChaveTransp)) d["chave-api-dados"] = ChaveTransp;
        return d;
    }
    public async Task SalvarChaveTransp(string chave)
    {
        await Nav.CofreGravar(CofreChaveTransp, System.Text.Encoding.UTF8.GetBytes(chave));
        ChaveTransp = chave; _transpLida = true;
    }
    public async Task RemoverChaveTransp() { await Nav.CofreApagar(CofreChaveTransp); ChaveTransp = null; _transpLida = true; }

    // ---- Radar Smart360: histórico de CNPJ (criptografado; CPF nunca é salvo) ----
    public const string CofreRadar = "radar360";
    public QSmart.Core.Relacionamento.BaseRadar? Radar { get; private set; }
    public async Task<QSmart.Core.Relacionamento.BaseRadar> CarregarRadar()
    {
        if (Radar != null) return Radar;
        try { Radar = QSmart.Core.Relacionamento.BaseRadar.DeBytes(await Nav.CofreLer(CofreRadar)); } catch { Radar = new(); }
        return Radar;
    }
    public async Task SalvarRadar() { if (Radar != null) await Nav.CofreGravar(CofreRadar, Radar.ParaBytes()); }

    public const string MsgSemIA = "Esta ferramenta ainda não está liberada nesta máquina. Contate o suporte para configurar sua máquina para o uso total das ferramentas do QSmart.";

    // ---- One a One: no banco do QSmart (cifrado no servidor). A planilha serve só para exportar. ----
    public QSmart.Core.Coordenacao.BaseOO? OneBase;
    /// <summary>Erro ao abrir (sem acesso, sem internet…).</summary>
    public string? OneErro;
    public bool OneVeTudo;
    /// <summary>Nome da coordenação de quem está vendo (vazio para o administrador).</summary>
    public string OneCoordenacao = "";
    /// <summary>Pode criar, renomear e excluir lideranças: administrador ou coordenador.</summary>
    public bool OneGere => OneVeTudo || Conta.Perfil?.Papel == "coordenador";
    public string OneMinhaLideranca = "";
    public bool OneCarregando;
    const string CofreOne = "one_a_one";
    const string IdMeta = "__base";
    readonly Dictionary<string, string> _oneSinc = new();
    readonly SemaphoreSlim _oneTrava = new(1, 1);

    static string Ser(QSmart.Core.Coordenacao.BaseOO b) => System.Text.Encoding.UTF8.GetString(QSmart.Core.Coordenacao.OneAOne.ParaBytes(b));
    static string SerPessoa(QSmart.Core.Coordenacao.PessoaOO p) => Ser(new QSmart.Core.Coordenacao.BaseOO { Pessoas = { p } });
    static string SerMeta(QSmart.Core.Coordenacao.BaseOO b) => Ser(new QSmart.Core.Coordenacao.BaseOO { Liderancas = b.Liderancas, Fotos = b.Fotos });

    public async Task CarregarOne(bool forcar = false)
    {
        if (!Conta.Logado) return;
        if (OneBase != null && !forcar) return;
        OneCarregando = true; Notificar();
        try
        {
            var r = await Conta.Chamar("GET", "oo");
            if (!r.Ok || r.Json == null) { OneErro = r.Erro; OneBase = null; return; }
            OneErro = null;
            OneVeTudo = r.Json["veTudo"]?.GetValue<bool>() ?? false;
            OneMinhaLideranca = r.Json["lideranca"]?.GetValue<string>() ?? "";
            OneCoordenacao = r.Json["coordenacao"]?.GetValue<string>() ?? "";
            var equipes = (r.Json["equipes"] as System.Text.Json.Nodes.JsonArray)?.Select(x => x?.GetValue<string>() ?? "").Where(x => x != "").ToList() ?? new();
            var b = new QSmart.Core.Coordenacao.BaseOO();
            _oneSinc.Clear();
            if (r.Json["pessoas"] is System.Text.Json.Nodes.JsonArray a)
                foreach (var x in a)
                {
                    var id = x?["id"]?.GetValue<string>() ?? ""; var dados = x?["dados"]?.GetValue<string>() ?? "";
                    var lida = QSmart.Core.Coordenacao.OneAOne.DeBytes(System.Text.Encoding.UTF8.GetBytes(dados));
                    if (lida == null) continue;
                    if (id == IdMeta) { b.Liderancas = lida.Liderancas; b.Fotos = lida.Fotos; _oneSinc[id] = SerMeta(b); }
                    else if (lida.Pessoas.Count > 0) { var p = lida.Pessoas[0]; p.Id = id; b.Pessoas.Add(p); _oneSinc[id] = SerPessoa(p); }
                }
            b.Pessoas = b.Pessoas.OrderBy(p => p.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
            // equipes da coordenação que ainda não têm ninguém também aparecem como esteira
            foreach (var l in equipes) if (!b.Liderancas.Contains(l, StringComparer.OrdinalIgnoreCase)) b.Liderancas.Add(l);
            if (_oneSinc.ContainsKey(IdMeta)) _oneSinc[IdMeta] = SerMeta(b);
            OneBase = b;
        }
        finally { OneCarregando = false; Notificar(); }
    }

    /// <summary>Envia ao banco só o que mudou desde a última leitura.</summary>
    public async Task SalvarOne()
    {
        if (OneBase == null || !Conta.Logado) return;
        await _oneTrava.WaitAsync();
        var erros = new List<string>();
        try
        {
            var b = OneBase;
            var ids = new HashSet<string>();
            foreach (var p in b.Pessoas.ToList())
            {
                ids.Add(p.Id);
                var json = SerPessoa(p);
                if (_oneSinc.TryGetValue(p.Id, out var ant) && ant == json) continue;
                if (p.Coordenacao == "" && OneCoordenacao != "") p.Coordenacao = OneCoordenacao;
                var r = await Conta.Chamar("PUT", "oo", new System.Text.Json.Nodes.JsonObject { ["id"] = p.Id, ["lideranca"] = p.Lideranca, ["coordenacao"] = p.Coordenacao, ["dados"] = SerPessoa(p) });
                if (r.Ok) _oneSinc[p.Id] = json; else erros.Add($"{p.Nome}: {r.Erro}");
            }
            foreach (var id in _oneSinc.Keys.Where(k => k != IdMeta && !ids.Contains(k)).ToList())
            {
                var r = await Conta.Chamar("DELETE", "oo?id=" + Uri.EscapeDataString(id));
                if (r.Ok) _oneSinc.Remove(id); else erros.Add(r.Erro);
            }
            if (OneGere)
            {
                var meta = SerMeta(b);
                if (!_oneSinc.TryGetValue(IdMeta, out var antM) || antM != meta)
                {
                    var r = await Conta.Chamar("PUT", "oo", new System.Text.Json.Nodes.JsonObject { ["id"] = IdMeta, ["lideranca"] = "", ["dados"] = meta });
                    if (r.Ok) _oneSinc[IdMeta] = meta; else erros.Add(r.Erro);
                }
            }
        }
        finally { _oneTrava.Release(); }
        if (erros.Count > 0)
        {
            await Janelas.Aviso("One a One", "Algumas mudanças não foram salvas no banco:\n\n" + string.Join("\n", erros.Distinct().Take(5)) + "\n\nVou recarregar a carteira do banco.");
            OneBase = null; await CarregarOne(true);
        }
    }

    // ---- Base de colaboradores (planilha da coordenação: nome, admissão, liderança, coordenação) ----
    public async Task<RespostaApi> BaseResumo() => await Conta.Chamar("GET", "oo/base");

    public async Task<RespostaApi> BaseEnviar(List<QSmart.Core.Coordenacao.LinhaBase> linhas, string arquivo, int? coordenador = null)
    {
        var a = new System.Text.Json.Nodes.JsonArray();
        foreach (var l in linhas)
            a.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["nome"] = l.Nome, ["admissao"] = l.Admissao?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                ["lideranca"] = l.Lideranca, ["coordenacao"] = l.Coordenacao,
            });
        var corpo = new System.Text.Json.Nodes.JsonObject { ["linhas"] = a, ["arquivo"] = arquivo };
        if (coordenador is int c) corpo["coordenador_id"] = c;
        var r = await Conta.Chamar("POST", "oo/base", corpo);
        if (r.Ok) { OneBase = null; FeColabs = null; }
        return r;
    }

    // ---- Férias (painel geral): todas as coordenações veem todo mundo; cada uma edita os seus ----
    public List<QSmart.Core.Coordenacao.ColabFer>? FeColabs;
    public bool FeAdmin;
    public string FeCoordenacao = "";
    readonly Dictionary<string, string> _feSinc2 = new();

    public async Task CarregarFerias(bool forcar = false)
    {
        if (!Conta.Logado) return;
        if (FeColabs != null && !forcar) return;
        var r = await Conta.Chamar("GET", "fe");
        if (!r.Ok || r.Json == null) { FeErro = r.Erro; FeColabs = null; Notificar(); return; }
        FeErro = null;
        FeAdmin = r.Json["admin"]?.GetValue<bool>() ?? false;
        FeCoordenacao = r.Json["coordenacao"]?.GetValue<string>() ?? "";
        var lista = new List<QSmart.Core.Coordenacao.ColabFer>();
        _feSinc2.Clear();
        if (r.Json["pessoas"] is System.Text.Json.Nodes.JsonArray a)
            foreach (var x in a)
            {
                string S(string k) => x?[k]?.GetValue<string>() ?? "";
                var c = new QSmart.Core.Coordenacao.ColabFer
                {
                    Id = S("id"), Nome = S("nome"), Lideranca = S("lideranca"), Coordenacao = S("coordenacao"),
                    Admissao = DateOnly.TryParseExact(S("admissao"), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : null,
                    Minha = x?["minha"]?.GetValue<bool>() ?? false,
                };
                var dados = S("dados");
                c.F = dados == "" ? new() : QSmart.Core.Coordenacao.FeriasBanco.DeJson(dados);
                c.F.Admissao ??= c.Admissao;
                if (c.Admissao == null) c.Admissao = c.F.Admissao;
                _feSinc2[c.Id] = QSmart.Core.Coordenacao.FeriasBanco.ParaJson(c.F);
                lista.Add(c);
            }
        FeColabs = lista.OrderBy(c => c.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
        Notificar();
    }

    public async Task<bool> SalvarFerias(QSmart.Core.Coordenacao.ColabFer c)
    {
        var json = QSmart.Core.Coordenacao.FeriasBanco.ParaJson(c.F);
        if (_feSinc2.TryGetValue(c.Id, out var ant) && ant == json) return true;
        var r = await Conta.Chamar("PUT", "fe", new System.Text.Json.Nodes.JsonObject { ["id"] = c.Id, ["dados"] = json });
        if (r.Ok) { _feSinc2[c.Id] = json; _ = Sino.Carregar(); return true; }
        await Janelas.Aviso("Férias", "Não consegui salvar no banco: " + r.Erro + "\n\nVou recarregar as férias do banco.");
        await CarregarFerias(true);
        return false;
    }

    // ---- Férias no banco (mesmas pessoas do One a One) ----
    public Dictionary<string, QSmart.Core.Coordenacao.FeriasPessoa>? FeDados;
    public string? FeErro;
    readonly Dictionary<string, string> _feSinc = new();

    public async Task CarregarFe(bool forcar = false)
    {
        if (!Conta.Logado) return;
        await CarregarOne(forcar);
        if (OneErro != null) { FeErro = OneErro; return; }
        if (FeDados != null && !forcar) return;
        var r = await Conta.Chamar("GET", "fe");
        if (!r.Ok || r.Json == null) { FeErro = r.Erro; FeDados = null; Notificar(); return; }
        FeErro = null;
        var d = new Dictionary<string, QSmart.Core.Coordenacao.FeriasPessoa>();
        _feSinc.Clear();
        if (r.Json["pessoas"] is System.Text.Json.Nodes.JsonArray a)
            foreach (var x in a)
            {
                var id = x?["id"]?.GetValue<string>() ?? ""; var json = x?["dados"]?.GetValue<string>() ?? "";
                if (id == "") continue;
                d[id] = QSmart.Core.Coordenacao.FeriasBanco.DeJson(json);
                _feSinc[id] = QSmart.Core.Coordenacao.FeriasBanco.ParaJson(d[id]);
            }
        FeDados = d;
        Notificar();
    }

    /// <summary>Grava as férias de uma pessoa (só se mudou).</summary>
    public async Task<bool> SalvarFe(string pessoaId)
    {
        if (FeDados == null || !FeDados.TryGetValue(pessoaId, out var f)) return false;
        var json = QSmart.Core.Coordenacao.FeriasBanco.ParaJson(f);
        if (_feSinc.TryGetValue(pessoaId, out var ant) && ant == json) return true;
        var r = await Conta.Chamar("PUT", "fe", new System.Text.Json.Nodes.JsonObject { ["id"] = pessoaId, ["dados"] = json });
        if (r.Ok) { _feSinc[pessoaId] = json; return true; }
        await Janelas.Aviso("Férias", "Não consegui salvar no banco: " + r.Erro + "\n\nVou recarregar as férias do banco.");
        FeDados = null; await CarregarFe(true);
        return false;
    }

    /// <summary>Carteira antiga, guardada só neste navegador (antes do banco), para enviar uma vez.</summary>
    public async Task<QSmart.Core.Coordenacao.BaseOO?> OneLocalAntigo()
    {
        try
        {
            var b = await LerSeguro(CofreOne, Dados.ChOneAOne, "o One a One deste navegador");
            return b != null && QSmart.Core.Coordenacao.OneAOne.DeBytes(b) is { } o && o.Pessoas.Count > 0 ? o : null;
        }
        catch { return null; }
    }

    public async Task<(int ok, int falhas)> EnviarOneLocal(QSmart.Core.Coordenacao.BaseOO antigo)
    {
        var lista = new System.Text.Json.Nodes.JsonArray();
        foreach (var p in antigo.Pessoas) lista.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = p.Id, ["lideranca"] = p.Lideranca, ["dados"] = SerPessoa(p) });
        lista.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = IdMeta, ["lideranca"] = "", ["dados"] = SerMeta(antigo) });
        var r = await Conta.Chamar("POST", "oo/lote", new System.Text.Json.Nodes.JsonObject { ["pessoas"] = lista });
        if (!r.Ok) throw new InvalidOperationException(r.Erro);
        int ok = r.Json?["ok"]?.GetValue<int>() ?? 0, falhas = r.Json?["falhas"]?.GetValue<int>() ?? 0;
        if (falhas == 0) await ApagarSeguro(CofreOne, Dados.ChOneAOne);
        OneBase = null; await CarregarOne(true);
        return (ok, falhas);
    }

    // ---- Controle automatizado de férias: planilha completa, guardada só neste navegador e criptografada ----
    public string FeriasModo = "manual";
    public QSmart.Core.Coordenacao.BaseAuto? FeriasAuto;
    public string FeriasAutoEm = "";
    bool _feriasAutoLido;
    const string CofreFeriasAuto = "ferias_auto";

    public async Task CarregarFeriasAuto()
    {
        if (_feriasAutoLido) return;
        _feriasAutoLido = true;
        try
        {
            var bytes = await LerSeguro(CofreFeriasAuto, Dados.ChFeriasAuto, "o controle de férias");
            if (bytes == null || bytes.Length == 0) return;
            using var doc = System.Text.Json.JsonDocument.Parse(bytes);
            var r = doc.RootElement;
            var b = QSmart.Core.Coordenacao.FeriasAuto.Ler(Convert.FromBase64String(r.GetProperty("original").GetString()!), r.GetProperty("arquivo").GetString() ?? "");
            foreach (var m in r.GetProperty("mudancas").EnumerateObject())
                if (b.Periodos.FirstOrDefault(p => p.Id.ToString() == m.Name) is { } p && DateOnly.TryParse(m.Value.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var d)) p.Inicio = d;
            foreach (var id in r.GetProperty("mantidos").EnumerateArray()) b.Mantidos.Add(id.GetInt32());
            FeriasAuto = b;
            FeriasAutoEm = r.TryGetProperty("em", out var em) ? em.GetString() ?? "" : "";
            Notificar();
        }
        catch { }
    }

    public async Task SalvarFeriasAuto()
    {
        var b = FeriasAuto;
        if (b == null) { await ApagarSeguro(CofreFeriasAuto, Dados.ChFeriasAuto); return; }
        var obj = new Dictionary<string, object>
        {
            ["arquivo"] = b.Arquivo,
            ["em"] = FeriasAutoEm,
            ["original"] = Convert.ToBase64String(b.Original),
            ["mudancas"] = b.Periodos.Where(p => p.Alterado).ToDictionary(p => p.Id.ToString(), p => p.Inicio.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            ["mantidos"] = b.Mantidos.ToList(),
        };
        await GuardarSeguro(CofreFeriasAuto, Dados.ChFeriasAuto, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(obj));
    }
    public bool CoordCarregada;

    public string? FeriasResumo
    {
        get
        {
            if (FeDados == null || OneBase == null) return null;
            var (b, _, _) = QSmart.Core.Coordenacao.FeriasBanco.Montar(OneBase.Pessoas, FeDados);
            if (b.Periodos.Count == 0) return null;
            int at = QSmart.Core.Coordenacao.FeriasAuto.ChoqueTotal(b), ini = QSmart.Core.Coordenacao.FeriasBanco.ChoquePedido(b);
            return $"{b.Periodos.Count} período(s)" + (at > 0 ? $" · {at} dia(s) de choque" : " · sem choque") + (at < ini ? $" · −{Math.Round((ini - at) * 100.0 / ini):0}% de impacto" : "");
        }
    }

    public async Task CarregarCoordenacao()
    {
        if (CoordCarregada) return;
        CoordCarregada = true;
        try { Ferias = Core.Coordenacao.Ferias.Ler(await Nav.Ler(ChFerias)); } catch { Ferias = new(); }
        try { EstMsg = QSmart.Core.Operacao.Estrategia.Ler(await Nav.Ler(ChEstrategia)); } catch { EstMsg = new(); }
        try
        {
            var j = await Dados.LerJustificativas();
            if (j == null && await Nav.Ler(ChPtsJust) is string antigo)          // versão anterior: só no navegador → passa para o arquivo
            {
                j = antigo;
                await Dados.SalvarJustificativas(PontosAcao.GravarJustificativas(PontosAcao.LerJustificativas(antigo)));
            }
            PtsJust = PontosAcao.LerJustificativas(j);
        }
        catch { PtsJust = new(); }
    }

    public async Task SalvarFerias()
    {
        if (!await Nav.Gravar(ChFerias, Core.Coordenacao.Ferias.Gravar(Ferias)))
            await Janelas.Erro("Férias", "Não foi possível salvar neste navegador (armazenamento cheio ou bloqueado).");
        Notificar();
    }

    public async Task SalvarPtsJust()
    {
        if (!await Dados.SalvarJustificativas(PontosAcao.GravarJustificativas(PtsJust)))
            await Janelas.Erro("Pontos de ação", "Não foi possível salvar a justificativa neste navegador.");
        Notificar();
    }


    // ================= início =================
    public async Task Iniciar()
    {
        if (Iniciado) return;
        await Dados.Iniciar();
        if (Dados.PastaDadosPendente != null || Dados.PastaBackupPendente != null)
        {
            // o navegador só libera a pasta escolhida depois de um clique do usuário
            var nomes = string.Join(" e ", new[] { Dados.PastaDadosPendente, Dados.PastaBackupPendente }.Where(n => n != null).Select(n => $"“{n}”"));
            var sim = await Janelas.Pergunta("Pasta de dados", $"O QSmart guarda o histórico, as preferências, os pareceres e os backups na pasta {nomes}.\n\nPermitir o acesso agora?");
            if (!sim || !await Dados.PermitirPastas())
                await Janelas.Aviso("Pasta de dados", "Sem acesso à pasta: nesta sessão os dados vêm deste navegador. Para usar a pasta de novo, recarregue a página ou abra Configurações.");
        }
        Prefs = await Dados.CarregarPreferencias();
        Historico = await Dados.CarregarHistorico();
        CarregarPareceres(await Dados.CarregarPareceres());
        await Nav.Tema(Prefs.Tema);
        await CarregarCoordenacao();
        Iniciado = true;
        await AtualizarRodapeBackup();
        Notificar();
        _ = Task.Run(async () => { await Task.Delay(1500); await BackupAuto("abertura"); });
    }

    void CarregarPareceres(ConfigPareceres c)
    {
        ParPadroes = c.Padroes;
        ParIgnorar = c.Ignorar;
        ParProcExcluidos = c.ProcExcluidos;
        ParDocumento = c.Documento;
        ParDocGravado = c.Documento != "";
        ParIgnorarTexto = string.Join("\n", ParIgnorar);
    }

    /// <summary>Relê histórico, preferências e pareceres (depois de importar ou restaurar um backup).</summary>
    public async Task RecarregarDados()
    {
        Prefs = await Dados.CarregarPreferencias();
        Historico = await Dados.CarregarHistorico();
        HistSel = new();
        CarregarPareceres(await Dados.CarregarPareceres());
        await Nav.Tema(Prefs.Tema);
        if (CuboResumo != null) RecalcularResumo();
        await AtualizarRodapeBackup();
        Notificar();
    }

    public bool AbaAtiva(string chave) => !Prefs.AbasOff.Contains(Abas.PaiDe(chave) ?? chave) && Permitida(chave);

    /// <summary>A seção Coordenação é só de coordenador e administrador; liderança e usuário usam o resto.</summary>
    public bool Permitida(string chave) => Abas.Por(Abas.PaiDe(chave) ?? chave)?.Grupo != "Coordenação" || Conta.Perfil?.Coordenacao == true;

    public async Task SalvarPrefs()
    {
        if (!await Dados.SalvarPreferencias(Prefs))
            await Janelas.Aviso("Preferências", $"Não foi possível salvar as preferências {Dados.OndeGrava}. Elas valem só até fechar a página.");
    }

    public async Task AlternarTema()
    {
        Prefs.Tema = Prefs.Tema == "claro" ? "escuro" : "claro";
        await Nav.Tema(Prefs.Tema);
        await SalvarPrefs();
        Notificar();
    }

    public async Task AlternarMenu()
    {
        Prefs.Recolhido = !Prefs.Recolhido;
        Notificar();
        await SalvarPrefs();
    }

    // ================= backup =================
    public async Task BackupAuto(string motivo)
    {
        try
        {
            var p = await Dados.CarregarPreferencias();
            if (p.Backup.Ativo) await Dados.FazerBackup(motivo, p);
        }
        catch (Exception e) { Console.WriteLine("Backup automático falhou: " + e.Message); }
        await AtualizarRodapeBackup();
        Notificar();
    }

    public async Task AtualizarRodapeBackup()
    {
        try
        {
            var p = await Dados.CarregarPreferencias();
            var bks = await Dados.ListarBackups();
            if (!p.Backup.Ativo) UltimoBackupTexto = "Backup automático desligado";
            else if (bks.Count > 0 && DateTime.TryParse(bks[0].CriadoEm, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d))
                UltimoBackupTexto = "Último backup: " + d.ToString("dd/MM HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            else UltimoBackupTexto = "Nenhum backup ainda";
        }
        catch { UltimoBackupTexto = ""; }
    }

    [JSInvokable]
    public Task AoOcultar() => BackupAuto("fechamento");

    // ================= segundo plano =================
    /// <summary>Roda uma tarefa longa sem travar a tela: texto(seg) atualiza o aviso de leitura.</summary>
    public async Task Executar<T>(Func<Progresso, Task<T>> funcao, Func<T, Task> aoConcluir, Func<Exception, Task> aoFalhar, Action<int, string> texto)
    {
        _tarefas++;
        var t0 = DateTime.UtcNow;
        Progresso? prog = null;
        prog = new Progresso
        {
            Ceder = async () =>
            {
                texto((int)(DateTime.UtcNow - t0).TotalSeconds, prog!.Msg);
                Notificar();
                await Task.Delay(1);
            },
        };
        texto(0, "");
        Notificar();
        await Task.Delay(20);
        T? r = default;
        Exception? erro = null;
        try { r = await funcao(prog); }
        catch (Exception e) { erro = e; }
        finally { _tarefas = Math.Max(0, _tarefas - 1); }
        if (erro != null) await aoFalhar(erro); else await aoConcluir(r!);
        Notificar();
    }

    public static string Mensagem(Exception e) => e is InvalidDataException or IOException ? e.Message : $"{e.GetType().Name}: {e.Message}";

    // ================= Resumo =================
    public void RecalcularResumo()
    {
        if (CuboResumo == null) return;
        Resultado = Volume.CalcularResumo(CuboResumo, ClassesProc, FiltrosResumo, Resultado?.Arquivo ?? ArquivoResumo?.Nome ?? "");
        AtualizarSubResumo();
    }

    public void AtualizarSubResumo()
    {
        var r = Resultado;
        if (r == null) return;
        var filtrado = r.Linhas != r.LinhasTotal ? $" (de {Texto.Fmt(r.LinhasTotal)})" : "";
        SubResumo = $"{r.Arquivo}, {Texto.Fmt(r.Linhas)} registros{filtrado}, {r.Usuarios} usuários";
    }

    public async Task AplicarClasses(Dictionary<string, string> novo)
    {
        foreach (var (k, v) in novo) Prefs.Classes[k] = v;
        if (!await Dados.SalvarPreferencias(Prefs))
            await Janelas.Aviso("Preferências", $"Não foi possível salvar a classificação {Dados.OndeGrava}. Ela vale só até fechar a página.");
        RecalcularResumo();
        Notificar();
    }

    // ================= Pareceres =================
    public async Task<bool> SalvarConfigPareceres()
        => await Dados.SalvarPareceres(new ConfigPareceres { Padroes = ParPadroes, Ignorar = ParIgnorar, ProcExcluidos = ParProcExcluidos, Documento = ParDocumento });

    // ================= Depurador =================
    public CtxDep CtxDepurador() => new()
    {
        Classes = new(ClassesProc), Padroes = ParPadroes.ToList(), Ignorar = ParIgnorar.ToList(), ProcExcluidos = ParProcExcluidos.ToList(),
        Acentos = ParAcento, IncDesvio = DepIncDesvio, IncGrav = DepIncGrav,
    };

    // ================= arquivos =================
    public async Task Salvar(string nome, byte[] bytes, string mime, string titulo = "Arquivo salvo")
    {
        await Nav.Baixar(nome, bytes, mime);
        if (mime == Navegador.MimePdf)
        {
            if (await Janelas.Pergunta(titulo, $"Arquivo baixado: {nome}\n\nDeseja abrir agora?")) await Nav.Abrir(bytes, mime);
        }
        else await Janelas.Info(titulo, $"Arquivo baixado: {nome}\n\nEle está na pasta de downloads do navegador.");
    }

    public static string Agora(string fmt) => DateTime.Now.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture);
}
