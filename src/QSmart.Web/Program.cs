using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using QSmart.UI.Servicos;

// QSmart · Insi — aplicação C# (Blazor WebAssembly) que roda inteira no navegador.
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<QSmart.UI.Raiz>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AddQSmart();
await builder.Build().RunAsync();
