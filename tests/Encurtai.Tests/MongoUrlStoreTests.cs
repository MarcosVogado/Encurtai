using Encurtai.Api.Services;
using MongoDB.Driver;
using Xunit;

namespace Encurtai.Tests;

/// <summary>
/// Testes de INTEGRAÇÃO: precisam de um Mongo rodando.
/// Cada instância usa um banco único e o descarta no fim (isolamento total).
/// </summary>
[Trait("Category", "Integration")]
public class MongoUrlStoreTests : IDisposable
{
    private readonly IMongoClient _client;
    private readonly string _dbName;
    private readonly MongoUrlStore _store;

    public MongoUrlStoreTests()
    {
        var conn = Environment.GetEnvironmentVariable("MONGO_TEST_CONNECTION")
                   ?? "mongodb://localhost:27017";
        _client = new MongoClient(conn);
        _dbName = $"encurtai_test_{Guid.NewGuid():N}";
        _store = new MongoUrlStore(_client.GetDatabase(_dbName));
    }

    [Fact]
    public void AdicionarEBuscar_DevolveUrlSalva()
    {
        _store.TryAdd("abc123", "https://exemplo.com");

        var achou = _store.TryGet("abc123", out var url);

        Assert.True(achou);
        Assert.Equal("https://exemplo.com", url);
    }

    [Fact]
    public void Adicionar_CodigoDuplicado_RetornaFalse()
    {
        Assert.True(_store.TryAdd("dup", "https://a.com"));
        Assert.False(_store.TryAdd("dup", "https://b.com")); // _id duplicado
    }

    [Fact]
    public void Buscar_CodigoInexistente_RetornaFalse()
    {
        Assert.False(_store.TryGet("naoexiste", out _));
    }

    public void Dispose() => _client.DropDatabase(_dbName);
}