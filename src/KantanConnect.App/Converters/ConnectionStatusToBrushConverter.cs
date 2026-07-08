using System.Globalization;
using System.Windows;
using System.Windows.Data;
using KantanConnect.App.ViewModels;

namespace KantanConnect.App.Converters;

/// <summary>
/// Traduce un <see cref="ConnectionStatus"/> al <c>Brush</c> con nombre correspondiente
/// definido en <c>App.xaml</c> (<c>StatusIdleBrush</c>, etc.), para que el punto de
/// color de cada ventana (<c>StatusDotStyle</c>) siga el mismo semáforo en toda la app.
/// </summary>
public sealed class ConnectionStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var resourceKey = value switch
        {
            ConnectionStatus.Busy => "StatusBusyBrush",
            ConnectionStatus.Connected => "StatusConnectedBrush",
            ConnectionStatus.Error => "StatusErrorBrush",
            _ => "StatusIdleBrush",
        };

        return Application.Current.TryFindResource(resourceKey)
            ?? throw new InvalidOperationException($"No se encontró el recurso '{resourceKey}' en App.xaml.");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
