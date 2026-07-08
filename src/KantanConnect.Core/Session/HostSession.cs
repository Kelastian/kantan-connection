using System.Net.Sockets;
using System.Text.Json;
using KantanConnect.Core.Models;
using KantanConnect.Core.Protocol;
using KantanConnect.Core.Protocol.Messages;
using KantanConnect.Core.Security;

namespace KantanConnect.Core.Session;

/// <summary>
/// Lado "Host" de una sesión: escucha una única conexión TCP entrante, hace el
/// handshake inicial, exige el PIN (máximo <see cref="MaxPinAttempts"/> intentos) y,
/// una vez aceptado, deja la conexión lista para que las Fases 5/6 (video/entrada)
/// construyan sobre ella.
///
/// Deliberadamente acepta <b>una sola conexión a la vez</b>: este proyecto modela
/// "una persona compartiendo su pantalla con otra", no un servidor multi-cliente.
/// </summary>
public sealed class HostSession : IAsyncDisposable
{
    public const int MaxPinAttempts = 3;
    private const string ProtocolVersion = "1.0";

    private readonly TcpListener _listener;
    private readonly ScreenInfo _screenInfo;
    private readonly string _pin;

    // El stream lo escriben dos "dueños" distintos y potencialmente concurrentes: el
    // bucle de captura/envío de video (Fase 5, vía SendFrameAsync) y RunPostAuthLoopAsync
    // al responder un Ping con Pong. Un socket TCP soporta lectura y escritura simultáneas,
    // pero NO dos escrituras simultáneas (los bytes de ambos mensajes se entrelazarían y
    // corromperían el framing de MessageFramer); este semáforo serializa toda escritura.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _sessionCts;
    private Task? _sessionTask;

    /// <summary>Se dispara en cuanto un Viewer completa el handshake y se le pide el PIN.</summary>
    public event EventHandler<HelloMessage>? ViewerConnecting;

    /// <summary>Se dispara cuando el Viewer manda un PIN incorrecto (con intentos aún disponibles).</summary>
    public event EventHandler<PinRejectedEventArgs>? PinRejected;

    /// <summary>Se dispara cuando el PIN fue aceptado: la sesión ya está lista para video/entrada.</summary>
    public event EventHandler? Connected;

    /// <summary>Se dispara cada vez que llega un evento de ratón/teclado del Viewer.</summary>
    public event EventHandler<InputEvent>? InputEventReceived;

    /// <summary>Se dispara cuando la sesión termina, por cualquier motivo.</summary>
    public event EventHandler<SessionEndedEventArgs>? SessionEnded;

    /// <param name="pin">El PIN que el usuario ve en pantalla (ver <see cref="PinGenerator"/>).</param>
    public HostSession(string pin, ScreenInfo screenInfo, int tcpPort = Discovery.DiscoveryDefaults.TcpSessionPort)
    {
        _pin = pin;
        _screenInfo = screenInfo;
        _listener = new TcpListener(System.Net.IPAddress.Any, tcpPort);
    }

    /// <summary>
    /// Empieza a escuchar y maneja el ciclo de vida completo de la primera conexión
    /// que llegue, en una tarea de fondo. No hace falta esperar esta llamada: seguí el
    /// progreso a través de los eventos (<see cref="ViewerConnecting"/>, <see cref="Connected"/>, etc).
    /// </summary>
    public void Start()
    {
        if (_sessionTask is not null)
        {
            return;
        }

        _listener.Start();
        _sessionCts = new CancellationTokenSource();
        _sessionTask = RunAsync(_sessionCts.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            _client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            _stream = _client.GetStream();

            var hello = await ReadExpectedAsync<HelloMessage>(MessageType.Hello, cancellationToken)
                .ConfigureAwait(false);
            ViewerConnecting?.Invoke(this, hello);

            await WriteFramedAsync(
                MessageType.ScreenInfo, JsonSerializer.SerializeToUtf8Bytes(_screenInfo), cancellationToken)
                .ConfigureAwait(false);

            if (!await NegotiatePinAsync(cancellationToken).ConfigureAwait(false))
            {
                RaiseSessionEnded(SessionEndReason.PinAttemptsExhausted);
                return;
            }

            Connected?.Invoke(this, EventArgs.Empty);

            await RunPostAuthLoopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RaiseSessionEnded(SessionEndReason.LocalDisconnect);
        }
        catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException)
        {
            RaiseSessionEnded(SessionEndReason.ConnectionLost, ex.Message);
        }
    }

    /// <summary>
    /// Intercambia <see cref="PinRequestMessage"/>/<see cref="PinAttemptMessage"/> hasta que el
    /// Viewer acierte el PIN o se agoten los intentos. Devuelve true si quedó autenticado.
    /// </summary>
    private async Task<bool> NegotiatePinAsync(CancellationToken cancellationToken)
    {
        var pinRequest = new PinRequestMessage { PinLength = _pin.Length };

        for (var attemptsUsed = 0; attemptsUsed < MaxPinAttempts; attemptsUsed++)
        {
            await WriteFramedAsync(
                MessageType.PinRequest, JsonSerializer.SerializeToUtf8Bytes(pinRequest), cancellationToken)
                .ConfigureAwait(false);

            var attempt = await ReadExpectedAsync<PinAttemptMessage>(MessageType.PinAttempt, cancellationToken)
                .ConfigureAwait(false);

            if (attempt.Pin == _pin)
            {
                await SendPinResultAsync(accepted: true, remainingAttempts: 0, cancellationToken)
                    .ConfigureAwait(false);
                return true;
            }

            var remaining = MaxPinAttempts - (attemptsUsed + 1);
            await SendPinResultAsync(accepted: false, remainingAttempts: remaining, cancellationToken)
                .ConfigureAwait(false);
            PinRejected?.Invoke(this, new PinRejectedEventArgs(remaining));
        }

        return false;
    }

    private Task SendPinResultAsync(bool accepted, int remainingAttempts, CancellationToken cancellationToken)
    {
        var result = new PinResultMessage { Accepted = accepted, RemainingAttempts = remainingAttempts };
        return WriteFramedAsync(MessageType.PinResult, JsonSerializer.SerializeToUtf8Bytes(result), cancellationToken);
    }

    /// <summary>
    /// Envía un fotograma ya capturado y codificado al Viewer conectado. Pensado para
    /// llamarse repetidamente desde el bucle de captura de video (compuesto en <c>App</c>,
    /// que combina un <c>IScreenCapturer</c> con esta sesión) mientras la sesión esté
    /// <see cref="Connected"/>. Es seguro invocarlo aunque, al mismo tiempo,
    /// <see cref="RunPostAuthLoopAsync"/> esté respondiendo un <c>Pong</c>: ambas
    /// escrituras se serializan a través de <see cref="_writeLock"/>.
    /// </summary>
    public Task SendFrameAsync(CapturedFrame frame, CancellationToken cancellationToken = default)
    {
        var message = new FrameDataMessage
        {
            JpegBytes = frame.EncodedBytes,
            WidthPixels = frame.WidthPixels,
            HeightPixels = frame.HeightPixels,
            CapturedAtUtc = frame.CapturedAtUtc,
        };

        return WriteFramedAsync(MessageType.FrameData, JsonSerializer.SerializeToUtf8Bytes(message), cancellationToken);
    }

    /// <summary>
    /// Tras autenticar, la sesión queda "viva" respondiendo latidos, recibiendo entrada
    /// (Fase 6) y esperando un cierre ordenado, mientras en paralelo el llamador puede
    /// estar usando <see cref="SendFrameAsync"/> para transmitir video (Fase 5).
    /// </summary>
    private async Task RunPostAuthLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await MessageFramer.ReadAsync(_stream!, cancellationToken).ConfigureAwait(false);
            if (message is null)
            {
                RaiseSessionEnded(SessionEndReason.ConnectionLost, "El Viewer cerró la conexión sin avisar.");
                return;
            }

            switch (message.Value.Type)
            {
                case MessageType.Ping:
                    await WriteFramedAsync(MessageType.Pong, [], cancellationToken).ConfigureAwait(false);
                    break;

                case MessageType.InputEvent:
                    HandleInputEvent(message.Value.Payload);
                    break;

                case MessageType.Bye:
                    RaiseSessionEnded(SessionEndReason.RemoteClosed);
                    return;

                default:
                    // El Viewer no manda FrameData (eso viaja Host -> Viewer); cualquier
                    // otro tipo se ignora sin romper el bucle.
                    break;
            }
        }
    }

    /// <summary>
    /// Deserializa un <see cref="InputEvent"/> recién leído y republica el evento para
    /// que quien escuche <see cref="InputEventReceived"/> (en <c>App</c>, un
    /// <c>IInputInjector</c>) lo reproduzca con <c>SendInput</c>.
    /// </summary>
    private void HandleInputEvent(byte[] payload)
    {
        var inputEvent = JsonSerializer.Deserialize<InputEvent>(payload)
            ?? throw new InvalidDataException("No se pudo deserializar el payload de InputEvent.");

        InputEventReceived?.Invoke(this, inputEvent);
    }

    private async Task<T> ReadExpectedAsync<T>(MessageType expectedType, CancellationToken cancellationToken)
    {
        var message = await MessageFramer.ReadAsync(_stream!, cancellationToken).ConfigureAwait(false);
        if (message is null)
        {
            throw new EndOfStreamException($"Se esperaba {expectedType} pero la conexión se cerró.");
        }

        if (message.Value.Type != expectedType)
        {
            throw new InvalidDataException($"Se esperaba {expectedType} pero llegó {message.Value.Type}.");
        }

        return JsonSerializer.Deserialize<T>(message.Value.Payload)
            ?? throw new InvalidDataException($"No se pudo deserializar el payload de {expectedType}.");
    }

    /// <summary>Envía un mensaje, garantizando que ninguna otra escritura se entrelace (ver <see cref="_writeLock"/>).</summary>
    private async Task WriteFramedAsync(MessageType type, byte[] payload, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MessageFramer.WriteAsync(_stream!, type, payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void RaiseSessionEnded(SessionEndReason reason, string? detail = null) =>
        SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason, detail));

    public async ValueTask DisposeAsync()
    {
        _sessionCts?.Cancel();

        try
        {
            await (_sessionTask ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch
        {
            // Cualquier excepción de la tarea de sesión ya fue reportada vía SessionEnded;
            // acá solo esperamos a que termine, no hace falta propagarla de nuevo.
        }

        _stream?.Dispose();
        _client?.Dispose();
        _listener.Stop();
        _sessionCts?.Dispose();
        _writeLock.Dispose();
    }
}
