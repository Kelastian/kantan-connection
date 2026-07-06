using System.Collections.ObjectModel;
using System.Windows.Threading;
using KantanConnect.Core.Discovery;

namespace KantanConnect.App.ViewModels;

/// <summary>
/// ViewModel de la ventana principal: mantiene la lista de peers descubiertos en la LAN
/// y expone los comandos "Compartir mi pantalla" / "Conectar", que abren
/// <c>HostWindow</c>/<c>ViewerWindow</c> respectivamente. Es el primer punto donde se
/// conectan las piezas de <c>Core</c> (protocolo/descubrimiento) con la UI de <c>App</c>.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly string _localPeerId = Guid.NewGuid().ToString("N");
    private readonly string _localDisplayName = Environment.MachineName;
    private readonly Dispatcher _dispatcher;
    private readonly DiscoveryListener _discoveryListener;

    private bool _isSharing;
    private string _statusText = "Buscando otros equipos en la red...";
    private HostWindow? _hostWindow;

    public MainViewModel()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _discoveryListener = new DiscoveryListener(_localPeerId);
        _discoveryListener.PeerDiscoveredOrUpdated += OnPeerDiscoveredOrUpdated;
        _discoveryListener.PeerLost += OnPeerLost;
        _discoveryListener.Start();

        ShareScreenCommand = new RelayCommand(ToggleSharing);
        ConnectCommand = new RelayCommand(ConnectToSelectedPeer, () => SelectedPeer is not null);
    }

    public ObservableCollection<PeerListItemViewModel> Peers { get; } = [];

    private PeerListItemViewModel? _selectedPeer;
    public PeerListItemViewModel? SelectedPeer
    {
        get => _selectedPeer;
        set => SetField(ref _selectedPeer, value);
    }

    public bool IsSharing
    {
        get => _isSharing;
        private set => SetField(ref _isSharing, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public RelayCommand ShareScreenCommand { get; }

    public RelayCommand ConnectCommand { get; }

    private void ToggleSharing()
    {
        if (IsSharing)
        {
            _hostWindow?.Close();
        }
        else
        {
            OpenHostWindow();
        }
    }

    private void OpenHostWindow()
    {
        _hostWindow = new HostWindow(_localPeerId, _localDisplayName)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        _hostWindow.Closed += OnHostWindowClosed;
        IsSharing = true;
        StatusText = $"Compartiendo como \"{_localDisplayName}\".";
        _hostWindow.Show();
    }

    private void OnHostWindowClosed(object? sender, EventArgs e)
    {
        if (_hostWindow is not null)
        {
            _hostWindow.Closed -= OnHostWindowClosed;
        }

        _hostWindow = null;
        IsSharing = false;
        StatusText = "Buscando otros equipos en la red...";
    }

    private void ConnectToSelectedPeer()
    {
        if (SelectedPeer is null)
        {
            return;
        }

        var viewerWindow = new ViewerWindow(
            SelectedPeer.IpAddress, SelectedPeer.TcpPort, SelectedPeer.DisplayName)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        viewerWindow.Show();
    }

    private void OnPeerDiscoveredOrUpdated(object? sender, PeerChangedEventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            var existing = Peers.FirstOrDefault(p => p.Id == e.Peer.Id);
            if (existing is not null)
            {
                // Actualizar in-place (no Remove+Add): recrear el ítem en cada beacon
                // (cada ~1s, ver DiscoveryBroadcaster) le hacía perder a WPF la
                // selección de la fila justo mientras el usuario intentaba hacer clic.
                existing.UpdateFrom(e.Peer);
                return;
            }

            Peers.Add(new PeerListItemViewModel(e.Peer));
        });
    }

    private void OnPeerLost(object? sender, PeerChangedEventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            var existing = Peers.FirstOrDefault(p => p.Id == e.Peer.Id);
            if (existing is not null)
            {
                Peers.Remove(existing);
            }

            if (SelectedPeer?.Id == e.Peer.Id)
            {
                SelectedPeer = null;
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        _hostWindow?.Close();
        await _discoveryListener.DisposeAsync();
    }
}
