using Encurtai.Api.Services;
using Xunit;

namespace Encurtai.Tests;

public class UrlValidationTests
{
    [Theory]
    [InlineData("https://exemplo.com")]
    [InlineData("http://exemplo.com/caminho?query=1")]
    [InlineData("https://sub.dominio.com.br/a/b/c")]
    public void UrlValida_DeveAceitar(string url)
    {
        var ok = UrlShortenerService.IsValidUrl(url, out _);
        Assert.True(ok);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("exemplo.com")]         // faltou o esquema
    [InlineData("ftp://exemplo.com")]   // esquema não suportado
    [InlineData("javascript:alert(1)")] // não é http/https
    [InlineData(null)]
    public void UrlInvalida_DeveRejeitar(string? url)
    {
        var ok = UrlShortenerService.IsValidUrl(url, out _);
        Assert.False(ok);
    }
}
