namespace KantanConnect.Core.Discovery;

/// <summary>
/// El "grito" que un Host manda por UDP Broadcast cada cierto intervalo mientras está
/// compartiendo pantalla, para que otros equipos de la LAN lo descubran solos.
/// Se serializa como JSON plano (ver <c>DiscoveryBroadcaster</c> / <c>DiscoveryListener</c>
/// en la Fase 2), así que sus propiedades deben ser simples y estables entre versiones.
/// </summary>
public sealed class DiscoveryBeacon
{
    /// <summary>Constante para poder distinguir beacons de Kantan Connect de ruido UDP en la red.</summary>
    public const string AppIdentifier = "KantanConnect";

    public required string App { get; init; } = AppIdentifier;

    public required string PeerId { get; init; }

    public required string DisplayName { get; init; }

    public required int TcpPort { get; init; }

    public required string ProtocolVersion { get; init; }
}
