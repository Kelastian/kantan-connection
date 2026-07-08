using System.Diagnostics;
using KantanConnect.Core.Discovery;

namespace KantanConnect.Windows.Platform;

/// <summary>
/// Crea las reglas de entrada del Firewall de Windows que Kantan Connect necesita
/// (UDP de descubrimiento, TCP de sesión), usando <c>netsh advfirewall</c>. Se ejecuta
/// una sola vez al arrancar (ver <c>App.xaml.cs</c>), para que el usuario nunca tenga
/// que abrir el Firewall de Windows a mano — esa es la promesa de "0 problemas" del
/// proyecto (ver plan, sección 1).
/// </summary>
public static class FirewallHelper
{
    private const string UdpDiscoveryRuleName = "Kantan Connect - Descubrimiento (UDP)";
    private const string TcpSessionRuleName = "Kantan Connect - Sesión (TCP)";

    /// <summary>
    /// Crea (si no existen ya) las reglas de entrada necesarias. Requiere estar elevado;
    /// llamarlo sin permisos de Administrador no lanza excepción, pero <c>netsh</c>
    /// devuelve un código de error que se reporta en el resultado.
    /// </summary>
    public static FirewallSetupResult EnsureRulesExist()
    {
        var udpResult = EnsureRuleExists(UdpDiscoveryRuleName, "UDP", DiscoveryDefaults.UdpDiscoveryPort);
        var tcpResult = EnsureRuleExists(TcpSessionRuleName, "TCP", DiscoveryDefaults.TcpSessionPort);

        return new FirewallSetupResult(udpResult, tcpResult);
    }

    private static bool EnsureRuleExists(string ruleName, string protocol, int port)
    {
        if (RuleAlreadyExists(ruleName))
        {
            return true;
        }

        var arguments = "advfirewall firewall add rule "
            + $"name=\"{ruleName}\" "
            + "dir=in "
            + "action=allow "
            + $"protocol={protocol} "
            + $"localport={port} "
            + "profile=any";

        return RunNetsh(arguments);
    }

    /// <summary>
    /// Idempotencia: <c>netsh advfirewall firewall add rule</c> no rechaza nombres
    /// duplicados por sí solo (crearía una segunda regla idéntica cada vez que arranca
    /// la app). Por eso se consulta primero con <c>show rule</c>, cuyo código de salida
    /// es 0 solo si encontró al menos una regla con ese nombre exacto.
    /// </summary>
    private static bool RuleAlreadyExists(string ruleName) =>
        RunNetsh($"advfirewall firewall show rule name=\"{ruleName}\"");

    private static bool RunNetsh(string arguments)
    {
        var startInfo = new ProcessStartInfo("netsh", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return false;
        }

        process.WaitForExit();
        return process.ExitCode == 0;
    }
}

/// <summary>Resultado de intentar crear las reglas de firewall, para poder informar en la UI si algo falló.</summary>
public readonly record struct FirewallSetupResult(bool UdpRuleReady, bool TcpRuleReady)
{
    public bool AllRulesReady => UdpRuleReady && TcpRuleReady;
}
