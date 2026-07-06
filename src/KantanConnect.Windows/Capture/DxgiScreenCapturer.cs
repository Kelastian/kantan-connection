using KantanConnect.Core.Abstractions;
using KantanConnect.Core.Models;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace KantanConnect.Windows.Capture;

/// <summary>
/// Captura de pantalla usando DXGI Desktop Duplication, la API que le pide a la propia
/// GPU "avisame cuando cambie el contenido de esta pantalla y dame acceso directo a esos
/// píxeles". Es mucho más rápida que GDI porque evita copiar por CPU: la GPU entrega la
/// textura ya lista, y solo copiamos a un buffer legible por CPU una vez.
///
/// Puede fallar en varios escenarios conocidos (sesión RDP, pantalla bloqueada, GPU sin
/// soporte) — por eso <see cref="ScreenCapturerFactory"/> siempre tiene <see cref="GdiScreenCapturer"/>
/// como red de contención si la construcción de esta clase lanza una excepción.
/// </summary>
public sealed class DxgiScreenCapturer : IScreenCapturer
{
    /// <summary>
    /// Cuánto esperar a que la GPU reporte un cambio antes de devolver el control.
    /// Con "0 cambios" en pantalla, AcquireNextFrame se queda esperando hasta este
    /// límite; ese comportamiento es justamente lo que ahorra ancho de banda cuando
    /// la pantalla está quieta (ver Fase 5: no hay frame nuevo que enviar).
    /// </summary>
    private const uint AcquireFrameTimeoutMs = 500;

    private readonly IFrameEncoder _encoder;
    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly IDXGIOutputDuplication _duplication;
    private readonly int _widthPixels;
    private readonly int _heightPixels;
    private ID3D11Texture2D? _stagingTexture;
    private bool _disposed;

    /// <param name="outputIndex">
    /// Índice del monitor a capturar (0 = principal). v1 de este proyecto solo soporta
    /// un monitor a la vez (ver plan, sección "riesgos y notas").
    /// </param>
    public DxgiScreenCapturer(IFrameEncoder encoder, int outputIndex = 0)
    {
        _encoder = encoder;

        using var factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();
        factory.EnumAdapters1(0, out IDXGIAdapter1 adapter).CheckError();

        using (adapter)
        {
            var featureLevels = new[]
            {
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
            };

            D3D11.D3D11CreateDevice(
                adapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                featureLevels,
                out _device,
                out _context).CheckError();

            adapter.EnumOutputs((uint)outputIndex, out var output).CheckError();

            using (output)
            using (var output1 = output.QueryInterface<IDXGIOutput1>())
            {
                var outputDescription = output.Description;
                _widthPixels =
                    outputDescription.DesktopCoordinates.Right - outputDescription.DesktopCoordinates.Left;
                _heightPixels =
                    outputDescription.DesktopCoordinates.Bottom - outputDescription.DesktopCoordinates.Top;

                _duplication = output1.DuplicateOutput(_device);
            }
        }
    }

    public ScreenInfo GetScreenInfo() => new() { WidthPixels = _widthPixels, HeightPixels = _heightPixels };

    public async Task<CapturedFrame> CaptureNextFrameAsync(CancellationToken cancellationToken = default)
    {
        // AcquireNextFrame bloquea (hasta el timeout) esperando un cambio de pantalla;
        // se ejecuta en un hilo de threadpool para no congelar quien esté esperando
        // este Task (por ejemplo, el bucle de envío de video de la Fase 5).
        return await Task.Run(() => CaptureNextFrameCore(), cancellationToken).ConfigureAwait(false);
    }

    private CapturedFrame CaptureNextFrameCore()
    {
        using var duplicatedResource = AcquireNextFrameResource();
        try
        {
            using var acquiredTexture = duplicatedResource.QueryInterface<ID3D11Texture2D>();

            EnsureStagingTextureCreated(acquiredTexture.Description);
            _context.CopyResource(_stagingTexture!, acquiredTexture);
        }
        finally
        {
            // DXGI exige liberar cada frame adquirido antes de poder pedir el
            // siguiente con AcquireNextFrame; si no, la próxima llamada falla con
            // DXGI_ERROR_INVALID_CALL (bug real encontrado al probar esta clase con
            // 3 capturas seguidas: la primera funcionaba, la segunda fallaba).
            _duplication.ReleaseFrame();
        }

        var rawBgraPixels = ReadStagingTexturePixels();
        var encodedBytes = _encoder.Encode(rawBgraPixels, _widthPixels, _heightPixels);

        return new CapturedFrame
        {
            EncodedBytes = encodedBytes,
            WidthPixels = _widthPixels,
            HeightPixels = _heightPixels,
            CapturedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Pide el próximo fotograma a la GPU. Si nadie cambió la pantalla dentro del
    /// timeout, DXGI reporta <c>DXGI_ERROR_WAIT_TIMEOUT</c>: no es un error real, solo
    /// significa "nada nuevo todavía", así que reintentamos hasta que haya un cambio.
    /// </summary>
    private IDXGIResource AcquireNextFrameResource()
    {
        while (true)
        {
            var result = _duplication.AcquireNextFrame(
                AcquireFrameTimeoutMs, out _, out var resource);

            if (result.Success)
            {
                return resource!;
            }

            if (result.Code == Vortice.DXGI.ResultCode.WaitTimeout.Code)
            {
                continue;
            }

            result.CheckError();
        }
    }

    private void EnsureStagingTextureCreated(Texture2DDescription sourceDescription)
    {
        if (_stagingTexture is not null)
        {
            return;
        }

        _stagingTexture = _device.CreateTexture2D(new Texture2DDescription
        {
            Width = sourceDescription.Width,
            Height = sourceDescription.Height,
            Format = sourceDescription.Format,
            MipLevels = 1,
            ArraySize = 1,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
            MiscFlags = ResourceOptionFlags.None,
        });
    }

    private byte[] ReadStagingTexturePixels()
    {
        var mapped = _context.Map(_stagingTexture!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);

        try
        {
            // El "stride" (bytes por fila) que reporta la GPU puede traer relleno extra
            // al final de cada fila por alineación de memoria; copiamos fila por fila
            // para descartar ese relleno y dejar un buffer BGRA32 compacto, tal como lo
            // espera IFrameEncoder (que no sabe nada de particularidades de DXGI).
            var bytesPerPixel = 4;
            var tightRowBytes = _widthPixels * bytesPerPixel;
            var buffer = new byte[tightRowBytes * _heightPixels];

            unsafe
            {
                var sourceRow = (byte*)mapped.DataPointer;
                for (var y = 0; y < _heightPixels; y++)
                {
                    var destinationOffset = y * tightRowBytes;
                    var sourceSpan = new ReadOnlySpan<byte>(sourceRow, tightRowBytes);
                    sourceSpan.CopyTo(buffer.AsSpan(destinationOffset, tightRowBytes));
                    sourceRow += mapped.RowPitch;
                }
            }

            return buffer;
        }
        finally
        {
            _context.Unmap(_stagingTexture!, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // No hace falta un ReleaseFrame extra acá: CaptureNextFrameCore ya libera
        // cada frame en su propio "finally" antes de devolver el control, así que
        // nunca debería quedar uno pendiente al llegar a Dispose.
        _stagingTexture?.Dispose();
        _duplication.Dispose();
        _context.Dispose();
        _device.Dispose();
        _disposed = true;
    }
}
