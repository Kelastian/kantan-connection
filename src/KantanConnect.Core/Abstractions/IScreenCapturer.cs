using KantanConnect.Core.Models;

namespace KantanConnect.Core.Abstractions;

/// <summary>
/// Contrato para "algo que sabe capturar la pantalla". <c>Core</c> no sabe nada de DXGI
/// ni de GDI: solo pide fotogramas. Las implementaciones reales (DXGI, GDI de respaldo)
/// viven en <c>KantanConnect.Windows</c>, que es el único proyecto atado a la plataforma.
/// Esta separación es la que permitirá, a futuro, capturar pantalla en Android con otra
/// implementación sin tocar el protocolo ni la lógica de sesión.
/// </summary>
public interface IScreenCapturer : IDisposable
{
    ScreenInfo GetScreenInfo();

    /// <summary>
    /// Captura el siguiente fotograma disponible. Puede bloquear brevemente esperando
    /// un cambio en pantalla (comportamiento típico de Desktop Duplication).
    /// </summary>
    Task<CapturedFrame> CaptureNextFrameAsync(CancellationToken cancellationToken = default);
}
