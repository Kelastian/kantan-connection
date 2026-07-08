using System.Windows;
using System.Windows.Input;
using KantanConnect.App.ViewModels;
using KantanConnect.Core.Models;

namespace KantanConnect.App;

public partial class ViewerWindow : Window
{
    private readonly ViewerViewModel _viewModel;

    public ViewerWindow(string hostIpAddress, int hostTcpPort, string hostDisplayName)
    {
        InitializeComponent();
        _viewModel = new ViewerViewModel(hostIpAddress, hostTcpPort, hostDisplayName);
        DataContext = _viewModel;
        Closed += OnClosed;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private async void OnClosed(object? sender, EventArgs e)
    {
        await _viewModel.DisposeAsync();
    }

    private void OnRemoteScreenMouseMove(object sender, MouseEventArgs e)
    {
        if (!TryGetNormalizedPosition(e.GetPosition(RemoteScreenImage), out var normalizedX, out var normalizedY))
        {
            return;
        }

        _viewModel.SendInputEvent(new InputEvent
        {
            Kind = InputEventKind.MouseMove,
            NormalizedX = normalizedX,
            NormalizedY = normalizedY,
        });
    }

    private void OnRemoteScreenMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Enfocar la imagen al hacer clic es lo que permite que, después, los eventos de
        // teclado (KeyDown/KeyUp) le lleguen a este control en vez de perderse: WPF solo
        // entrega teclado al elemento que tiene el foco de teclado en ese momento.
        RemoteScreenImage.Focus();

        if (!TryMapMouseButton(e.ChangedButton, out var button)
            || !TryGetNormalizedPosition(e.GetPosition(RemoteScreenImage), out var normalizedX, out var normalizedY))
        {
            return;
        }

        _viewModel.SendInputEvent(new InputEvent
        {
            Kind = InputEventKind.MouseButtonDown,
            Button = button,
            NormalizedX = normalizedX,
            NormalizedY = normalizedY,
        });
    }

    private void OnRemoteScreenMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!TryMapMouseButton(e.ChangedButton, out var button)
            || !TryGetNormalizedPosition(e.GetPosition(RemoteScreenImage), out var normalizedX, out var normalizedY))
        {
            return;
        }

        _viewModel.SendInputEvent(new InputEvent
        {
            Kind = InputEventKind.MouseButtonUp,
            Button = button,
            NormalizedX = normalizedX,
            NormalizedY = normalizedY,
        });
    }

    private void OnRemoteScreenMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!TryGetNormalizedPosition(e.GetPosition(RemoteScreenImage), out var normalizedX, out var normalizedY))
        {
            return;
        }

        _viewModel.SendInputEvent(new InputEvent
        {
            Kind = InputEventKind.MouseWheel,
            NormalizedX = normalizedX,
            NormalizedY = normalizedY,
            WheelDelta = e.Delta,
        });
    }

    private void OnRemoteScreenKeyDown(object sender, KeyEventArgs e)
    {
        _viewModel.SendInputEvent(new InputEvent
        {
            Kind = InputEventKind.KeyDown,
            VirtualKeyCode = KeyInterop.VirtualKeyFromKey(e.Key),
        });

        // Evita que teclas de navegación (Tab, flechas) muevan el foco dentro de la
        // ventana del Viewer en vez de viajar al Host: queremos que absolutamente todo
        // lo que se presione sobre la imagen se interprete como entrada remota.
        e.Handled = true;
    }

    private void OnRemoteScreenKeyUp(object sender, KeyEventArgs e)
    {
        _viewModel.SendInputEvent(new InputEvent
        {
            Kind = InputEventKind.KeyUp,
            VirtualKeyCode = KeyInterop.VirtualKeyFromKey(e.Key),
        });

        e.Handled = true;
    }

    private static bool TryMapMouseButton(MouseButton changedButton, out MouseButtonKind button)
    {
        button = changedButton switch
        {
            MouseButton.Left => MouseButtonKind.Left,
            MouseButton.Right => MouseButtonKind.Right,
            MouseButton.Middle => MouseButtonKind.Middle,
            _ => MouseButtonKind.None,
        };

        return button != MouseButtonKind.None;
    }

    /// <summary>
    /// Convierte una posición en píxeles del control <c>Image</c> a coordenadas
    /// normalizadas (0.0–1.0) relativas a la imagen realmente dibujada, no al control.
    ///
    /// Como el <c>Image</c> usa <c>Stretch="Uniform"</c>, si la relación de aspecto del
    /// control no coincide exactamente con la del video (por ejemplo, ventana más ancha
    /// que 16:9 mientras la pantalla del Host sí lo es), WPF centra la imagen y deja
    /// franjas vacías a los costados o arriba/abajo. Sin esta corrección, un clic en esa
    /// franja se traduciría a una coordenada inválida o friccionaría con el borde
    /// equivocado de la pantalla del Host.
    /// </summary>
    private bool TryGetNormalizedPosition(Point positionInControl, out double normalizedX, out double normalizedY)
    {
        normalizedX = 0;
        normalizedY = 0;

        if (RemoteScreenImage.Source is not System.Windows.Media.Imaging.BitmapSource source
            || RemoteScreenImage.ActualWidth <= 0 || RemoteScreenImage.ActualHeight <= 0)
        {
            return false;
        }

        // PixelWidth/PixelHeight (no Width/Height, que son unidades independientes de
        // dispositivo a 96 DPI) son la resolución real del JPEG decodificado — lo que
        // hace falta para calcular el aspect ratio correcto de la imagen renderizada.
        var imagePixelWidth = source.PixelWidth;
        var imagePixelHeight = source.PixelHeight;
        if (imagePixelWidth <= 0 || imagePixelHeight <= 0)
        {
            return false;
        }

        var controlWidth = RemoteScreenImage.ActualWidth;
        var controlHeight = RemoteScreenImage.ActualHeight;

        var scale = Math.Min(controlWidth / imagePixelWidth, controlHeight / imagePixelHeight);
        var renderedWidth = imagePixelWidth * scale;
        var renderedHeight = imagePixelHeight * scale;

        var offsetX = (controlWidth - renderedWidth) / 2;
        var offsetY = (controlHeight - renderedHeight) / 2;

        var xWithinImage = positionInControl.X - offsetX;
        var yWithinImage = positionInControl.Y - offsetY;

        if (xWithinImage < 0 || xWithinImage > renderedWidth || yWithinImage < 0 || yWithinImage > renderedHeight)
        {
            // El clic cayó en una franja vacía fuera de la imagen; no hay coordenada
            // válida que enviar.
            return false;
        }

        normalizedX = Math.Clamp(xWithinImage / renderedWidth, 0.0, 1.0);
        normalizedY = Math.Clamp(yWithinImage / renderedHeight, 0.0, 1.0);
        return true;
    }
}
