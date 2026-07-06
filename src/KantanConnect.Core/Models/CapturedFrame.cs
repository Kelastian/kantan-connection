namespace KantanConnect.Core.Models;

/// <summary>
/// Un fotograma ya codificado (JPEG) listo para viajar por la red.
/// Se mantiene deliberadamente simple: solo bytes + metadatos mínimos.
/// </summary>
public sealed class CapturedFrame
{
    /// <summary>Bytes de la imagen codificada (JPEG).</summary>
    public required byte[] EncodedBytes { get; init; }

    public required int WidthPixels { get; init; }

    public required int HeightPixels { get; init; }

    /// <summary>Momento (UTC) en que se capturó, útil para medir latencia end-to-end.</summary>
    public required DateTimeOffset CapturedAtUtc { get; init; }
}
