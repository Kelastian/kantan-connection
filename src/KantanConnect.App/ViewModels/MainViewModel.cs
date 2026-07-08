using System.Collections.ObjectModel;
using System.Windows.Threading;
using KantanConnect.App.Services;
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
    private string _statusText = string.Empty;
    private ConnectionStatus _connectionStatus = ConnectionStatus.Idle;
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

        // Recalcula StatusText con el idioma nuevo cuando el usuario lo cambia (ver
        // LocalizationService.notas.md): sin esto, el texto quedaría "congelado" en el
        // idioma que estaba activo cuando se generó por última vez.
        LocalizationService.Instance.PropertyChanged += (_, _) => RefreshStatusText();
        RefreshStatusText();
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

    /// <summary>Para el punto de color de la ventana (ver <c>ConnectionStatusToBrushConverter</c>).</summary>
    public ConnectionStatus ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetField(ref _connectionStatus, value);
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
        ConnectionStatus = ConnectionStatus.Busy;
        RefreshStatusText();
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
        ConnectionStatus = ConnectionStatus.Idle;
        RefreshStatusText();
    }

    /// <summary>
    /// Reconstruye <see cref="StatusText"/> a partir del estado actual (<see cref="IsSharing"/>)
    /// en el idioma vigente. Centralizar esto acá (en vez de asignar el texto directo en
    /// cada transición, como en fases anteriores) es lo que permite refrescar el texto
    /// cuando el usuario cambia de idioma sin tener que repetir esa lógica en dos lugares.
    /// </summary>
    private void RefreshStatusText()
    {
        StatusText = IsSharing
            ? LocalizationService.Instance.Format("MainViewModel_SharingAsStatusFormat", _localDisplayName)
            : LocalizationService.Instance["MainViewModel_SearchingStatus"];
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
