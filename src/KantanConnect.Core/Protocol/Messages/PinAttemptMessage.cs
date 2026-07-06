namespace KantanConnect.Core.Protocol.Messages;

/// <summary>El Viewer envía el PIN que el usuario escribió, para que el Host lo valide.</summary>
public sealed class PinAttemptMessage
{
    public required string Pin { get; init; }
}
