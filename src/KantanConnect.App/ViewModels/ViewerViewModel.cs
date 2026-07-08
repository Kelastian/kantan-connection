using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KantanConnect.App.Services;
using KantanConnect.Core.Models;
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

    private string _statusText = string.Empty;
    private string _connectedToHeading = string.Empty;
    private ConnectionStatus _connectionStatus = ConnectionStatus.Busy;

    /// <summary>Ver el mismo patrón documentado en <c>HostViewModel</c> (y en <c>Localization.notas.md</c>).</summary>
    private enum DisplayState { Connecting, PinRejected, Connected, SessionEnded }

    private DisplayState _displayState = DisplayState.Connecting;
    private int _pinRejectedRemainingAttempts;
    private SessionEndReason _sessionEndedReason;

    public ViewerViewModel(string hostIpAddress, int hostTcpPort, string hostDisplayName)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        HostDisplayName = hostDisplayName;

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
        _viewerSession.FrameReceived += OnFrameReceived;
        _viewerSession.SessionEnded += OnSessionEnded;

        LocalizationService.Instance.PropertyChanged += (_, _) => RefreshLocalizedText();
        RefreshLocalizedText();

        _viewerSession.Start();
    }

    public string HostDisplayName { get; }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    /// <summary>El encabezado "Conectado a {Host}" de la parte superior de la ventana.</summary>
    public string ConnectedToHeading
    {
        get => _connectedToHeading;
        private set => SetField(ref _connectedToHeading, value);
    }

    /// <summary>Para el punto de color de la ventana (ver <c>ConnectionStatusToBrushConverter</c>).</summary>
    public ConnectionStatus ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetField(ref _connectionStatus, value);
    }

    private BitmapSource? _currentFrame;

    /// <summary>El último fotograma recibido del Host, listo para enlazar a un <c>Image</c> de WPF.</summary>
    public BitmapSource? CurrentFrame
    {
        get => _currentFrame;
        private set => SetField(ref _currentFrame, value);
    }

    /// <summary>
    /// Envía un evento de ratón/teclado capturado en <c>ViewerWindow</c> (code-behind,
    /// donde vive la medición geométrica del control <c>Image</c>) al Host. Fire-and-forget
    /// intencional: los manejadores de eventos de entrada de WPF son <c>void</c>, no
    /// <c>async Task</c>, y no tiene sentido bloquear la UI esperando el envío de cada
    /// movimiento de mouse. Si la sesión ya no está conectada, el envío simplemente falla
    /// en silencio (mismo criterio que el resto de fallos de red de esta sesión, que ya
    /// se reportan vía <see cref="ViewerSession.SessionEnded"/>, no acá).
    /// </summary>
    public void SendInputEvent(InputEvent inputEvent)
    {
        _ = _viewerSession.SendInputEventAsync(inputEvent).ContinueWith(
            _ => { /* Errores de un solo evento de entrada no ameritan acción propia. */ },
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }

    private async Task<string> OnRequestPinFromUserAsync(int pinLength)
    {
        var loc = LocalizationService.Instance;

        // El primer intento usa el prompt genérico ("Ingresá el PIN de N dígitos...");
        // a partir del segundo, PinRejected ya dejó guardados los intentos restantes
        // para armar el mensaje "PIN incorrecto, intentos restantes: N" en su lugar.
        var message = _displayState == DisplayState.PinRejected
            ? loc.Format("PinPromptWindow_RejectedPromptFormat", _pinRejectedRemainingAttempts)
            : loc.Format("PinPromptWindow_PromptFormat", pinLength);

        return await PinPromptWindow.PromptAsync(_dispatcher, pinLength, message);
    }

    private void OnPinRejected(object? sender, PinRejectedEventArgs e)
    {
        _displayState = DisplayState.PinRejected;
        _pinRejectedRemainingAttempts = e.RemainingAttempts;
        _dispatcher.Invoke(() =>
        {
            ConnectionStatus = ConnectionStatus.Error;
            RefreshLocalizedText();
        });
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        _displayState = DisplayState.Connected;
        _dispatcher.Invoke(() =>
        {
            ConnectionStatus = ConnectionStatus.Connected;
            RefreshLocalizedText();
        });
    }

    /// <summary>
    /// Decodifica el JPEG recibido a un <see cref="BitmapSource"/> inmutable y lo publica
    /// para que el <c>Image</c> de la ventana lo muestre. Se dispara desde el hilo de
    /// fondo de <see cref="ViewerSession"/>, así que el trabajo se salta al hilo de UI
    /// con <see cref="Dispatcher.Invoke"/> antes de tocar <see cref="CurrentFrame"/>.
    /// </summary>
    private void OnFrameReceived(object? sender, CapturedFrame frame)
    {
        var bitmap = DecodeJpegToFrozenBitmap(frame.EncodedBytes);
        _dispatcher.Invoke(() => CurrentFrame = bitmap);
    }

    /// <summary>
    /// Decodificar en el hilo de fondo (acá) y solo "congelar+asignar" en el UI thread
    /// evita bloquear ese hilo con la decodificación JPEG de cada fotograma; <c>Freeze()</c>
    /// es lo que permite crear el <see cref="BitmapImage"/> fuera del hilo de UI y aun así
    /// poder usarlo luego como <see cref="CurrentFrame"/> (los objetos WPF sin congelar
    /// solo se pueden tocar desde el hilo que los creó).
    /// </summary>
    private static BitmapImage DecodeJpegToFrozenBitmap(byte[] jpegBytes)
    {
        using var stream = new MemoryStream(jpegBytes);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        _displayState = DisplayState.SessionEnded;
        _sessionEndedReason = e.Reason;
        _dispatcher.Invoke(() =>
        {
            ConnectionStatus = e.Reason == SessionEndReason.LocalDisconnect
                ? ConnectionStatus.Idle
                : ConnectionStatus.Error;
            RefreshLocalizedText();
        });
    }

    /// <summary>
    /// Reconstruye <see cref="StatusText"/> y <see cref="ConnectedToHeading"/> a partir de
    /// <see cref="_displayState"/> en el idioma vigente. Se llama tanto en cada transición
    /// de estado como cuando <see cref="LocalizationService"/> notifica un cambio de idioma.
    /// </summary>
    private void RefreshLocalizedText()
    {
        var loc = LocalizationService.Instance;

        StatusText = _displayState switch
        {
            DisplayState.PinRejected => loc["ViewerViewModel_PinRejectedStatus"],
            DisplayState.Connected => loc.Format("ViewerViewModel_ConnectedStatusFormat", HostDisplayName),
            DisplayState.SessionEnded => _sessionEndedReason switch
            {
                SessionEndReason.PinAttemptsExhausted => loc["Session_PinAttemptsExhausted_Viewer"],
                SessionEndReason.RemoteClosed => loc["Session_RemoteClosed"],
                SessionEndReason.ConnectionLost => loc["Session_ConnectionLost"],
                _ => loc["Session_Ended"],
            },
            _ => loc.Format("ViewerViewModel_ConnectingStatusFormat", HostDisplayName),
        };

        ConnectedToHeading = loc.Format("ViewerWindow_ConnectedToFormat", HostDisplayName);
    }

    public async ValueTask DisposeAsync() => await _viewerSession.DisposeAsync();
}
