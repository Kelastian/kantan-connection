using System.Windows;
using KantanConnect.App.ViewModels;

namespace KantanConnect.App;

public partial class HostWindow : Window
{
    private readonly HostViewModel _viewModel;

    public HostWindow(string localPeerId, string localDisplayName)
    {
        InitializeComponent();
        _viewModel = new HostViewModel(localPeerId, localDisplayName);
        DataContext = _viewModel;
        Closed += OnClosed;
    }

    private void OnStopSharingClick(object sender, RoutedEventArgs e) => Close();

    private async void OnClosed(object? sender, EventArgs e)
    {
        await _viewModel.DisposeAsync();
    }
}
