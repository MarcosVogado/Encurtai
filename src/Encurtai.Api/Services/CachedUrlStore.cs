using Microsoft.Extensions.Caching.Distributed;

namespace Encurtai.Api.Services;

/// <summary>
/// Decorator de IUrlStore que põe um cache distribuído (Redis) na frente do Mongo.
///
/// Por que faz sentido num encurtador: a leitura (GET /{codigo}) é a operação
/// quente e o acesso é extremamente desbalanceado — um punhado de links responde
/// pela maior parte dos acessos. Resolver esses pela memória do Redis evita uma
/// ida ao banco por clique.
///
/// O cache é OPCIONAL: só é registrado quando existe a connection string "Redis"
/// (ver Program.cs). E é tolerante a falha — se o Redis cair, toda operação cai
/// para o Mongo em vez de derrubar a aplicação. Cache indisponível degrada
/// desempenho, não disponibilidade.
/// </summary>
public class CachedUrlStore : IUrlStore
{
    private const string Prefixo = "encurtai:link:";

    private readonly IUrlStore _interno;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CachedUrlStore> _log;
    private readonly DistributedCacheEntryOptions _opcoes;

    public CachedUrlStore(
        IUrlStore interno,
        IDistributedCache cache,
        ILogger<CachedUrlStore> log,
        TimeSpan ttl)
    {
        _interno = interno;
        _cache = cache;
        _log = log;
        _opcoes = new DistributedCacheEntryOptions { SlidingExpiration = ttl };
    }

    public bool TryAdd(string codigo, string url)
    {
        // A fonte da verdade é o store interno. Só cacheia se ele aceitou de fato —
        // cachear antes transformaria uma colisão de código em link fantasma.
        var gravou = _interno.TryAdd(codigo, url);
        if (gravou)
            Gravar(codigo, url);

        return gravou;
    }

    public bool TryGet(string codigo, out string url)
    {
        if (Ler(codigo) is { } doCache)
        {
            url = doCache;
            return true;
        }

        var achou = _interno.TryGet(codigo, out url);
        if (achou)
            Gravar(codigo, url);

        // Ausência NÃO é cacheada: um código inexistente hoje pode existir em
        // seguida, e cachear o negativo criaria 404 persistente para link válido.
        return achou;
    }

    private string? Ler(string codigo)
    {
        try
        {
            return _cache.GetString(Prefixo + codigo);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Leitura no cache falhou para {Codigo}; caindo para o store.", codigo);
            return null;
        }
    }

    private void Gravar(string codigo, string url)
    {
        try
        {
            _cache.SetString(Prefixo + codigo, url, _opcoes);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Gravação no cache falhou para {Codigo}; seguindo sem cache.", codigo);
        }
    }
}
