using System.Windows.Input;

namespace KantanConnect.App.ViewModels;

/// <summary>
/// Implementación mínima de <see cref="ICommand"/> para poder enlazar botones de WPF
/// (<c>Button Command="{Binding ...}"</c>) a métodos simples del ViewModel, sin traer
/// una librería MVVM externa para algo que son ~20 líneas de código.
/// </summary>
public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => execute();
}
