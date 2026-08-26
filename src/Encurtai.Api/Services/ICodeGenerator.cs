namespace Encurtai.Api.Services;

/// <summary>
/// Gera o "código curto". É uma interface (e não um método fixo) de propósito:
/// nos testes conseguimos trocar por um gerador falso e forçar uma colisão.
/// Esse é o "seam" que torna a lógica testável.
/// </summary>
public interface ICodeGenerator
{
    string Next();
}
