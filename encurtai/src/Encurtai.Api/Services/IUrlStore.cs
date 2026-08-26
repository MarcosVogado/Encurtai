namespace Encurtai.Api.Services;

/// <summary>
/// Armazenamento código -> URL. Hoje é em memória; amanhã pode virar um banco
/// (EF Core) sem que o serviço de negócio precise mudar. Isso é o gancho de
/// evolução para você praticar "migrations no CI" depois.
/// </summary>
public interface IUrlStore
{
    bool TryAdd(string codigo, string url);
    bool TryGet(string codigo, out string url);
}
