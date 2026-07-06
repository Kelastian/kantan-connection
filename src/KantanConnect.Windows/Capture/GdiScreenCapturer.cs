using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using KantanConnect.Core.Abstractions;
using KantanConnect.Core.Models;

namespace KantanConnect.Windows.Capture;

/// <summary>
/// Captura de pantalla usando GDI (<see cref="Graphics.CopyFromScreen"/>), la técnica
/// clásica de Windows. Es más lenta que <c>DxgiScreenCapturer</c> (copia por CPU en vez
/// de por GPU) pero funciona en prácticamente cualquier escenario: sesiones RDP, tarjetas
/// gráficas sin soporte de Desktop Duplication, máquinas virtuales, etc. Por eso es el
/// respaldo que usa <see cref="ScreenCapturerFactory"/> cuando DXGI no está disponible.
/// </summary>
public sealed class GdiScreenCapturer : IScreenCapturer
{
    private readonly IFrameEncoder _encoder;
    private readonly int _widthPixels;
    private readonly int _heightPixels;
    private readonly Bitmap _frameBuffer;
    private readonly Graphics _frameGraphics;
    private bool _disposed;

    public GdiScreenCapturer(IFrameEncoder encoder)
    {
        _encoder = encoder;
        _widthPixels = GetSystemMetrics(SM_CXSCREEN);
        _heightPixels = GetSystemMetrics(SM_CYSCREEN);

        // Reutilizar el mismo Bitmap/Graphics entre capturas evita reservar memoria
        // nueva en cada fotograma; a los FPS que necesitamos (Fase 5), esa asignación
        // repetida sería suficiente para generar pausas de garbage collection notorias.
        _frameBuffer = new Bitmap(_widthPixels, _heightPixels, PixelFormat.Format32bppArgb);
        _frameGraphics = Graphics.FromImage(_frameBuffer);
    }

    public ScreenInfo GetScreenInfo() => new() { WidthPixels = _widthPixels, HeightPixels = _heightPixels };

    public Task<CapturedFrame> CaptureNextFrameAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _frameGraphics.CopyFromScreen(0, 0, 0, 0, new Size(_widthPixels, _heightPixels));

        var rawBgraPixels = ExtractBgraPixels(_frameBuffer);
        var encodedBytes = _encoder.Encode(rawBgraPixels, _widthPixels, _heightPixels);

        var frame = new CapturedFrame
        {
            EncodedBytes = encodedBytes,
            WidthPixels = _widthPixels,
            HeightPixels = _heightPixels,
            CapturedAtUtc = DateTimeOffset.UtcNow,
        };

        return Task.FromResult(frame);
    }

    /// <summary>
    /// Copia los píxeles del <see cref="Bitmap"/> a un array plano BGRA32, el formato
    /// crudo que espera <see cref="IFrameEncoder"/>. GDI+ ya entrega los píxeles en ese
    /// formato exacto (<see cref="PixelFormat.Format32bppArgb"/>), así que esto es solo
    /// una copia de memoria, sin conversión de color.
    /// </summary>
    private static byte[] ExtractBgraPixels(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

        try
        {
            var byteCount = bitmapData.Stride * bitmapData.Height;
            var buffer = new byte[byteCount];
            Marshal.Copy(bitmapData.Scan0, buffer, 0, byteCount);
            return buffer;
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _frameGraphics.Dispose();
        _frameBuffer.Dispose();
        _disposed = true;
    }
}
