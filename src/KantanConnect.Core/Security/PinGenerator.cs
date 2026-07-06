using System.Security.Cryptography;

namespace KantanConnect.Core.Security;

/// <summary>
/// Genera el PIN numérico que el Host muestra en pantalla para emparejarse con un Viewer.
///
/// Se usa <see cref="RandomNumberGenerator"/> (criptográficamente seguro) en vez de
/// <see cref="Random"/> porque, aunque el PIN es corto y de corta duración, no cuesta
/// nada usar la fuente de aleatoriedad "correcta" por defecto en .NET.
/// </summary>
public static class PinGenerator
{
    public const int MinLength = 4;
    public const int MaxLength = 6;

    /// <summary>Genera un PIN de <paramref name="length"/> dígitos (con ceros a la izquierda permitidos).</summary>
    public static string Generate(int length = 6)
    {
        if (length < MinLength || length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(length), length, $"El PIN debe tener entre {MinLength} y {MaxLength} dígitos.");
        }

        var maxExclusive = (int)Math.Pow(10, length);
        var value = RandomNumberGenerator.GetInt32(0, maxExclusive);

        return value.ToString().PadLeft(length, '0');
    }
}
