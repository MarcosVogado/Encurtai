using Encurtai.Api.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Encurtai.Tests;

public class CachedUrlStoreTests
{
    private static CachedUrlStore Criar(IUrlStore interno, IDistributedCache cache) =>
        new(interno, cache, NullLogger<CachedUrlStore>.Instance, TimeSpan.FromMinutes(5));

    [Fact]
    public void Leitura_repetida_nao_volta_ao_store()
    {
        var interno = new ContandoUrlStore();
        interno.TryAdd("abc123", "https://exemplo.com/");
        var store = Criar(interno, new CacheFalso());

        Assert.True(store.TryGet("abc123", out var primeira));
        Assert.True(store.TryGet("abc123", out var segunda));

        Assert.Equal("https://exemplo.com/", primeira);
        Assert.Equal("https://exemplo.com/", segunda);
        Assert.Equal(1, interno.LeiturasRecebidas); // a segunda veio do cache
    }

    [Fact]
    public void Gravacao_popula_o_cache_e_dispensa_a_primeira_leitura()
    {
        var interno = new ContandoUrlStore();
        var store = Criar(interno, new CacheFalso());

        Assert.True(store.TryAdd("abc123", "https://exemplo.com/"));
        Assert.True(store.TryGet("abc123", out var url));

        Assert.Equal("https://exemplo.com/", url);
        Assert.Equal(0, interno.LeiturasRecebidas);
    }

    [Fact]
    public void Colisao_no_store_nao_entra_no_cache()
    {
        // Se o cache fosse populado antes da confirmação do store, um código
        // recusado por colisão viraria link fantasma resolvendo pelo cache.
        var interno = new ContandoUrlStore();
        interno.TryAdd("abc123", "https://primeiro.com/");
        var store = Criar(interno, new CacheFalso());

        Assert.False(store.TryAdd("abc123", "https://segundo.com/"));
        Assert.True(store.TryGet("abc123", out var url));

        Assert.Equal("https://primeiro.com/", url);
    }

    [Fact]
    public void Codigo_inexistente_nao_e_cacheado_negativamente()
    {
        var interno = new ContandoUrlStore();
        var store = Criar(interno, new CacheFalso());

        Assert.False(store.TryGet("nada01", out _));
        interno.TryAdd("nada01", "https://chegou-depois.com/");

        Assert.True(store.TryGet("nada01", out var url));
        Assert.Equal("https://chegou-depois.com/", url);
    }

    [Fact]
    public void Cache_indisponivel_degrada_para_o_store_sem_lancar()
    {
        // Redis fora do ar deve custar desempenho, não disponibilidade.
        var interno = new ContandoUrlStore();
        interno.TryAdd("abc123", "https://exemplo.com/");
        var store = Criar(interno, new CacheQuebrado());

        Assert.True(store.TryAdd("xyz789", "https://outro.com/"));
        Assert.True(store.TryGet("abc123", out var url));

        Assert.Equal("https://exemplo.com/", url);
    }

    // --- dublês ---

    private sealed class ContandoUrlStore : IUrlStore
    {
        private readonly Dictionary<string, string> _dados = new();

        public int LeiturasRecebidas { get; private set; }

        public bool TryAdd(string codigo, string url) => _dados.TryAdd(codigo, url);

        public bool TryGet(string codigo, out string url)
        {
            LeiturasRecebidas++;
            var achou = _dados.TryGetValue(codigo, out var valor);
            url = valor ?? string.Empty;
            return achou;
        }
    }

    private sealed class CacheFalso : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _dados = new();

        public byte[]? Get(string key) => _dados.TryGetValue(key, out var v) ? v : null;

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => _dados[key] = value;

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key) => _dados.Remove(key);

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class CacheQuebrado : IDistributedCache
    {
        private static Exception Falha() => new InvalidOperationException("Redis indisponível");

        public byte[]? Get(string key) => throw Falha();

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Falha();

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Falha();

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
            CancellationToken token = default) => throw Falha();

        public void Refresh(string key) => throw Falha();

        public Task RefreshAsync(string key, CancellationToken token = default) => throw Falha();

        public void Remove(string key) => throw Falha();

        public Task RemoveAsync(string key, CancellationToken token = default) => throw Falha();
    }
}
