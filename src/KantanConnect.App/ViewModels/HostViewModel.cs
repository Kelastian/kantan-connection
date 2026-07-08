using System.Windows.Threading;
using KantanConnect.Core.Abstractions;
using KantanConnect.Core.Discovery;
using KantanConnect.Core.Models;
using KantanConnect.Core.Protocol.Messages;
using KantanConnect.Core.Security;
using KantanConnect.Core.Session;
using KantanConnect.Windows.Capture;
using KantanConnect.Windows.Encoding;
using KantanConnect.Windows.Input;

namespace KantanConnect.App.ViewModels;

/// <summary>
/// ViewModel de <c>HostWindow</c>: dueño único de "compartir mi pantalla", tanto del
/// beacon UDP (<see cref="DiscoveryBroadcaster"/>, para que te encuentren) como de la
/// sesión TCP+PIN (<see cref="HostSession"/>, para autenticar y conversar). Se crean y
/// destruyen juntos porque representan exactamente el mismo período de tiempo: mientras
/// esta ventana esté abierta, el equipo está "compartiendo". Mantenerlos acá (y no
/// repartidos entre <c>MainViewModel</c> y esta clase) evita, por ejemplo, que el usuario
/// pueda abrir dos <see cref="HostSession"/> a la vez intentando escuchar el mismo puerto TCP.
/// </summary>
public sealed class HostViewModel : ViewModelBase, IAsyncDisposable
{
    /// <summary>
    /// Cap de fotogramas por segundo del bucle de captura+envío. 20 FPS es un punto medio
    /// dentro del rango 15-30 previsto en el plan: suficientemente fluido para compartir
    /// pantalla (no video de acción) sin exigir de más a la codificación JPEG por frame.
    /// </summary>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000.0 / 20);

    private readonly Dispatcher _dispatcher;
    private readonly DiscoveryBroadcaster _broadcaster;
    private readonly HostSession _hostSession;
    private readonly IScreenCapturer _screenCapturer;
    private readonly IInputInjector _inputInjector = new SendInputInjector();

    private CancellationTokenSource? _captureLoopCts;
    private Task? _captureLoopTask;

    private string _statusText = "Esperando que alguien se conecte...";
    private string? _viewerDisplayName;

    public HostViewModel(string localPeerId, string localDisplayName)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Pin = PinGenerator.Generate();
        PinWithSpacing = string.Join(' ', Pin.ToCharArray());

        var beacon = new DiscoveryBeacon
        {
            App = DiscoveryBeacon.AppIdentifier,
            PeerId = localPeerId,
            DisplayName = localDisplayName,
            TcpPort = DiscoveryDefaults.TcpSessionPort,
            ProtocolVersion = "1.0",
        };
        _broadcaster = new DiscoveryBroadcaster(beacon);

        _screenCapturer = ScreenCapturerFactory.Create(
            new JpegFrameEncoder(),
            onFallbackToGdi: ex => _dispatcher.Invoke(() =>
                StatusText = $"Aviso: captura por GPU no disponible ({ex.GetType().Name}), usando modo compatible."));

        _hostSession = new HostSession(Pin, _screenCapturer.GetScreenInfo(), DiscoveryDefaults.TcpSessionPort);
        _hostSession.ViewerConnecting += OnViewerConnecting;
        _hostSession.PinRejected += OnPinRejected;
        _hostSession.Connected += OnConnected;
        _hostSession.InputEventReceived += OnInputEventReceived;
        _hostSession.SessionEnded += OnSessionEnded;

        _broadcaster.Start();
        _hostSession.Start();
    }

    /// <summary>El PIN a mostrar en grande en la ventana.</summary>
    public string Pin { get; }

    /// <summary>
    /// El PIN con un espacio entre cada dígito (ej. "4 8 2 9"), para que se lea más
    /// claro a la distancia. WPF clásico no tiene una propiedad de "letter-spacing" en
    /// <c>TextBlock</c> (a diferencia de WinUI/CSS), así que se resuelve insertando
    /// los espacios acá en vez de en el XAML.
    /// </summary>
    public string PinWithSpacing { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public string? ViewerDisplayName
    {
        get => _viewerDisplayName;
        private set => SetField(ref _viewerDisplayName, value);
    }

    private void OnViewerConnecting(object? sender, HelloMessage hello)
    {
        _dispatcher.Invoke(() =>
        {
            ViewerDisplayName = hello.ViewerDisplayName;
            StatusText = $"\"{hello.ViewerDisplayName}\" se está conectando. Verificando PIN...";
        });
    }

    private void OnPinRejected(object? sender, PinRejectedEventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            StatusText = $"PIN incorrecto. Intentos restantes: {e.RemainingAttempts}.";
        });
    }

    /// <summary>
    /// Reproduce el evento de entrada recibido con <c>SendInput</c>. A propósito NO se
    /// salta al hilo de UI (a diferencia del resto de manejadores de esta clase):
    /// <c>SendInput</c> es una llamada directa a <c>user32.dll</c> que actúa sobre el
    /// sistema operativo entero, no sobre esta ventana WPF, así que no tiene la
    /// restricción de afinidad de hilo que sí tienen los objetos de WPF. Saltar acá
    /// agregaría latencia innecesaria al control remoto sin ningún beneficio.
    /// </summary>
    private void OnInputEventReceived(object? sender, InputEvent inputEvent) => _inputInjector.Inject(inputEvent);

    private void OnConnected(object? sender, EventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            StatusText = $"Conectado con \"{ViewerDisplayName}\". Transmitiendo pantalla...";
        });

        _captureLoopCts = new CancellationTokenSource();
        _captureLoopTask = RunCaptureLoopAsync(_captureLoopCts.Token);
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        StopCaptureLoop();

        _dispatcher.Invoke(() =>
        {
            StatusText = e.Reason switch
            {
                SessionEndReason.PinAttemptsExhausted =>
                    "Se agotaron los intentos de PIN. La conexión fue rechazada.",
                SessionEndReason.RemoteClosed => "El otro equipo cerró la conexión.",
                SessionEndReason.ConnectionLost => "Se perdió la conexión con el otro equipo.",
                _ => "La sesión terminó.",
            };
        });
    }

    /// <summary>
    /// Captura y envía fotogramas en bucle mientras la sesión esté conectada, respetando
    /// <see cref="FrameInterval"/> como cap de FPS. Corre en la tarea de fondo que ya
    /// mantiene <c>HostSession</c> (no bloquea el hilo de UI).
    /// </summary>
    private async Task RunCaptureLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var frameStartedAt = DateTimeOffset.UtcNow;

                var frame = await _screenCapturer.CaptureNextFrameAsync(cancellationToken).ConfigureAwait(false);
                await _hostSession.SendFrameAsync(frame, cancellationToken).ConfigureAwait(false);

                var elapsed = DateTimeOffset.UtcNow - frameStartedAt;
                var remainingDelay = FrameInterval - elapsed;
                if (remainingDelay > TimeSpan.Zero)
                {
                    await Task.Delay(remainingDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelación esperada al terminar la sesión o cerrar la ventana.
        }
    }

    /// <summary>
    /// Solo cancela el bucle; deliberadamente NO limpia <see cref="_captureLoopTask"/> a
    /// null (a diferencia del resto de los "Stop*" del proyecto), porque
    /// <see cref="DisposeAsync"/> necesita esa referencia para esperar a que el bucle
    /// termine de verdad antes de liberar <see cref="_screenCapturer"/>.
    /// </summary>
    private void StopCaptureLoop()
    {
        _captureLoopCts?.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        await _broadcaster.DisposeAsync();
        await _hostSession.DisposeAsync();

        // HostSession.DisposeAsync ya disparó SessionEnded -> StopCaptureLoop (cancela
        // el token); acá esperamos a que la tarea del bucle realmente termine antes de
        // liberar _screenCapturer, o una captura en curso podría lanzar sobre un objeto
        // ya dispuesto.
        if (_captureLoopTask is not null)
        {
            try
            {
                await _captureLoopTask;
            }
            catch (OperationCanceledException)
            {
                // Cancelación esperada al cerrar la ventana.
            }
        }

        _captureLoopCts?.Dispose();
        _screenCapturer.Dispose();
    }
}
