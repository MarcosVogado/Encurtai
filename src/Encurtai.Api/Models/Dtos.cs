namespace Encurtai.Api.Models;

/// <summary>Corpo esperado no POST /encurtar.</summary>
public record ShortenRequest(string? Url);

/// <summary>Resposta de sucesso do POST /encurtar.</summary>
public record ShortenResponse(string Codigo, string UrlCurta);
