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
    public Estado(Navegador nav, Dados dados, Janelas janelas) { Nav = nav; Dados = dados; Janelas = janelas; }

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
            var bytes = await Nav.CofreLer(CofreFeriasAuto);
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
        if (b == null) { await Nav.CofreApagar(CofreFeriasAuto); return; }
        var obj = new Dictionary<string, object>
        {
            ["arquivo"] = b.Arquivo,
            ["em"] = FeriasAutoEm,
            ["original"] = Convert.ToBase64String(b.Original),
            ["mudancas"] = b.Periodos.Where(p => p.Alterado).ToDictionary(p => p.Id.ToString(), p => p.Inicio.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)),
            ["mantidos"] = b.Mantidos.ToList(),
        };
        await Nav.CofreGravar(CofreFeriasAuto, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(obj));
    }
    public bool CoordCarregada;

    public string? FeriasResumo
    {
        get
        {
            if (Ferias.Periodos.Count == 0) return null;
            var al = Core.Coordenacao.Ferias.Alertas(Ferias);
            int ch = al.Count(a => a.Nivel == NivelFerias.Choque), pr = al.Count(a => a.Nivel is NivelFerias.Proximo or NivelFerias.MuitoProximo);
            return $"{Ferias.Periodos.Count} pedido(s)" + (ch > 0 ? $" · {ch} choque(s)" : "") + (pr > 0 ? $" · {pr} data(s) próxima(s)" : "");
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

    public bool AbaAtiva(string chave) => !Prefs.AbasOff.Contains(chave);

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
