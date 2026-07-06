using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace KantanConnect.Core.Discovery;

/// <summary>
/// Mientras está activo, repite un <see cref="DiscoveryBeacon"/> por UDP Broadcast cada
/// <see cref="DiscoveryDefaults.BroadcastInterval"/>, para que los <see cref="DiscoveryListener"/>
/// de otros equipos de la LAN lo descubran sin ninguna IP escrita a mano.
///
/// Solo debe estar corriendo mientras el usuario tiene la pantalla en modo "compartir";
/// no es un servicio que arranca con la app entera (ver <c>Start</c>/<c>Stop</c>).
/// </summary>
public sealed class DiscoveryBroadcaster : IAsyncDisposable
{
    private readonly DiscoveryBeacon _beacon;
    private readonly UdpClient _udpClient;
    private readonly IPEndPoint _broadcastEndpoint;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public DiscoveryBroadcaster(DiscoveryBeacon beacon, int udpPort = DiscoveryDefaults.UdpDiscoveryPort)
    {
        _beacon = beacon;
        _udpClient = new UdpClient { EnableBroadcast = true };
        _broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, udpPort);
    }

    /// <summary>Empieza a emitir el beacon en un bucle de fondo. Llamar dos veces no hace nada.</summary>
    public void Start()
    {
        if (_loopTask is not null)
        {
            return;
        }

        _loopCts = new CancellationTokenSource();
        _loopTask = RunBroadcastLoopAsync(_loopCts.Token);
    }

    /// <summary>Detiene la emisión. Seguro de llamar aunque nunca se haya iniciado.</summary>
    public async Task StopAsync()
    {
        if (_loopCts is null)
        {
            return;
        }

        _loopCts.Cancel();

        try
        {
            await (_loopTask ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelación esperada al detener el bucle; no representa un error real.
        }

        _loopCts.Dispose();
        _loopCts = null;
        _loopTask = null;
    }

    private async Task RunBroadcastLoopAsync(CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(_beacon);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _udpClient.SendAsync(payload, _broadcastEndpoint, cancellationToken).ConfigureAwait(false);
            }
            catch (SocketException)
            {
                // Una red temporalmente caída no debe tumbar el Host; se reintenta
                // en la siguiente vuelta del bucle tras el delay de abajo.
            }

            await Task.Delay(DiscoveryDefaults.BroadcastInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _udpClient.Dispose();
    }
}
