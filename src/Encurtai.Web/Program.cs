using Encurtai.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Aponta para a API rodando localmente. Ajuste se mudar a porta.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri("http://localhost:5080") });

await builder.Build().RunAsync();
