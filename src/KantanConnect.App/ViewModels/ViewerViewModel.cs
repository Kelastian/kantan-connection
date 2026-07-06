using System.Windows.Threading;
using KantanConnect.Core.Session;

namespace KantanConnect.App.ViewModels;

/// <summary>
/// ViewModel de <c>ViewerWindow</c>: conecta un <see cref="ViewerSession"/> al Host
/// elegido en <c>MainWindow</c> y refleja su progreso, pidiéndole el PIN al usuario a
/// través de <see cref="PinPromptWindow"/> cada vez que el protocolo lo requiere.
/// </summary>
public sealed class ViewerViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly ViewerSession _viewerSession;

    private string _statusText;
    private string? _lastRejectionMessage;

    public ViewerViewModel(string hostIpAddress, int hostTcpPort, string hostDisplayName)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        HostDisplayName = hostDisplayName;
        _statusText = $"Conectando con \"{hostDisplayName}\"...";

        _viewerSession = new ViewerSession(
            hostIpAddress,
            hostTcpPort,
            viewerId: Guid.NewGuid().ToString("N"),
            viewerDisplayName: Environment.MachineName)
        {
            RequestPinFromUserAsync = OnRequestPinFromUserAsync,
        };

        _viewerSession.PinRejected += OnPinRejected;
        _viewerSession.Connected += OnConnected;
        _viewerSession.SessionEnded += OnSessionEnded;
        _viewerSession.Start();
    }

    public string HostDisplayName { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    private async Task<string> OnRequestPinFromUserAsync(int pinLength)
    {
        var message = _lastRejectionMessage;
        _lastRejectionMessage = null;
        return await PinPromptWindow.PromptAsync(_dispatcher, pinLength, message);
    }

    private void OnPinRejected(object? sender, PinRejectedEventArgs e)
    {
        _lastRejectionMessage = $"PIN incorrecto. Intentos restantes: {e.RemainingAttempts}.\n" +
                                  "Ingresá el PIN nuevamente:";
        _dispatcher.Invoke(() => StatusText = "PIN incorrecto, reintentando...");
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        _dispatcher.Invoke(() =>
            StatusText = $"Conectado con \"{HostDisplayName}\". " +
                          "(La recepción de video se habilita en una fase futura.)");
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            StatusText = e.Reason switch
            {
                SessionEndReason.PinAttemptsExhausted =>
                    "Se agotaron los intentos de PIN. No fue posible conectar.",
                SessionEndReason.RemoteClosed => "El otro equipo cerró la conexión.",
                SessionEndReason.ConnectionLost => "Se perdió la conexión con el otro equipo.",
                _ => "La sesión terminó.",
            };
        });
    }

    public async ValueTask DisposeAsync() => await _viewerSession.DisposeAsync();
}
