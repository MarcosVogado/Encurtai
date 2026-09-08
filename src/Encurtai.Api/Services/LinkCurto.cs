using MongoDB.Bson.Serialization.Attributes;

namespace Encurtai.Api.Services;

////<summary>
/// Classe que reperesenta o documento salvo no Mongo. O código curto é o próprio _id
/// Garantindo unicidade sem indice extra
/// </summary>
public class LinkCurto
{
    [BsonId]
    public string Codigo { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime CriadoEm { get; set; }
}
