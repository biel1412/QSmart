using QSmart.UI.Servicos;

var b = WebApplication.CreateBuilder(args);
b.Services.AddRazorComponents().AddInteractiveServerComponents()
    .AddHubOptions(o => o.MaximumReceiveMessageSize = 256 * 1024 * 1024);
b.Services.AddQSmart();
var app = b.Build();
app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<DevHost.Components.App>().AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(QSmart.UI.Raiz).Assembly);
app.Run();
