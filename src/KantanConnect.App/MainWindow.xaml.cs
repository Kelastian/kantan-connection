using System.Windows;
using KantanConnect.App.Services;
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

    private void OnLanguageToggleClick(object sender, RoutedEventArgs e) =>
        LocalizationService.Instance.ToggleLanguage();

    private async void OnClosed(object? sender, EventArgs e)
    {
        await _viewModel.DisposeAsync();
    }
}
