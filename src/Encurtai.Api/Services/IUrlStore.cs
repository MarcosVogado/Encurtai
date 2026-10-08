namespace Encurtai.Api.Services;

/// <summary>
/// Armazenamento código -> URL.
///
/// A abstração já provou seu valor duas vezes: o store saiu de memória
/// (<see cref="InMemoryUrlStore"/>, hoje usado só pelos testes unitários) para
/// MongoDB (<see cref="MongoUrlStore"/>), e depois ganhou um cache por cima
/// (<see cref="CachedUrlStore"/>) — tudo isso sem o
/// <see cref="UrlShortenerService"/> mudar uma linha.
/// </summary>
public interface IUrlStore
{
    bool TryAdd(string codigo, string url);
    bool TryGet(string codigo, out string url);
}
