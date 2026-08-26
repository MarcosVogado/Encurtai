using System.Security.Cryptography;

namespace Encurtai.Api.Services;

public class RandomCodeGenerator : ICodeGenerator
{
    // Alfabeto sem caracteres ambíguos (0/O, 1/l/I) para o link ser fácil de ler/digitar.
    private const string Alfabeto =
        "abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly int _tamanho;

    public RandomCodeGenerator(int codeLength = 6) => _tamanho = codeLength;

    public string Next()
    {
        var chars = new char[_tamanho];
        for (var i = 0; i < _tamanho; i++)
            chars[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        return new string(chars);
    }
}
