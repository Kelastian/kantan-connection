using System.Diagnostics;
using System.Security.Principal;

namespace KantanConnect.Windows.Platform;

/// <summary>
/// Detecta si el proceso actual corre elevado (como Administrador) y, si no, lo
/// vuelve a lanzar elevado. Existe porque <c>app.manifest</c> (Fase 0) ya declara
/// <c>requireAdministrator</c> — en teoría Windows nunca debería dejar arrancar el
/// `.exe` sin elevar — pero esta clase es la red de seguridad para los casos donde
/// igual hace falta relanzar (por ejemplo, si alguien ejecuta el `.dll` con `dotnet run`
/// en desarrollo, donde el manifest del host de `dotnet.exe` manda, no el nuestro).
/// </summary>
public static class AdminHelper
{
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Relanza el ejecutable actual pidiendo elevación (dispara el diálogo de UAC).
    /// Devuelve <c>true</c> si el relanzamiento se inició correctamente (el llamador
    /// debe cerrar la instancia actual); <c>false</c> si el usuario canceló el UAC o
    /// el relanzamiento falló por otro motivo (el llamador debe seguir sin elevar, o
    /// mostrar un error, según corresponda).
    /// </summary>
    public static bool TryRelaunchElevated()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath))
        {
            return false;
        }

        var startInfo = new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // El usuario canceló el diálogo de UAC (o algo similar impidió elevar).
            // No es un error del programa: es una decisión válida del usuario.
            return false;
        }
    }
}
