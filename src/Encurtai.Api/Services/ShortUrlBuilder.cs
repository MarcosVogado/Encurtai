namespace Encurtai.Api.Services;

/// <summary>
/// Monta a URL curta devolvida pelo POST /encurtar.
///
/// Existe como helper estático e puro de propósito: antes, a URL era montada
/// inline com Request.Scheme/Request.Host, o que devolve o host INTERNO do
/// container quando a aplicação roda atrás de nginx ou de um ingress — ou seja,
/// o encurtador gerava links quebrados justamente no ambiente containerizado.
/// Isolado aqui, o comportamento fica coberto por teste unitário sem subir servidor.
/// </summary>
public static class ShortUrlBuilder
{
    /// <param name="baseUrlConfigurada">
    /// Valor de <c>Encurtai:BaseUrl</c>. Quando preenchido, manda — é o endereço
    /// público pelo qual o usuário final alcança o encurtador.
    /// </param>
    /// <param name="scheme">Scheme do request, usado só como fallback.</param>
    /// <param name="host">Host do request, usado só como fallback.</param>
    /// <param name="codigo">Código curto gerado.</param>
    public static string Build(string? baseUrlConfigurada, string scheme, string host, string codigo)
    {
        var raiz = string.IsNullOrWhiteSpace(baseUrlConfigurada)
            ? $"{scheme}://{host}"
            : baseUrlConfigurada.Trim();

        return $"{raiz.TrimEnd('/')}/{codigo}";
    }
}
