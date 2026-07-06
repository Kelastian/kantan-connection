using KantanConnect.Core.Discovery;

namespace KantanConnect.Core.Tests.Discovery;

/// <summary>
/// Pruebas de integración en loopback (127.0.0.1) entre <see cref="DiscoveryBroadcaster"/>
/// y <see cref="DiscoveryListener"/> reales, usando un puerto UDP aleatorio por test para
/// poder correr en paralelo sin pisarse entre sí ni con una instancia real de la app.
/// </summary>
public class DiscoveryBroadcasterListenerTests
{
    private static int GetRandomEphemeralPort()
    {
        // Puerto 0 le pide al sistema operativo "elegí uno libre vos"; lo leemos y
        // cerramos enseguida para reutilizarlo en el broadcaster/listener del test.
        using var probe = new System.Net.Sockets.UdpClient(0);
        return ((System.Net.IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    [Fact]
    public async Task Listener_ReceivesBeacon_FromBroadcasterOnSameMachine()
    {
        var port = GetRandomEphemeralPort();
        var beacon = new DiscoveryBeacon
        {
            App = DiscoveryBeacon.AppIdentifier,
            PeerId = "host-peer-1",
            DisplayName = "PC-ABUELA",
            TcpPort = 47801,
            ProtocolVersion = "1.0",
        };

        await using var listener = new DiscoveryListener(localPeerId: "viewer-peer-1", udpPort: port);
        await using var broadcaster = new DiscoveryBroadcaster(beacon, udpPort: port);

        var discoveredTcs = new TaskCompletionSource<Models.PeerInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.PeerDiscoveredOrUpdated += (_, e) => discoveredTcs.TrySetResult(e.Peer);

        listener.Start();
        broadcaster.Start();

        var discovered = await WaitWithTimeoutAsync(discoveredTcs.Task, TimeSpan.FromSeconds(5));

        Assert.Equal("host-peer-1", discovered.Id);
        Assert.Equal("PC-ABUELA", discovered.DisplayName);
        Assert.Equal(47801, discovered.TcpPort);
    }

    [Fact]
    public async Task Listener_IgnoresBeacon_WithOwnLocalPeerId()
    {
        var port = GetRandomEphemeralPort();
        const string selfId = "self-peer";
        var beacon = new DiscoveryBeacon
        {
            App = DiscoveryBeacon.AppIdentifier,
            PeerId = selfId,
            DisplayName = "No debería aparecer",
            TcpPort = 47801,
            ProtocolVersion = "1.0",
        };

        await using var listener = new DiscoveryListener(localPeerId: selfId, udpPort: port);
        await using var broadcaster = new DiscoveryBroadcaster(beacon, udpPort: port);

        var sawAnyPeer = false;
        listener.PeerDiscoveredOrUpdated += (_, _) => sawAnyPeer = true;

        listener.Start();
        broadcaster.Start();

        // No hay evento que esperar (justamente estamos probando que nunca llega),
        // así que damos un margen razonable y verificamos que siga vacío.
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.False(sawAnyPeer);
        Assert.Empty(listener.CurrentPeers);
    }

    [Fact]
    public async Task Listener_ExpiresPeer_WhenBroadcasterStops()
    {
        var port = GetRandomEphemeralPort();
        var beacon = new DiscoveryBeacon
        {
            App = DiscoveryBeacon.AppIdentifier,
            PeerId = "host-peer-2",
            DisplayName = "PC-TEMPORAL",
            TcpPort = 47801,
            ProtocolVersion = "1.0",
        };

        await using var listener = new DiscoveryListener(localPeerId: "viewer-peer-2", udpPort: port);
        await using var broadcaster = new DiscoveryBroadcaster(beacon, udpPort: port);

        var discoveredTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lostTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.PeerDiscoveredOrUpdated += (_, _) => discoveredTcs.TrySetResult(true);
        listener.PeerLost += (_, _) => lostTcs.TrySetResult(true);

        listener.Start();
        broadcaster.Start();
        await WaitWithTimeoutAsync(discoveredTcs.Task, TimeSpan.FromSeconds(5));

        await broadcaster.StopAsync();

        await WaitWithTimeoutAsync(lostTcs.Task, DiscoveryDefaults.PeerExpiration + TimeSpan.FromSeconds(5));

        Assert.Empty(listener.CurrentPeers);
    }

    private static async Task<T> WaitWithTimeoutAsync<T>(Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            throw new TimeoutException($"La operación no completó dentro de {timeout}.");
        }

        return await task;
    }
}
