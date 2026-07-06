using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KantanConnect.App.ViewModels;

/// <summary>
/// Implementación compartida de <see cref="INotifyPropertyChanged"/>, la interfaz que
/// WPF usa para enterarse de que una propiedad cambió y refrescar la UI enlazada.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Asigna <paramref name="field"/> y notifica a la UI solo si el valor realmente
    /// cambió, evitando refrescos de pantalla innecesarios.
    /// </summary>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
