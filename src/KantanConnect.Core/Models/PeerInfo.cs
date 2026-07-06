namespace KantanConnect.Core.Models;

/// <summary>
/// Representa a otro equipo de la LAN que fue descubierto (o se anuncia a sí mismo)
/// a través del beacon de <c>Discovery</c>.
/// </summary>
public sealed class PeerInfo
{
    /// <summary>Identificador único generado por instancia de la app (GUID como texto).</summary>
    public required string Id { get; init; }

    /// <summary>Nombre amigable a mostrar en la UI (por defecto, el nombre del equipo).</summary>
    public required string DisplayName { get; init; }

    /// <summary>Dirección IP del peer en la LAN.</summary>
    public required string IpAddress { get; init; }

    /// <summary>Puerto TCP donde ese peer escucha sesiones de host (compartir pantalla).</summary>
    public required int TcpPort { get; init; }

    /// <summary>Versión de protocolo/app del peer, útil para diagnósticos futuros.</summary>
    public required string Version { get; init; }

    /// <summary>Última vez (UTC) que se recibió un beacon de este peer.</summary>
    public DateTimeOffset LastSeenUtc { get; set; }
}
