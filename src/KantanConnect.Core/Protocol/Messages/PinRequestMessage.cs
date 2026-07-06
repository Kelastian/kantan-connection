namespace KantanConnect.Core.Protocol.Messages;

/// <summary>El Host le pide al Viewer que solicite un PIN al usuario y lo envíe.</summary>
public sealed class PinRequestMessage
{
    /// <summary>Cuántos dígitos tiene el PIN, para que la UI del Viewer arme el campo adecuado.</summary>
    public required int PinLength { get; init; }
}
