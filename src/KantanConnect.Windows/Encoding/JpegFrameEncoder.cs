using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using KantanConnect.Core.Abstractions;

namespace KantanConnect.Windows.Encoding;

/// <summary>
/// Comprime píxeles crudos BGRA32 a JPEG usando GDI+ (<see cref="System.Drawing"/>).
/// JPEG se eligió (ver plan, sección 2) porque es un códec con pérdida pero muy liviano
/// de codificar en CPU y soportado nativamente por .NET sin dependencias extra — suficiente
/// para una LAN doméstica, donde el ancho de banda no es el cuello de botella principal.
/// </summary>
public sealed class JpegFrameEncoder : IFrameEncoder
{
    /// <summary>
    /// Calidad JPEG (0-100). 70 es un punto medio razonable: visualmente aceptable para
    /// compartir pantalla (texto/ventanas, no fotografía) y bastante más liviano que
    /// calidades cercanas a 100, lo que importa para mantener buen FPS en la Fase 5.
    /// </summary>
    private const long JpegQuality = 70L;

    private readonly ImageCodecInfo _jpegCodec = GetJpegCodecInfo();
    private readonly EncoderParameters _encoderParameters = CreateEncoderParameters();

    public byte[] Encode(ReadOnlySpan<byte> rawBgraPixels, int widthPixels, int heightPixels)
    {
        using var bitmap = new Bitmap(widthPixels, heightPixels, PixelFormat.Format32bppArgb);
        CopyPixelsIntoBitmap(rawBgraPixels, bitmap);

        using var outputStream = new MemoryStream();
        bitmap.Save(outputStream, _jpegCodec, _encoderParameters);
        return outputStream.ToArray();
    }

    private static void CopyPixelsIntoBitmap(ReadOnlySpan<byte> rawBgraPixels, Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            var byteCount = bitmapData.Stride * bitmapData.Height;
            Marshal.Copy(rawBgraPixels[..byteCount].ToArray(), 0, bitmapData.Scan0, byteCount);
        }
        finally
        {
            bitmap.UnlockBits(bitmapData);
        }
    }

    private static ImageCodecInfo GetJpegCodecInfo() =>
        ImageCodecInfo.GetImageDecoders().First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

    private static EncoderParameters CreateEncoderParameters()
    {
        var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, JpegQuality);
        return parameters;
    }
}
