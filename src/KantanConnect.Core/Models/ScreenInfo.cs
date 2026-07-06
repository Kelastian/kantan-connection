namespace KantanConnect.Core.Models;

/// <summary>
/// Describe la resolución de la pantalla que el Host va a compartir. El Viewer la usa
/// para saber a qué coordenadas (0..1) debe normalizar los clics antes de enviarlos.
/// </summary>
public sealed class ScreenInfo
{
    public required int WidthPixels { get; init; }

    public required int HeightPixels { get; init; }
}
