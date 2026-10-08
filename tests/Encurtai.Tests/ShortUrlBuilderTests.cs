using Encurtai.Api.Services;
using Xunit;

namespace Encurtai.Tests;

/// <summary>
/// Cobre o bug de containerização: sem Encurtai:BaseUrl, a URL curta era montada
/// com o host do request — que atrás de nginx/ingress é o host INTERNO do container.
/// </summary>
public class ShortUrlBuilderTests
{
    [Fact]
    public void BaseUrl_configurada_tem_precedencia_sobre_o_host_do_request()
    {
        var url = ShortUrlBuilder.Build(
            baseUrlConfigurada: "https://encurta.ai",
            scheme: "http",
            host: "encurtai-api-7d9f8b:8080", // como o container se vê por dentro
            codigo: "abc123");

        Assert.Equal("https://encurta.ai/abc123", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sem_BaseUrl_cai_para_o_host_do_request(string? baseUrl)
    {
        var url = ShortUrlBuilder.Build(baseUrl, "http", "localhost:5080", "abc123");

        Assert.Equal("http://localhost:5080/abc123", url);
    }

    [Theory]
    [InlineData("https://encurta.ai/")]
    [InlineData("https://encurta.ai//")]
    [InlineData("  https://encurta.ai/  ")]
    public void Barra_sobrando_na_BaseUrl_nao_duplica_na_url_final(string baseUrl)
    {
        var url = ShortUrlBuilder.Build(baseUrl, "http", "irrelevante", "abc123");

        Assert.Equal("https://encurta.ai/abc123", url);
    }

    [Fact]
    public void Prefixo_de_caminho_na_BaseUrl_e_preservado()
    {
        // Cenário de ingress servindo o encurtador sob um subcaminho.
        var url = ShortUrlBuilder.Build("https://exemplo.com/links", "http", "irrelevante", "abc123");

        Assert.Equal("https://exemplo.com/links/abc123", url);
    }
}
