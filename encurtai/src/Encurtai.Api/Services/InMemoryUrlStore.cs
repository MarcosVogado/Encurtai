using System.Collections.Concurrent;

namespace Encurtai.Api.Services;

public class InMemoryUrlStore : IUrlStore
{
    private readonly ConcurrentDictionary<string, string> _dados = new();

    public bool TryAdd(string codigo, string url) => _dados.TryAdd(codigo, url);

    public bool TryGet(string codigo, out string url)
    {
        var achou = _dados.TryGetValue(codigo, out var valor);
        url = valor ?? string.Empty;
        return achou;
    }
}
