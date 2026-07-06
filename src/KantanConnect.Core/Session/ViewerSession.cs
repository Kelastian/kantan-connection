using System.Net.Sockets;
using System.Text.Json;
using KantanConnect.Core.Models;
using KantanConnect.Core.Protocol;
using KantanConnect.Core.Protocol.Messages;

namespace KantanConnect.Core.Session;

/// <summary>
/// Lado "Viewer" de una sesión: se conecta por TCP a un Host, se presenta, y negocia
/// el PIN pidiéndoselo al usuario a través de <see cref="RequestPinFromUserAsync"/>
/// (inyectado por la UI) hasta que el Host lo acepte o se acaben los intentos.
/// </summary>
public sealed class ViewerSession : IAsyncDisposable
{
    private const string ProtocolVersion = "1.0";

    private readonly string _hostIpAddress;
    private readonly int _hostTcpPort;
    private readonly string _viewerId;
    private readonly string _viewerDisplayName;

    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _sessionCts;
    private Task? _sessionTask;

    /// <summary>
    /// Callback que la UI provee: dado cuántos dígitos tiene el PIN, debe pedírselo al
    /// usuario y devolverlo. Se llama de nuevo si el intento anterior fue rechazado y
    /// todavía quedan reintentos (ver <see cref="PinRejected"/>).
    /// </summary>
    public required Func<int, Task<string>> RequestPinFromUserAsync { get; init; }

    /// <summary>Se dispara al recibir la resolución de pantalla del Host.</summary>
    public event EventHandler<ScreenInfo>? ScreenInfoReceived;

    /// <summary>Se dispara cuando un intento de PIN fue rechazado (con intentos aún disponibles).</summary>
    public event EventHandler<PinRejectedEventArgs>? PinRejected;

    /// <summary>Se dispara cuando el PIN fue aceptado: la sesión ya está lista para video/entrada.</summary>
    public event EventHandler? Connected;

    /// <summary>Se dispara cuando la sesión termina, por cualquier motivo.</summary>
    public event EventHandler<SessionEndedEventArgs>? SessionEnded;

    public ViewerSession(string hostIpAddress, int hostTcpPort, string viewerId, string viewerDisplayName)
    {
        _hostIpAddress = hostIpAddress;
        _hostTcpPort = hostTcpPort;
        _viewerId = viewerId;
        _viewerDisplayName = viewerDisplayName;
    }

    /// <summary>Conecta y maneja el ciclo de vida completo en una tarea de fondo (ver eventos).</summary>
    public void Start()
    {
        if (_sessionTask is not null)
        {
            return;
        }

        _sessionCts = new CancellationTokenSource();
        _sessionTask = RunAsync(_sessionCts.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            _client = new TcpClient();
            await _client.ConnectAsync(_hostIpAddress, _hostTcpPort, cancellationToken).ConfigureAwait(false);
            _stream = _client.GetStream();

            var hello = new HelloMessage
            {
                ViewerId = _viewerId,
                ViewerDisplayName = _viewerDisplayName,
                ProtocolVersion = ProtocolVersion,
            };
            await MessageFramer.WriteAsync(
                _stream, MessageType.Hello, JsonSerializer.SerializeToUtf8Bytes(hello), cancellationToken)
                .ConfigureAwait(false);

            var screenInfo = await ReadExpectedAsync<ScreenInfo>(MessageType.ScreenInfo, cancellationToken)
                .ConfigureAwait(false);
            ScreenInfoReceived?.Invoke(this, screenInfo);

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

    private async Task<bool> NegotiatePinAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var pinRequest = await ReadExpectedAsync<PinRequestMessage>(MessageType.PinRequest, cancellationToken)
                .ConfigureAwait(false);

            var pin = await RequestPinFromUserAsync(pinRequest.PinLength).ConfigureAwait(false);

            var attempt = new PinAttemptMessage { Pin = pin };
            await MessageFramer.WriteAsync(
                _stream!, MessageType.PinAttempt, JsonSerializer.SerializeToUtf8Bytes(attempt), cancellationToken)
                .ConfigureAwait(false);

            var result = await ReadExpectedAsync<PinResultMessage>(MessageType.PinResult, cancellationToken)
                .ConfigureAwait(false);

            if (result.Accepted)
            {
                return true;
            }

            if (result.RemainingAttempts <= 0)
            {
                return false;
            }

            PinRejected?.Invoke(this, new PinRejectedEventArgs(result.RemainingAttempts));
            // El Host vuelve a mandar un PinRequest para el siguiente intento; el bucle continúa.
        }
    }

    /// <summary>
    /// Tras autenticar, la sesión queda "viva" mandando latidos periódicos. La recepción
    /// de video (Fase 5) y el envío de entrada (Fase 6) se añadirán sobre este mismo bucle.
    /// </summary>
    private async Task RunPostAuthLoopAsync(CancellationToken cancellationToken)
    {
        using var pingTimer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        var pingLoop = RunPingLoopAsync(pingTimer, cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await MessageFramer.ReadAsync(_stream!, cancellationToken).ConfigureAwait(false);
                if (message is null)
                {
                    RaiseSessionEnded(SessionEndReason.ConnectionLost, "El Host cerró la conexión sin avisar.");
                    return;
                }

                if (message.Value.Type == MessageType.Bye)
                {
                    RaiseSessionEnded(SessionEndReason.RemoteClosed);
                    return;
                }

                // El "Pong" de respuesta a nuestros latidos no necesita acción propia:
                // el solo hecho de poder leerlo confirma que la conexión sigue viva.
                // Los mensajes de video/entrada (Fases 5/6) se manejarán acá cuando existan.
            }
        }
        finally
        {
            pingTimer.Dispose();
            await pingLoop.ConfigureAwait(false);
        }
    }

    private async Task RunPingLoopAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await MessageFramer.WriteAsync(_stream!, MessageType.Ping, [], cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelación esperada al cerrar la sesión.
        }
        catch (ObjectDisposedException)
        {
            // El timer se dispuso al salir del bucle principal; nada que hacer.
        }
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
        _sessionCts?.Dispose();
    }
}
