namespace KantanConnect.Core.Protocol.Messages;

/// <summary>
/// Un fotograma de video codificado, enviado del Host al Viewer. A diferencia de los
/// demás mensajes de control (que son JSON puro), este mensaje "envuelve" los bytes
/// JPEG dentro de un sobre JSON con Base64 — se prefirió mantener un solo mecanismo de
/// serialización (System.Text.Json) para todo el protocolo en vez de mezclar JSON para
/// control y binario crudo para video, a costa de un ~33% de overhead por Base64 que,
/// en una LAN doméstica, no es el cuello de botella real (ver Encoding.notas.md sobre
/// por qué JPEG con compresión ya resuelve el ahorro de ancho de banda que importa).
/// </summary>
public sealed class FrameDataMessage
{
    public required byte[] JpegBytes { get; init; }

    public required int WidthPixels { get; init; }

    public required int HeightPixels { get; init; }

    /// <summary>Cuándo se capturó el frame en el Host (UTC), para medir latencia end-to-end.</summary>
    public required DateTimeOffset CapturedAtUtc { get; init; }
}
