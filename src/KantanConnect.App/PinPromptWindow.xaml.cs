using System.Windows;
using System.Windows.Threading;

namespace KantanConnect.App;

public partial class PinPromptWindow : Window
{
    private string? _enteredPin;

    public PinPromptWindow(int pinLength, string? previousAttemptMessage)
    {
        InitializeComponent();
        PinTextBox.MaxLength = pinLength;
        PromptText.Text = previousAttemptMessage
            ?? $"Ingresá el PIN de {pinLength} dígitos mostrado en el otro equipo:";
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        _enteredPin = PinTextBox.Text;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    /// <summary>
    /// Muestra el diálogo modal desde el hilo de UI (usando <paramref name="dispatcher"/>,
    /// ya que <see cref="ViewerSession.RequestPinFromUserAsync"/> se invoca desde el hilo
    /// de fondo de la sesión TCP) y devuelve el PIN ingresado, o cadena vacía si se canceló.
    /// </summary>
    public static Task<string> PromptAsync(Dispatcher dispatcher, int pinLength, string? previousAttemptMessage)
    {
        return dispatcher.InvokeAsync(() =>
        {
            var window = new PinPromptWindow(pinLength, previousAttemptMessage)
            {
                Owner = Application.Current.MainWindow,
            };

            return window.ShowDialog() == true ? window._enteredPin ?? string.Empty : string.Empty;
        }).Task;
    }
}
