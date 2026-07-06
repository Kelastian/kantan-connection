using System.Text.Json;
using KantanConnect.Core.Discovery;

namespace KantanConnect.Core.Tests.Discovery;

public class DiscoveryBeaconTests
{
    [Fact]
    public void SerializeThenDeserialize_RoundTripsAllFields()
    {
        var original = new DiscoveryBeacon
        {
            App = DiscoveryBeacon.AppIdentifier,
            PeerId = "peer-123",
            DisplayName = "PC-ABUELA",
            TcpPort = 47801,
            ProtocolVersion = "1.0",
        };

        var json = JsonSerializer.Serialize(original);
        var roundTripped = JsonSerializer.Deserialize<DiscoveryBeacon>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.App, roundTripped!.App);
        Assert.Equal(original.PeerId, roundTripped.PeerId);
        Assert.Equal(original.DisplayName, roundTripped.DisplayName);
        Assert.Equal(original.TcpPort, roundTripped.TcpPort);
        Assert.Equal(original.ProtocolVersion, roundTripped.ProtocolVersion);
    }

    [Fact]
    public void Deserialize_UnknownExtraFields_DoesNotThrow()
    {
        // Simula un beacon de una versión futura con campos nuevos: no debe romper
        // el parseo de una versión anterior de la app (compatibilidad hacia adelante).
        var json = """
            {
                "App": "KantanConnect",
                "PeerId": "peer-1",
                "DisplayName": "PC-1",
                "TcpPort": 47801,
                "ProtocolVersion": "1.0",
                "FuturoCampoDesconocido": 42
            }
            """;

        var beacon = JsonSerializer.Deserialize<DiscoveryBeacon>(json);

        Assert.NotNull(beacon);
        Assert.Equal("peer-1", beacon!.PeerId);
    }
}
