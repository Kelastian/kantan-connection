using KantanConnect.Core.Models;

namespace KantanConnect.Core.Abstractions;

/// <summary>
/// Contrato para "algo que sabe reproducir entrada de ratón/teclado en el sistema real".
/// La implementación de Windows (Fase 6) usa <c>SendInput</c> vía P/Invoke; queda en
/// <c>KantanConnect.Windows</c> para que <c>Core</c> siga sin dependencias de plataforma.
/// </summary>
public interface IInputInjector
{
    void Inject(InputEvent inputEvent);
}
