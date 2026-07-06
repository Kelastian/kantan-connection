using KantanConnect.Core.Models;

namespace KantanConnect.App.ViewModels;

/// <summary>
/// Envoltorio de un <see cref="PeerInfo"/> para mostrarlo en la lista de la UI.
/// Separado del DTO de <c>Core</c> porque a futuro puede necesitar propiedades
/// propias de presentación (por ejemplo, un ícono de estado) que no tienen sentido
/// en el modelo de datos puro que viaja por la red.
/// </summary>
public sealed class PeerListItemViewModel(PeerInfo peer)
{
    public string Id { get; } = peer.Id;

    public string DisplayName { get; } = peer.DisplayName;

    public string IpAddress { get; } = peer.IpAddress;

    public int TcpPort { get; } = peer.TcpPort;
}
