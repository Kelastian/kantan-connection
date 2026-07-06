using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using KantanConnect.Core.Models;

namespace KantanConnect.Core.Discovery;

/// <summary>Datos de un cambio en la lista de peers descubiertos.</summary>
public sealed class PeerChangedEventArgs(PeerInfo peer) : EventArgs
{
    public PeerInfo Peer { get; } = peer;
}

/// <summary>
/// Escucha el puerto UDP de descubrimiento y mantiene una lista viva de
/// <see cref="PeerInfo"/>, agregando/actualizando peers al recibir beacons y quitando
/// automáticamente los que dejaron de anunciarse (ver <see cref="DiscoveryDefaults.PeerExpiration"/>).
///
/// Pensado para instanciarse una sola vez por app (no por sesión) y correr durante
/// toda la vida del programa, ya que el usuario puede querer ver la lista de PCs
/// disponibles en todo momento, no solo mientras comparte su propia pantalla.
/// </summary>
public sealed class DiscoveryListener : IAsyncDisposable
{
    private readonly string _localPeerId;
    private readonly UdpClient _udpClient;
    private readonly ConcurrentDictionary<string, PeerInfo> _peersById = new();
    private CancellationTokenSource? _loopCts;
    private Task? _receiveLoopTask;
    private Task? _expirationLoopTask;

    /// <summary>Se dispara cuando aparece un peer nuevo o se refresca uno existente.</summary>
    public event EventHandler<PeerChangedEventArgs>? PeerDiscoveredOrUpdated;

    /// <summary>Se dispara cuando un peer deja de anunciarse y se lo quita de la lista.</summary>
    public event EventHandler<PeerChangedEventArgs>? PeerLost;

    /// <param name="localPeerId">
    /// El <see cref="PeerInfo.Id"/> propio de esta instancia, para descartar los beacons
    /// que uno mismo emite (evita que la app "se descubra a sí misma" en la lista).
    /// </param>
    public DiscoveryListener(string localPeerId, int udpPort = DiscoveryDefaults.UdpDiscoveryPort)
    {
        _localPeerId = localPeerId;
        _udpClient = new UdpClient();
        _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, udpPort));
    }

    public IReadOnlyCollection<PeerInfo> CurrentPeers => _peersById.Values.ToList();

    public void Start()
    {
        if (_receiveLoopTask is not null)
        {
            return;
        }

        _loopCts = new CancellationTokenSource();
        _receiveLoopTask = RunReceiveLoopAsync(_loopCts.Token);
        _expirationLoopTask = RunExpirationLoopAsync(_loopCts.Token);
    }

    public async Task StopAsync()
    {
        if (_loopCts is null)
        {
            return;
        }

        _loopCts.Cancel();

        try
        {
            await Task.WhenAll(
                _receiveLoopTask ?? Task.CompletedTask,
                _expirationLoopTask ?? Task.CompletedTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelación esperada al detener los bucles; no representa un error real.
        }

        _loopCts.Dispose();
        _loopCts = null;
        _receiveLoopTask = null;
        _expirationLoopTask = null;
    }

    private async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                continue;
            }

            TryHandleBeacon(result);
        }
    }

    private void TryHandleBeacon(UdpReceiveResult result)
    {
        DiscoveryBeacon? beacon;
        try
        {
            beacon = JsonSerializer.Deserialize<DiscoveryBeacon>(result.Buffer);
        }
        catch (JsonException)
        {
            // Tráfico UDP que no es un beacon nuestro (ruido de otros dispositivos
            // de la LAN); se descarta en silencio, no es un error del programa.
            return;
        }

        if (beacon is null || beacon.App != DiscoveryBeacon.AppIdentifier || beacon.PeerId == _localPeerId)
        {
            return;
        }

        var peer = new PeerInfo
        {
            Id = beacon.PeerId,
            DisplayName = beacon.DisplayName,
            IpAddress = result.RemoteEndPoint.Address.ToString(),
            TcpPort = beacon.TcpPort,
            Version = beacon.ProtocolVersion,
            LastSeenUtc = DateTimeOffset.UtcNow,
        };

        _peersById[peer.Id] = peer;
        PeerDiscoveredOrUpdated?.Invoke(this, new PeerChangedEventArgs(peer));
    }

    private async Task RunExpirationLoopAsync(CancellationToken cancellationToken)
    {
        // Revisar a la mitad del intervalo de expiración es suficiente granularidad
        // sin generar comprobaciones excesivas para un valor pensado en segundos.
        var checkInterval = TimeSpan.FromMilliseconds(DiscoveryDefaults.PeerExpiration.TotalMilliseconds / 2);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(checkInterval, cancellationToken).ConfigureAwait(false);

            var expiredCutoff = DateTimeOffset.UtcNow - DiscoveryDefaults.PeerExpiration;
            foreach (var peer in _peersById.Values)
            {
                if (peer.LastSeenUtc < expiredCutoff && _peersById.TryRemove(peer.Id, out var removed))
                {
                    PeerLost?.Invoke(this, new PeerChangedEventArgs(removed));
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _udpClient.Dispose();
    }
}
