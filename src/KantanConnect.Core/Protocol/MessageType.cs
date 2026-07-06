namespace KantanConnect.Core.Protocol;

/// <summary>
/// Identifica qué tipo de mensaje va dentro de un frame TCP (ver <see cref="MessageFramer"/>).
/// El valor numérico se serializa como un solo byte, así que el orden importa poco,
/// pero una vez publicado no se deben reordenar los existentes (romper compatibilidad).
/// </summary>
public enum MessageType : byte
{
    /// <summary>Primer mensaje que manda el Viewer al conectar por TCP: quién es.</summary>
    Hello = 1,

    /// <summary>El Host responde con la resolución de su pantalla.</summary>
    ScreenInfo = 2,

    /// <summary>El Host pide al Viewer que muestre un prompt de PIN.</summary>
    PinRequest = 3,

    /// <summary>El Viewer envía el PIN que el usuario escribió.</summary>
    PinAttempt = 4,

    /// <summary>El Host informa si el PIN fue aceptado o rechazado.</summary>
    PinResult = 5,

    /// <summary>Un fotograma de video codificado en JPEG.</summary>
    FrameData = 6,

    /// <summary>Un evento de ratón/teclado enviado por el Viewer.</summary>
    InputEvent = 7,

    /// <summary>Latido para detectar conexiones caídas.</summary>
    Ping = 8,

    /// <summary>Respuesta a <see cref="Ping"/>.</summary>
    Pong = 9,

    /// <summary>Cierre ordenado de la sesión, con motivo legible.</summary>
    Bye = 10,
}
