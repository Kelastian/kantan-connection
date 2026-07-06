namespace KantanConnect.Core.Protocol.Messages;

/// <summary>Respuesta del Host a un <see cref="PinAttemptMessage"/>.</summary>
public sealed class PinResultMessage
{
    public required bool Accepted { get; init; }

    /// <summary>
    /// Intentos restantes cuando <see cref="Accepted"/> es false. Al llegar a 0,
    /// el Host debe cerrar la conexión (política de máx. 3 intentos, implementada
    /// en <c>HostSession</c> en la Fase 3).
    /// </summary>
    public int RemainingAttempts { get; init; }
}
