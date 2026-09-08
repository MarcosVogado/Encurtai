using MongoDB.Driver;

namespace Encurtai.Api.Services;

/// <summary>
/// Implementação de IUrlStore usando MongoDB. Mesma interface do InMemoryUrlStore,
/// então o UrlShortenerService não sabe (nem precisa saber) que mudou o armazenamento.
/// </summary>
public class MongoUrlStore : IUrlStore
{
    private readonly IMongoCollection<LinkCurto> _colecao;

    public MongoUrlStore(IMongoDatabase database)
    {
        _colecao = database.GetCollection<LinkCurto>("links");
    }

    public bool TryAdd(string codigo, string url)
    {
        try
        {
            _colecao.InsertOne(new LinkCurto
            {
                Codigo = codigo,
                Url = url,
                CriadoEm = DateTime.UtcNow
            });
            return true;
        }
        catch (MongoWriteException ex)
            when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // _id já existe -> o serviço vai sortear outro código.
            return false;
        }
    }

    public bool TryGet(string codigo, out string url)
    {
        var doc = _colecao.Find(x => x.Codigo == codigo).FirstOrDefault();
        url = doc?.Url ?? string.Empty;
        return doc is not null;
    }
}