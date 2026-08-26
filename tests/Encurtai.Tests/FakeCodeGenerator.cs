using Encurtai.Api.Services;

namespace Encurtai.Tests;

/// <summary>
/// Gerador controlado para os testes: você diz exatamente quais códigos ele
/// vai devolver, na ordem. É isso que permite forçar uma colisão de propósito.
/// </summary>
public class FakeCodeGenerator : ICodeGenerator
{
    private readonly Queue<string> _codigos;
    private readonly string _ultimo;

    public FakeCodeGenerator(params string[] codigos)
    {
        _codigos = new Queue<string>(codigos);
        _ultimo = codigos.Length > 0 ? codigos[^1] : "fallback";
    }

    public string Next() => _codigos.Count > 0 ? _codigos.Dequeue() : _ultimo;
}
