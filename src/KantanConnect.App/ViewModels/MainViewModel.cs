using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using KantanConnect.Core.Discovery;

namespace KantanConnect.App.ViewModels;

/// <summary>
/// ViewModel de la ventana principal: mantiene la lista de peers descubiertos en la LAN
/// y expone los comandos "Compartir mi pantalla" / "Conectar". Es el primer punto donde
/// se conectan las piezas de <c>Core</c> (protocolo/descubrimiento) con la UI de <c>App</c>.
/// </summary>
public sealed class MainViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly string _localPeerId = Guid.NewGuid().ToString("N");
    private readonly string _localDisplayName = Environment.MachineName;
    private readonly Dispatcher _dispatcher;
    private readonly DiscoveryListener _discoveryListener;
    private DiscoveryBroadcaster? _activeBroadcaster;

    private bool _isSharing;
    private string _statusText = "Buscando otros equipos en la red...";

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
            StopSharing();
        }
        else
        {
            StartSharing();
        }
    }

    private void StartSharing()
    {
        var beacon = new DiscoveryBeacon
        {
            App = DiscoveryBeacon.AppIdentifier,
            PeerId = _localPeerId,
            DisplayName = _localDisplayName,
            TcpPort = DiscoveryDefaults.TcpSessionPort,
            ProtocolVersion = "1.0",
        };

        _activeBroadcaster = new DiscoveryBroadcaster(beacon);
        _activeBroadcaster.Start();
        IsSharing = true;
        StatusText = $"Compartiendo como \"{_localDisplayName}\". Esperando que alguien se conecte...";
    }

    private void StopSharing()
    {
        // Fire-and-forget intencional: ToggleSharing es síncrono porque lo dispara
        // directamente un RelayCommand. Detener el broadcaster es solo cancelar su
        // bucle en memoria (sin I/O bloqueante), así que no hace falta await aquí.
        _ = _activeBroadcaster?.StopAsync();
        _activeBroadcaster = null;
        IsSharing = false;
        StatusText = "Buscando otros equipos en la red...";
    }

    private void ConnectToSelectedPeer()
    {
        // La sesión TCP + emparejamiento por PIN se implementa en la Fase 3.
        // Por ahora dejamos explícito en la UI que el botón ya sabe A QUIÉN
        // conectarse, aunque todavía no exista el mecanismo para hacerlo.
        if (SelectedPeer is null)
        {
            return;
        }

        MessageBox.Show(
            $"Conectar a \"{SelectedPeer.DisplayName}\" ({SelectedPeer.IpAddress}:{SelectedPeer.TcpPort}) " +
            "se implementará en la Fase 3 (sesión TCP + PIN).",
            "Kantan Connect",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnPeerDiscoveredOrUpdated(object? sender, PeerChangedEventArgs e)
    {
        _dispatcher.Invoke(() =>
        {
            var existing = Peers.FirstOrDefault(p => p.Id == e.Peer.Id);
            if (existing is not null)
            {
                Peers.Remove(existing);
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
        if (_activeBroadcaster is not null)
        {
            await _activeBroadcaster.DisposeAsync();
        }

        await _discoveryListener.DisposeAsync();
    }
}
