using Encurtai.Api.Models;
using Encurtai.Api.Services;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// --- MongoDB ---
var mongoConnString = builder.Configuration.GetConnectionString("Mongo")
    ?? throw new InvalidOperationException(
        "Connection string 'Mongo' não configurada. Veja o README (user-secrets / docker / CI).");
var mongoDbName = builder.Configuration["Mongo:Database"] ?? "encurtai";

builder.Services.AddSingleton<IMongoClient>(_ => new MongoClient(mongoConnString));
builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDbName));

// Store agora é Mongo. O InMemoryUrlStore continua no projeto, usado pelos testes unitários.
builder.Services.AddSingleton<IUrlStore, MongoUrlStore>();
builder.Services.AddSingleton<ICodeGenerator>(new RandomCodeGenerator(codeLength: 6));
builder.Services.AddScoped<UrlShortenerService>();

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

public partial class Program { }