namespace KantanConnect.Core.Models;

/// <summary>Tipo de evento de entrada que el Viewer puede enviar al Host.</summary>
public enum InputEventKind
{
    MouseMove,
    MouseButtonDown,
    MouseButtonUp,
    MouseWheel,
    KeyDown,
    KeyUp,
}

/// <summary>Botón del ratón involucrado (irrelevante para eventos que no son de botón).</summary>
public enum MouseButtonKind
{
    None,
    Left,
    Right,
    Middle,
}

/// <summary>
/// Evento de entrada capturado en el Viewer y enviado al Host para ser reproducido
/// con <c>SendInput</c>. Las coordenadas de ratón viajan <b>normalizadas (0.0 a 1.0)</b>
/// relativas al tamaño de pantalla del Host (ver <see cref="ScreenInfo"/>), para que
/// funcionen sin importar diferencias de resolución entre Host y Viewer.
/// </summary>
public sealed class InputEvent
{
    public required InputEventKind Kind { get; init; }

    /// <summary>Posición X normalizada (0.0 = borde izquierdo, 1.0 = borde derecho).</summary>
    public double NormalizedX { get; init; }

    /// <summary>Posición Y normalizada (0.0 = borde superior, 1.0 = borde inferior).</summary>
    public double NormalizedY { get; init; }

    public MouseButtonKind Button { get; init; } = MouseButtonKind.None;

    /// <summary>Delta de la rueda del ratón (positivo = arriba), solo para <see cref="InputEventKind.MouseWheel"/>.</summary>
    public int WheelDelta { get; init; }

    /// <summary>Código de tecla virtual de Windows (VK_*), solo para eventos de teclado.</summary>
    public int VirtualKeyCode { get; init; }
}
