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
    public ValueTask<string[]> Chaves() => _js.InvokeAsync<string[]>("qsmart.kv.keys");

    public ValueTask Baixar(string nome, byte[] bytes, string mime) => _js.InvokeVoidAsync("qsmart.baixar", nome, bytes, mime);
    public ValueTask Abrir(byte[] bytes, string mime) => _js.InvokeVoidAsync("qsmart.abrir", bytes, mime);
    public ValueTask<bool> Copiar(string texto) => _js.InvokeAsync<bool>("qsmart.copiar", texto);
    public ValueTask AbrirUrl(string url) => _js.InvokeVoidAsync("qsmart.abrirUrl", url);
    public ValueTask Tema(string nome) => _js.InvokeVoidAsync("qsmart.tema", nome);
    public ValueTask Focar(string id) => _js.InvokeVoidAsync("qsmart.focar", id);
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
