using Encurtai.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Endereço da API lido em TEMPO DE EXECUÇÃO de wwwroot/appsettings.json.
//
// Isso importa porque este projeto é Blazor WebAssembly: o código é compilado
// para rodar no navegador do usuário, então variável de ambiente de container
// nunca chega até aqui. Antes a URL estava cravada como http://localhost:5080,
// o que só funciona na máquina do desenvolvedor.
//
// Como o arquivo é estático e servido pelo nginx, trocar de backend é editar um
// JSON (ou montar um ConfigMap sobre ele) — sem rebuild da imagem.
var apiBaseUrl = builder.Configuration["ApiBaseUrl"];
if (string.IsNullOrWhiteSpace(apiBaseUrl))
    apiBaseUrl = "/api/";

// BaseAddress só concatena direito se terminar em barra.
if (!apiBaseUrl.EndsWith('/'))
    apiBaseUrl += "/";

// Aceita valor absoluto (dev: http://localhost:5080/) ou relativo ao próprio
// origin (container: /api/, que o nginx encaminha para o serviço da API).
var baseAddress = Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var absoluta)
    ? absoluta
    : new Uri(new Uri(builder.HostEnvironment.BaseAddress), apiBaseUrl);

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = baseAddress });

await builder.Build().RunAsync();
