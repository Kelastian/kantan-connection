using KantanConnect.Core.Models;

namespace KantanConnect.App.ViewModels;

/// <summary>
/// Envoltorio de un <see cref="PeerInfo"/> para mostrarlo en la lista de la UI.
/// Separado del DTO de <c>Core</c> porque a futuro puede necesitar propiedades
/// propias de presentación (por ejemplo, un ícono de estado) que no tienen sentido
/// en el modelo de datos puro que viaja por la red.
/// </summary>
public sealed class PeerListItemViewModel : ViewModelBase
{
    private string _displayName;
    private string _ipAddress;
    private int _tcpPort;

    public PeerListItemViewModel(PeerInfo peer)
    {
        Id = peer.Id;
        _displayName = peer.DisplayName;
        _ipAddress = peer.IpAddress;
        _tcpPort = peer.TcpPort;
    }

    public string Id { get; }

    public string DisplayName
    {
        get => _displayName;
        private set => SetField(ref _displayName, value);
    }

    public string IpAddress
    {
        get => _ipAddress;
        private set => SetField(ref _ipAddress, value);
    }

    public int TcpPort
    {
        get => _tcpPort;
        private set => SetField(ref _tcpPort, value);
    }

    /// <summary>
    /// Refresca los datos de presentación cuando vuelve a llegar un beacon del mismo
    /// peer, sin recrear el objeto — así WPF conserva la selección actual en la lista
    /// (ver <c>ViewModels.notas.md</c>: recrear el ítem en cada beacon perdía la
    /// selección cada ~1 segundo, justo el intervalo de <c>DiscoveryBroadcaster</c>).
    /// </summary>
    public void UpdateFrom(PeerInfo peer)
    {
        DisplayName = peer.DisplayName;
        IpAddress = peer.IpAddress;
        TcpPort = peer.TcpPort;
    }
}
