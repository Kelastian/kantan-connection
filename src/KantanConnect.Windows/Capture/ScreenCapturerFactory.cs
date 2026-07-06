using KantanConnect.Core.Abstractions;

namespace KantanConnect.Windows.Capture;

/// <summary>
/// Punto único de creación de un <see cref="IScreenCapturer"/>: intenta
/// <see cref="DxgiScreenCapturer"/> primero (rápido, por GPU) y, si falla por cualquier
/// motivo, cae automáticamente a <see cref="GdiScreenCapturer"/> (más lento, pero
/// funciona en casi cualquier escenario). Así, "0 problemas para el usuario" se cumple
/// también acá: nunca debería fallar la app entera solo porque DXGI no esté disponible
/// (sesión RDP, GPU sin soporte, pantalla bloqueada al iniciar, etc.).
/// </summary>
public static class ScreenCapturerFactory
{
    /// <summary>
    /// Crea el mejor <see cref="IScreenCapturer"/> disponible en este equipo.
    /// </summary>
    /// <param name="onFallbackToGdi">
    /// Callback opcional invocado con el motivo si DXGI falló y se usó GDI en su lugar,
    /// útil para que la UI/logs informen por qué se está usando el modo de respaldo.
    /// </param>
    public static IScreenCapturer Create(IFrameEncoder encoder, Action<Exception>? onFallbackToGdi = null)
    {
        try
        {
            return new DxgiScreenCapturer(encoder);
        }
        catch (Exception ex)
        {
            onFallbackToGdi?.Invoke(ex);
            return new GdiScreenCapturer(encoder);
        }
    }
}
