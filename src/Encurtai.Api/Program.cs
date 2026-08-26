using Encurtai.Api.Models;
using Encurtai.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Injeção de dependência: store e gerador são singletons (o store precisa
// persistir entre requisições); o serviço de negócio é montado a partir deles.
builder.Services.AddSingleton<IUrlStore, InMemoryUrlStore>();
builder.Services.AddSingleton<ICodeGenerator>(new RandomCodeGenerator(codeLength: 6));
builder.Services.AddScoped<UrlShortenerService>();

// CORS liberado para o front Blazor conseguir chamar a API em desenvolvimento.
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

// POST /encurtar  -> recebe { "url": "..." } e devolve o código + link curto.
app.MapPost("/encurtar", (ShortenRequest req, UrlShortenerService svc, HttpContext ctx) =>
{
    var resultado = svc.Shorten(req.Url);
    return resultado.Status switch
    {
        ShortenStatus.Ok => Results.Ok(new ShortenResponse(
            resultado.Code!,
            $"{ctx.Request.Scheme}://{ctx.Request.Host}/{resultado.Code}")),

        ShortenStatus.InvalidUrl => Results.BadRequest(
            new { erro = "URL inválida. Use http:// ou https://." }),

        _ => Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
    };
});

// GET /{codigo}  -> redireciona para a URL original.
app.MapGet("/{codigo}", (string codigo, UrlShortenerService svc) =>
{
    var url = svc.Resolve(codigo);
    return url is null ? Results.NotFound() : Results.Redirect(url);
});

app.Run();

// Necessário para, no futuro, escrever testes de integração com WebApplicationFactory.
public partial class Program { }
