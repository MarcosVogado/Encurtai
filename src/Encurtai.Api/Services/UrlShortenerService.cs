namespace Encurtai.Api.Services;

/// <summary>
/// Regra de negócio do encurtador. Não conhece HTTP nem banco — só depende das
/// abstrações ICodeGenerator e IUrlStore. Por isso é 100% testável sem subir servidor.
/// </summary>
public class UrlShortenerService
{
    private const int MaxTentativas = 5;

    private readonly ICodeGenerator _gerador;
    private readonly IUrlStore _store;

    public UrlShortenerService(ICodeGenerator gerador, IUrlStore store)
    {
        _gerador = gerador;
        _store = store;
    }

    public ShortenResult Shorten(string? url)
    {
        if (!IsValidUrl(url, out var normalizada))
            return ShortenResult.Invalid();

        // Se o código sorteado já existe, tenta de novo (trata colisão).
        for (var i = 0; i < MaxTentativas; i++)
        {
            var codigo = _gerador.Next();
            if (_store.TryAdd(codigo, normalizada))
                return ShortenResult.Ok(codigo);
        }

        return ShortenResult.Colisao();
    }

    public string? Resolve(string codigo)
        => _store.TryGet(codigo, out var url) ? url : null;

    /// <summary>
    /// Valida a URL. Estático de propósito para dar um alvo de teste puro e direto.
    /// Só aceita http/https absolutos.
    /// </summary>
    public static bool IsValidUrl(string? url, out string normalizada)
    {
        normalizada = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        normalizada = uri.ToString();
        return true;
    }
}
