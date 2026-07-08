namespace KantanConnect.App.ViewModels;

/// <summary>
/// Estado semántico de una ventana en términos de "qué punto de color mostrar" (ver
/// <c>StatusDotBrushConverter</c>), separado del texto libre de <c>StatusText</c>.
/// Se modela como enum (en vez de exponer un <see cref="System.Windows.Media.Brush"/>
/// directo desde el ViewModel) para no acoplar el ViewModel a tipos de WPF — mismo
/// criterio que ya se sigue en el resto del proyecto (ver <c>ViewModels.notas.md</c>).
/// </summary>
public enum ConnectionStatus
{
    Idle,
    Busy,
    Connected,
    Error,
}
