using System.Windows;
using KantanConnect.App.ViewModels;

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
}
