namespace KantanConnect.Core.Session;

/// <summary>Motivo por el cual una sesión (Host o Viewer) terminó.</summary>
public enum SessionEndReason
{
    /// <summary>El otro lado mandó un <c>Bye</c> ordenado.</summary>
    RemoteClosed,

    /// <summary>Se agotaron los intentos de PIN permitidos.</summary>
    PinAttemptsExhausted,

    /// <summary>Se perdió la conexión sin un cierre ordenado (red caída, proceso muerto).</summary>
    ConnectionLost,

    /// <summary>El lado local pidió cerrar (por ejemplo, el usuario cerró la ventana).</summary>
    LocalDisconnect,
}

/// <summary>Datos de un evento de fin de sesión.</summary>
public sealed class SessionEndedEventArgs(SessionEndReason reason, string? detail = null) : EventArgs
{
    public SessionEndReason Reason { get; } = reason;

    public string? Detail { get; } = detail;
}

/// <summary>Datos de un evento de PIN rechazado, para que la UI muestre intentos restantes.</summary>
public sealed class PinRejectedEventArgs(int remainingAttempts) : EventArgs
{
    public int RemainingAttempts { get; } = remainingAttempts;
}
