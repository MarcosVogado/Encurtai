using Encurtai.Api.Services;
using Xunit;

namespace Encurtai.Tests;

public class UrlShortenerServiceTests
{
    private static UrlShortenerService CriarServico(ICodeGenerator gerador)
        => new(gerador, new InMemoryUrlStore());

    [Fact]
    public void Encurtar_UrlValida_RetornaCodigo()
    {
        var svc = CriarServico(new FakeCodeGenerator("abc123"));

        var r = svc.Shorten("https://exemplo.com");

        Assert.Equal(ShortenStatus.Ok, r.Status);
        Assert.Equal("abc123", r.Code);
    }

    [Fact]
    public void Encurtar_DepoisResolver_DevolveUrlOriginal()
    {
        var svc = CriarServico(new FakeCodeGenerator("xyz789"));

        svc.Shorten("https://exemplo.com/pagina");
        var url = svc.Resolve("xyz789");

        Assert.Equal("https://exemplo.com/pagina", url);
    }

    [Fact]
    public void Resolver_CodigoInexistente_RetornaNull()
    {
        var svc = CriarServico(new FakeCodeGenerator("aaa"));

        Assert.Null(svc.Resolve("naoexiste"));
    }

    [Fact]
    public void Encurtar_UrlInvalida_RetornaStatusInvalido()
    {
        var svc = CriarServico(new FakeCodeGenerator("aaa"));

        var r = svc.Shorten("isso-nao-e-url");

        Assert.Equal(ShortenStatus.InvalidUrl, r.Status);
    }

    [Fact]
    public void Encurtar_QuandoHaColisao_GeraOutroCodigo()
    {
        // O gerador oferece "dup", "dup", "novo".
        // 1a chamada ocupa "dup". 2a tenta "dup" (colide) e cai para "novo".
        var gerador = new FakeCodeGenerator("dup", "dup", "novo");
        var svc = CriarServico(gerador);

        var primeiro = svc.Shorten("https://a.com");
        var segundo = svc.Shorten("https://b.com");

        Assert.Equal("dup", primeiro.Code);
        Assert.Equal("novo", segundo.Code);
    }

    // ------------------------------------------------------------------
    // TESTE PROPOSITALMENTE QUEBRADO — ferramenta de demonstração.
    // Descomente, abra um Pull Request, e mostre o GitHub Actions barrando
    // o merge com o X vermelho. Depois comente de novo e o CI fica verde.
    // É a parte mais didática da apresentação.
    // ------------------------------------------------------------------
    // [Fact]
    // public void Demo_TesteQuebrado_ParaVerOCiFalhar()
    // {
    //     var svc = CriarServico(new FakeCodeGenerator("abc123"));
    //     var r = svc.Shorten("https://exemplo.com");
    //     Assert.Equal("codigo-errado-de-proposito", r.Code); // vai falhar de propósito
    // }
}
