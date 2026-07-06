namespace KantanConnect.Core.Protocol.Messages;

/// <summary>Primer mensaje de un Viewer al conectar: se presenta ante el Host.</summary>
public sealed class HelloMessage
{
    public required string ViewerId { get; init; }

    public required string ViewerDisplayName { get; init; }

    public required string ProtocolVersion { get; init; }
}
