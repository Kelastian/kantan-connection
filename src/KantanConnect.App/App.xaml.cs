using System.Windows;
using KantanConnect.Windows.Platform;

namespace KantanConnect.App;

/// <summary>
/// Punto de entrada de la aplicación WPF. Antes de mostrar cualquier ventana, se
/// asegura de que el proceso esté elevado (Administrador) y de que las reglas de
/// Firewall necesarias existan — la secuencia de arranque "0 problemas" descrita en
/// el plan (sección 1: el usuario nunca configura nada a mano).
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!AdminHelper.IsRunningAsAdministrator())
        {
            RelaunchElevatedAndShutdown();
            return;
        }

        // Si esto falla (por ejemplo, el usuario tiene deshabilitado el Firewall de
        // Windows, o usa un firewall de terceros), no bloqueamos el arranque: el
        // descubrimiento/conexión pueden seguir funcionando igual si el firewall no
        // está activo, y si sí lo está, HostWindow/MainWindow ya muestran errores
        // claros de conexión cuando algo no llega (ver Fase 3/2 y sus notas).
        var firewallResult = FirewallHelper.EnsureRulesExist();
        if (!firewallResult.AllRulesReady)
        {
            MessageBox.Show(
                "No se pudieron crear automáticamente las reglas de Firewall de Windows. " +
                "Es posible que el descubrimiento de otros equipos o las conexiones fallen. " +
                "Podés seguir usando la app; si hay problemas, revisá el Firewall de Windows manualmente.",
                "Kantan Connect",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        new MainWindow().Show();
    }

    private void RelaunchElevatedAndShutdown()
    {
        var relaunched = AdminHelper.TryRelaunchElevated();

        if (!relaunched)
        {
            MessageBox.Show(
                "Kantan Connect necesita ejecutarse como Administrador para poder " +
                "controlar el mouse/teclado remoto y configurar el Firewall automáticamente. " +
                "Volvé a abrir la aplicación y aceptá el permiso de administrador cuando Windows lo pida.",
                "Kantan Connect — Se requieren permisos de Administrador",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        Shutdown();
    }
}
