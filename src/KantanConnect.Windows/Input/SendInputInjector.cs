using KantanConnect.Core.Abstractions;
using KantanConnect.Core.Models;
using KantanConnect.Windows.Interop;

namespace KantanConnect.Windows.Input;

/// <summary>
/// Reproduce eventos de ratón/teclado recibidos del Viewer usando <c>SendInput</c>
/// (P/Invoke a <c>user32.dll</c>, ver <see cref="NativeMethods"/>). Es la implementación
/// de Windows de <see cref="IInputInjector"/>; requiere que el proceso corra elevado
/// (ver <c>app.manifest</c>, Fase 0) para no chocar con UIPI al inyectar sobre ventanas
/// de otras aplicaciones.
/// </summary>
public sealed class SendInputInjector : IInputInjector
{
    /// <summary>
    /// Rango que <c>SendInput</c> espera para coordenadas absolutas: 0 = borde
    /// izquierdo/superior de la pantalla <b>primaria</b>, 65535 = borde derecho/inferior.
    /// Como no se pasa <c>MOUSEEVENTF_VIRTUALDESK</c>, este mapeo ignora monitores
    /// secundarios — coherente con que este proyecto (v1) solo soporta un monitor
    /// (ver `Capture.notas.md` en esta misma carpeta padre, y el plan, sección de riesgos).
    /// </summary>
    private const double AbsoluteCoordinateMax = 65535.0;

    public void Inject(InputEvent inputEvent)
    {
        switch (inputEvent.Kind)
        {
            case InputEventKind.MouseMove:
                SendMouseInput(inputEvent, NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE);
                break;

            case InputEventKind.MouseButtonDown:
                SendMouseInput(inputEvent, MapButtonToDownFlag(inputEvent.Button) | NativeMethods.MOUSEEVENTF_ABSOLUTE);
                break;

            case InputEventKind.MouseButtonUp:
                SendMouseInput(inputEvent, MapButtonToUpFlag(inputEvent.Button) | NativeMethods.MOUSEEVENTF_ABSOLUTE);
                break;

            case InputEventKind.MouseWheel:
                SendMouseWheelInput(inputEvent);
                break;

            case InputEventKind.KeyDown:
                SendKeyboardInput(inputEvent.VirtualKeyCode, keyUp: false);
                break;

            case InputEventKind.KeyUp:
                SendKeyboardInput(inputEvent.VirtualKeyCode, keyUp: true);
                break;
        }
    }

    private static void SendMouseInput(InputEvent inputEvent, uint flags)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = ToAbsoluteCoordinate(inputEvent.NormalizedX),
                    dy = ToAbsoluteCoordinate(inputEvent.NormalizedY),
                    dwFlags = flags,
                },
            },
        };

        SendSingleInput(input);
    }

    private static void SendMouseWheelInput(InputEvent inputEvent)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            u = new NativeMethods.InputUnion
            {
                mi = new NativeMethods.MOUSEINPUT
                {
                    dx = ToAbsoluteCoordinate(inputEvent.NormalizedX),
                    dy = ToAbsoluteCoordinate(inputEvent.NormalizedY),
                    // mouseData es uint en la API de Windows, pero el delta de rueda es
                    // conceptualmente con signo: unchecked() conserva el patrón de bits
                    // para que Windows lo reinterprete como signed de su lado.
                    mouseData = unchecked((uint)inputEvent.WheelDelta),
                    dwFlags = NativeMethods.MOUSEEVENTF_WHEEL | NativeMethods.MOUSEEVENTF_ABSOLUTE,
                },
            },
        };

        SendSingleInput(input);
    }

    private static void SendKeyboardInput(int virtualKeyCode, bool keyUp)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.InputUnion
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)virtualKeyCode,
                    dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0,
                },
            },
        };

        SendSingleInput(input);
    }

    private static void SendSingleInput(NativeMethods.INPUT input)
    {
        var inputs = new[] { input };
        var sent = NativeMethods.SendInput(1, inputs, System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());

        // SendInput devuelve 0 si NINGÚN evento se pudo inyectar (por ejemplo, si otra
        // aplicación tiene bloqueada la entrada con BlockInput). No se convierte en
        // excepción: perder un solo movimiento/click no debería tumbar toda la sesión,
        // el próximo evento simplemente se reintenta solo con el siguiente movimiento.
        _ = sent;
    }

    private static int ToAbsoluteCoordinate(double normalized) =>
        (int)Math.Round(Math.Clamp(normalized, 0.0, 1.0) * AbsoluteCoordinateMax);

    private static uint MapButtonToDownFlag(MouseButtonKind button) => button switch
    {
        MouseButtonKind.Left => NativeMethods.MOUSEEVENTF_LEFTDOWN,
        MouseButtonKind.Right => NativeMethods.MOUSEEVENTF_RIGHTDOWN,
        MouseButtonKind.Middle => NativeMethods.MOUSEEVENTF_MIDDLEDOWN,
        _ => 0,
    };

    private static uint MapButtonToUpFlag(MouseButtonKind button) => button switch
    {
        MouseButtonKind.Left => NativeMethods.MOUSEEVENTF_LEFTUP,
        MouseButtonKind.Right => NativeMethods.MOUSEEVENTF_RIGHTUP,
        MouseButtonKind.Middle => NativeMethods.MOUSEEVENTF_MIDDLEUP,
        _ => 0,
    };
}
