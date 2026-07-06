namespace KantanConnect.Core.Protocol.Messages;

/// <summary>Cierre ordenado de sesión, enviado por cualquiera de las dos partes.</summary>
public sealed class ByeMessage
{
    public required string Reason { get; init; }
}
