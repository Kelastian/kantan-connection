namespace KantanConnect.Core.Discovery;

/// <summary>
/// Puertos e intervalos de red por defecto, compartidos entre Host y Viewer.
/// Vivir en una sola constante evita que "47800" quede repetido (y potencialmente
/// desincronizado) en distintos archivos de Broadcaster/Listener/HostSession.
/// </summary>
public static class DiscoveryDefaults
{
    /// <summary>Puerto UDP donde se emiten y escuchan los beacons de descubrimiento.</summary>
    public const int UdpDiscoveryPort = 47800;

    /// <summary>Puerto TCP por defecto donde el Host escucha conexiones de Viewers.</summary>
    public const int TcpSessionPort = 47801;

    /// <summary>Cada cuánto un Host activo repite su beacon.</summary>
    public static readonly TimeSpan BroadcastInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Si no llega un beacon nuevo de un peer en este tiempo, se considera perdido
    /// y se quita de la lista (cubre el caso de que alguien cierre la app de golpe,
    /// sin mandar un "Bye" explícito).
    /// </summary>
    public static readonly TimeSpan PeerExpiration = TimeSpan.FromSeconds(4);
}
