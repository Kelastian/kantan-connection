using System.Windows;
using KantanConnect.App.ViewModels;

namespace KantanConnect.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closed += OnClosed;
    }

    private async void OnClosed(object? sender, EventArgs e)
    {
        await _viewModel.DisposeAsync();
    }
}
