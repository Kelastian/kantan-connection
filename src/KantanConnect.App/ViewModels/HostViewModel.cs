using System.Windows.Threading;
using KantanConnect.Core.Discovery;
using KantanConnect.Core.Models;
using KantanConnect.Core.Protocol.Messages;
using KantanConnect.Core.Security;
using KantanConnect.Core.Session;

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
    private readonly Dispatcher _dispatcher;
    private readonly DiscoveryBroadcaster _broadcaster;
    private readonly HostSession _hostSession;

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

        // ScreenInfo real (basado en la pantalla física del Host) llega en la Fase 4
        // junto con la captura DXGI/GDI. Por ahora se informa un valor de referencia
        // (Full HD) para que el protocolo de sesión ya quede completo y probado.
        var placeholderScreenInfo = new ScreenInfo { WidthPixels = 1920, HeightPixels = 1080 };

        _hostSession = new HostSession(Pin, placeholderScreenInfo, DiscoveryDefaults.TcpSessionPort);
        _hostSession.ViewerConnecting += OnViewerConnecting;
        _hostSession.PinRejected += OnPinRejected;
        _hostSession.Connected += OnConnected;
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

    private void OnConnected(object? sender, EventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            StatusText = $"Conectado con \"{ViewerDisplayName}\". " +
                          "(La transmisión de pantalla se habilita en una fase futura.)";
        });
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
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

    public async ValueTask DisposeAsync()
    {
        await _broadcaster.DisposeAsync();
        await _hostSession.DisposeAsync();
    }
}
