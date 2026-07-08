using System.Windows;
using KantanConnect.App.Services;
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

        // Sin esto, una excepción no capturada durante el arranque (por ejemplo, un
        // recurso XAML que no se encuentra en tiempo de ejecución) hace que el proceso
        // termine en silencio, sin ninguna ventana ni mensaje — exactamente el tipo de
        // fallo "no pasa nada, no abre nada" que es imposible de diagnosticar a ciegas.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

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
            var loc = LocalizationService.Instance;
            MessageBox.Show(
                loc["FirewallRulesFailedMessage"],
                loc["FirewallRulesFailedTitle"],
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
            var loc = LocalizationService.Instance;
            MessageBox.Show(
                loc["AdminRequiredMessage"],
                loc["AdminRequiredTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        Shutdown();
    }

    private void OnDispatcherUnhandledException(
        object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        var loc = LocalizationService.Instance;
        MessageBox.Show(
            loc.Format("ErrorDialogMessageFormat", e.Exception.Message),
            loc["ErrorDialogTitle"],
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Shutdown();
    }
}
