using Microsoft.JSInterop;

namespace QSmart.UI.Servicos;

/// <summary>Acesso ao navegador: armazenamento local, downloads, área de transferência e PDFs.</summary>
public sealed class Navegador
{
    readonly IJSRuntime _js;
    public Navegador(IJSRuntime js) { _js = js; }

    public ValueTask<string?> Ler(string chave) => _js.InvokeAsync<string?>("qsmart.kv.get", chave);
    public ValueTask<bool> Gravar(string chave, string valor) => _js.InvokeAsync<bool>("qsmart.kv.set", chave, valor);
    public ValueTask Apagar(string chave) => _js.InvokeVoidAsync("qsmart.kv.del", chave);
    /// <summary>Cofre criptografado (AES-GCM) só neste navegador.</summary>
    public ValueTask<bool> CofreGravar(string nome, byte[] dados) => _js.InvokeAsync<bool>("qsmart.cofre.set", nome, dados);
    public ValueTask<byte[]?> CofreLer(string nome) => _js.InvokeAsync<byte[]?>("qsmart.cofre.get", nome);
    public ValueTask CofreApagar(string nome) => _js.InvokeVoidAsync("qsmart.cofre.del", nome);
    public ValueTask<byte[]> CifrarSenha(byte[] dados, string senha) => _js.InvokeAsync<byte[]>("qsmart.cofre.cifrarSenha", dados, senha);
    public ValueTask<byte[]?> DecifrarSenha(byte[] dados, string senha) => _js.InvokeAsync<byte[]?>("qsmart.cofre.decifrarSenha", dados, senha);
    public ValueTask<string[]> Chaves() => _js.InvokeAsync<string[]>("qsmart.kv.keys");

    // pasta escolhida pelo usuário (papel: "dados" ou "backup")
    public ValueTask<bool> PastaSuportada() => _js.InvokeAsync<bool>("qsmart.pasta.suportado");
    public ValueTask<string?> PastaNome(string papel) => _js.InvokeAsync<string?>("qsmart.pasta.nome", papel);
    public ValueTask<string> PastaEstado(string papel) => _js.InvokeAsync<string>("qsmart.pasta.estado", papel);
    public ValueTask<bool> PastaPermitir(string papel) => _js.InvokeAsync<bool>("qsmart.pasta.permitir", papel);
    public ValueTask<string?> PastaEscolher(string papel) => _js.InvokeAsync<string?>("qsmart.pasta.escolher", papel);
    public ValueTask PastaConfirmar(string papel) => _js.InvokeVoidAsync("qsmart.pasta.confirmar", papel);
    public ValueTask PastaEsquecer(string papel) => _js.InvokeVoidAsync("qsmart.pasta.esquecer", papel);
    public ValueTask<string?> PastaLer(string papel, string caminho, bool escolhida = false) => _js.InvokeAsync<string?>("qsmart.pasta.ler", papel, caminho, escolhida);
    public ValueTask<bool> PastaGravar(string papel, string caminho, string texto, bool escolhida = false) => _js.InvokeAsync<bool>("qsmart.pasta.gravar", papel, caminho, texto, escolhida);
    public ValueTask<string[]> PastaListar(string papel, string sub) => _js.InvokeAsync<string[]>("qsmart.pasta.listar", papel, sub);
    public ValueTask PastaApagar(string papel, string caminho) => _js.InvokeVoidAsync("qsmart.pasta.apagar", papel, caminho);

    public ValueTask Baixar(string nome, byte[] bytes, string mime) => _js.InvokeVoidAsync("qsmart.baixar", nome, bytes, mime);
    public ValueTask Abrir(byte[] bytes, string mime) => _js.InvokeVoidAsync("qsmart.abrir", bytes, mime);
    public ValueTask<bool> Copiar(string texto) => _js.InvokeAsync<bool>("qsmart.copiar", texto);
    public ValueTask AbrirUrl(string url) => _js.InvokeVoidAsync("qsmart.abrirUrl", url);
    public ValueTask Tema(string nome) => _js.InvokeVoidAsync("qsmart.tema", nome);
    public ValueTask Focar(string id) => _js.InvokeVoidAsync("qsmart.focar", id);
    public sealed record RespostaHttp(int Status, string Texto);
    public ValueTask<RespostaHttp> IA(string url, string chave, string corpo) => _js.InvokeAsync<RespostaHttp>("qsmart.ia", url, chave, corpo);
    public ValueTask<RespostaHttp> Get(string url) => _js.InvokeAsync<RespostaHttp>("qsmart.get", url);
    public ValueTask<RespostaHttp> Req(string url, string metodo, Dictionary<string, string>? cab, string? corpo, int ms = 30000) => _js.InvokeAsync<RespostaHttp>("qsmart.req", url, metodo, cab ?? new(), corpo, ms);
    public ValueTask<string> Imagem(byte[] dados, int max) => _js.InvokeAsync<string>("qsmart.imagem", dados, max);
    public ValueTask NotaMontar(Microsoft.AspNetCore.Components.ElementReference el, Microsoft.AspNetCore.Components.ElementReference barra, Microsoft.AspNetCore.Components.ElementReference arq, object refNet, string html, string id) => _js.InvokeVoidAsync("qsmart.nota.montar", el, barra, arq, refNet, html, id);
    public ValueTask NotaDesmontar(Microsoft.AspNetCore.Components.ElementReference el) => _js.InvokeVoidAsync("qsmart.nota.desmontar", el);
    public ValueTask NotaEnviar(Microsoft.AspNetCore.Components.ElementReference el) => _js.InvokeVoidAsync("qsmart.nota.enviar", el);
    public ValueTask NotaCmd(Microsoft.AspNetCore.Components.ElementReference el, string nome, string? valor = null) => _js.InvokeVoidAsync("qsmart.nota.cmd", el, nome, valor);
    public ValueTask NotaGuardarSelecao(Microsoft.AspNetCore.Components.ElementReference el) => _js.InvokeVoidAsync("qsmart.nota.guardarSelecao", el);
    public ValueTask NotaImagens(Microsoft.AspNetCore.Components.ElementReference el, Microsoft.AspNetCore.Components.ElementReference input) => _js.InvokeVoidAsync("qsmart.nota.imagens", el, input);
    public ValueTask ColarImagens(Microsoft.AspNetCore.Components.ElementReference el, object refNet) => _js.InvokeVoidAsync("qsmart.colarImagens", el, refNet);
    public ValueTask Clicar(Microsoft.AspNetCore.Components.ElementReference el) => _js.InvokeVoidAsync("qsmart.clicar", el);
    public ValueTask<string> ReduzirImagem(byte[] dados, int max) => _js.InvokeAsync<string>("qsmart.reduzirImagem", dados, max);
    public ValueTask<RespostaHttp> GetH(string url, Dictionary<string, string>? cabecalhos = null, int ms = 25000) => _js.InvokeAsync<RespostaHttp>("qsmart.getH", url, cabecalhos ?? new(), ms);
    public ValueTask Rolar(string id) => _js.InvokeVoidAsync("qsmart.rolar", id);
    public ValueTask RolarLinha(string id, int linha) => _js.InvokeVoidAsync("qsmart.rolarLinha", id, linha);
    public ValueTask Pronto() => _js.InvokeVoidAsync("qsmart.pronto");
    public ValueTask AoOcultar<T>(DotNetObjectReference<T> r) where T : class => _js.InvokeVoidAsync("qsmart.aoOcultar", r);

    public ValueTask<int> PdfPaginas(byte[] bytes) => _js.InvokeAsync<int>("qsmart.pdfPaginas", bytes);
    public ValueTask<byte[]> PdfJuntar(List<byte[]> lista) => _js.InvokeAsync<byte[]>("qsmart.pdfJuntar", lista);
    public ValueTask<string[]> PdfLinhas(byte[] bytes) => _js.InvokeAsync<string[]>("qsmart.pdfLinhas", bytes);

    public const string MimeXlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string MimePdf = "application/pdf";
    public const string MimeCsv = "text/csv";
    public const string MimeJson = "application/json";
}
