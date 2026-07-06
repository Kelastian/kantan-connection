namespace KantanConnect.Core.Abstractions;

/// <summary>
/// Contrato para "algo que comprime píxeles crudos a un formato transmisible" (JPEG en la
/// implementación de Windows, ver Fase 4). Vive separado de <see cref="IScreenCapturer"/>
/// porque la captura y la codificación son responsabilidades distintas y así se pueden
/// probar o reemplazar de forma independiente (por ejemplo, cambiar a un códec H.264 a futuro).
/// </summary>
public interface IFrameEncoder
{
    /// <summary>
    /// Comprime un buffer de píxeles crudos en formato BGRA32 a bytes listos para enviar.
    /// </summary>
    byte[] Encode(ReadOnlySpan<byte> rawBgraPixels, int widthPixels, int heightPixels);
}
