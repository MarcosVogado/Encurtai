namespace Encurtai.Api.Services;

public enum ShortenStatus
{
    Ok,
    InvalidUrl,
    Collision
}

/// <summary>Resultado da operação de encurtar, sem lançar exceção para fluxo normal.</summary>
public sealed record ShortenResult(ShortenStatus Status, string? Code)
{
    public static ShortenResult Ok(string code) => new(ShortenStatus.Ok, code);
    public static ShortenResult Invalid() => new(ShortenStatus.InvalidUrl, null);
    public static ShortenResult Colisao() => new(ShortenStatus.Collision, null);
}
