using System.Reflection;
using Encurtai.Api.Models;
using Encurtai.Api.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.Distributed;
using MongoDB.Bson;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);

// --- Versão da imagem ---
// Vem do <Version> do csproj (injetado no build por -p:Version=). É devolvida em
// /health para que um rollback seja verificável com um curl, e não só com um
// print de pipeline verde.
var versao = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?.Split('+')[0] ?? "0.0.0";

// --- MongoDB ---
// Sem default no appsettings.json: se a connection string não vier, a aplicação
// falha AQUI, no boot, com mensagem clara. Antes havia "mongodb://localhost:27017"
// no appsettings, o que dentro de um container aponta para o próprio container —
// a imagem subia "saudável" e só quebrava na primeira escrita.
var mongoConnString = builder.Configuration.GetConnectionString("Mongo")
    ?? throw new InvalidOperationException(
        "Connection string 'Mongo' não configurada. Defina ConnectionStrings__Mongo " +
        "(container/CI) ou use dotnet user-secrets no desenvolvimento. Veja o README.");
var mongoDbName = builder.Configuration["Mongo:Database"] ?? "encurtai";

// Timeouts curtos de propósito. O padrão do driver é 30s de server selection:
// com ele, /health/ready pendura meio minuto quando o banco está fora, prendendo
// thread e deixando a readinessProbe depender do próprio timeout dela para falhar.
// Falhar rápido e deixar a probe tentar de novo é o comportamento correto.
var mongoTimeout = TimeSpan.FromSeconds(
    builder.Configuration.GetValue<int?>("Mongo:TimeoutSegundos") ?? 5);

builder.Services.AddSingleton<IMongoClient>(_ =>
{
    var settings = MongoClientSettings.FromConnectionString(mongoConnString);
    settings.ServerSelectionTimeout = mongoTimeout;
    settings.ConnectTimeout = mongoTimeout;
    return new MongoClient(settings);
});

builder.Services.AddSingleton(sp =>
    sp.GetRequiredService<IMongoClient>().GetDatabase(mongoDbName));

// --- Store ---
// MongoUrlStore é sempre registrado como tipo concreto; quem atende IUrlStore
// depende de haver cache configurado ou não.
builder.Services.AddSingleton<MongoUrlStore>();

var redisConnString = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnString))
{
    builder.Services.AddSingleton<IUrlStore>(sp => sp.GetRequiredService<MongoUrlStore>());
}
else
{
    // Cache é opt-in por configuração: sem ConnectionStrings__Redis a aplicação
    // roda idêntica à versão anterior, direto no Mongo.
    builder.Services.AddStackExchangeRedisCache(o =>
    {
        o.Configuration = redisConnString;
        o.InstanceName = "encurtai";
    });

    var ttlMinutos = builder.Configuration.GetValue<int?>("Encurtai:CacheTtlMinutos") ?? 60;

    builder.Services.AddSingleton<IUrlStore>(sp => new CachedUrlStore(
        sp.GetRequiredService<MongoUrlStore>(),
        sp.GetRequiredService<IDistributedCache>(),
        sp.GetRequiredService<ILogger<CachedUrlStore>>(),
        TimeSpan.FromMinutes(ttlMinutos)));
}

builder.Services.AddSingleton<ICodeGenerator>(new RandomCodeGenerator(codeLength: 6));
builder.Services.AddScoped<UrlShortenerService>();

// --- Proxy reverso ---
// Atrás do nginx (compose) ou de um ingress (k8s), Scheme/Host do request são os
// do container. Confiar nos headers X-Forwarded-* é o fallback; o caminho
// preferido é Encurtai:BaseUrl, abaixo, que não depende de confiar em header algum.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                       | ForwardedHeaders.XForwardedProto
                       | ForwardedHeaders.XForwardedHost;

    // Em container o IP do proxy não é conhecido de antemão. Aceitável porque o
    // único uso desses headers é montar a URL curta, e BaseUrl tem precedência.
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

// --- CORS ---
// Antes era AllowAnyOrigin/Header/Method em qualquer ambiente. Com o nginx fazendo
// proxy de /api/, o tráfego de produção é same-origin e não precisa de CORS algum.
var corsOrigins = builder.Configuration.GetSection("Encurtai:CorsOrigins").Get<string[]>()
                  ?? Array.Empty<string>();

builder.Services.AddCors(options => options.AddDefaultPolicy(p =>
{
    if (corsOrigins.Length > 0)
        p.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
    else if (builder.Environment.IsDevelopment())
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    // Sem origens e fora de Development: nenhuma origem cruzada é liberada.
}));

// Endereço público do encurtador. Preenchido no compose e no k8s.
var baseUrlConfigurada = builder.Configuration["Encurtai:BaseUrl"];

// Chave de demonstração para o fluxo de rollback: quando true, /health/ready passa
// a responder 503, a readinessProbe reprova e o rollout é barrado. Mesmo espírito
// do teste quebrado comentado nos testes, mas aplicado ao deploy.
var simularFalha = builder.Configuration.GetValue<bool>("Encurtai:SimularFalhaDeReadiness");

var app = builder.Build();

app.UseForwardedHeaders();

// Banco indisponível é 503, não 500.
//
// Antes deste tratamento, com o Mongo fora do ar tanto GET /{codigo} quanto
// POST /encurtar estouravam MongoDB.Driver.MongoException crua e viravam 500 —
// e, em Development, respondiam com stack trace inteira no corpo. 500 diz
// "a aplicação tem um defeito"; 503 diz "a dependência está fora", que é o
// sinal correto para cliente, para proxy e para a leitura do incidente.
app.UseExceptionHandler(ramo => ramo.Run(async ctx =>
{
    var excecao = ctx.Features
        .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;

    var dependenciaFora = excecao is MongoException or TimeoutException
        || excecao?.InnerException is MongoException or TimeoutException;

    ctx.Response.StatusCode = dependenciaFora
        ? StatusCodes.Status503ServiceUnavailable
        : StatusCodes.Status500InternalServerError;

    var log = ctx.RequestServices.GetRequiredService<ILogger<Program>>();
    log.LogError(excecao, "Falha ao atender {Metodo} {Caminho}", ctx.Request.Method, ctx.Request.Path);

    await ctx.Response.WriteAsJsonAsync(new
    {
        erro = dependenciaFora
            ? "Serviço temporariamente indisponível. Tente novamente."
            : "Erro interno."
    });
}));

app.UseCors();

// --- Health ---
// Rotas literais têm precedência sobre o parâmetro de GET /{codigo}, então estas
// convivem com o catch-all do encurtador.

// Liveness: o processo está de pé. Não toca no banco de propósito — se o Mongo cair,
// reiniciar o container não resolve, e um liveness que depende do banco viraria
// um loop de restart.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", versao }));

// Readiness: só recebe tráfego quem consegue falar com o Mongo.
app.MapGet("/health/ready", async (IMongoDatabase db, CancellationToken ct) =>
{
    if (simularFalha)
        return Results.Json(
            new { status = "unready", motivo = "Encurtai:SimularFalhaDeReadiness=true" },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    // Teto rígido independente do driver: a probe recebe resposta mesmo que a
    // seleção de servidor demore mais do que o configurado.
    using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
    limite.CancelAfter(TimeSpan.FromSeconds(3));

    try
    {
        await db.RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1), cancellationToken: limite.Token);

        return Results.Ok(new { status = "ready", versao });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new { status = "unready", motivo = ex.Message },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// POST /encurtar  -> recebe { "url": "..." } e devolve o código + link curto.
app.MapPost("/encurtar", (ShortenRequest req, UrlShortenerService svc, HttpContext ctx) =>
{
    var resultado = svc.Shorten(req.Url);
    return resultado.Status switch
    {
        ShortenStatus.Ok => Results.Ok(new ShortenResponse(
            resultado.Code!,
            ShortUrlBuilder.Build(
                baseUrlConfigurada,
                ctx.Request.Scheme,
                ctx.Request.Host.ToString(),
                resultado.Code!))),

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
