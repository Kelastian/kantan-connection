using System.Runtime.InteropServices;

namespace KantanConnect.Windows.Interop;

/// <summary>
/// Declaraciones P/Invoke de <c>user32.dll</c> para inyectar entrada real (Fase 6),
/// usadas por <see cref="Input.SendInputInjector"/>. Los tipos, tamaños y offsets de
/// estos structs fueron verificados empíricamente contra un binario compilado en esta
/// misma arquitectura (x64) — no son solo una transcripción de la documentación.
/// </summary>
internal static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public const uint INPUT_MOUSE = 0;
    public const uint INPUT_KEYBOARD = 1;

    public const uint MOUSEEVENTF_MOVE = 0x0001;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
    public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    public const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    public const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    public const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    public const uint MOUSEEVENTF_WHEEL = 0x0800;
    public const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    public const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>
    /// Representa el "union" de C (<c>INPUT</c> = <c>type</c> + una de
    /// <c>MOUSEINPUT</c>/<c>KEYBDINPUT</c>/<c>HARDWAREINPUT</c>). En C# se logra con dos
    /// structs: <see cref="InputUnion"/> superpone sus miembros en el mismo offset de
    /// memoria (<c>FieldOffset(0)</c> en los tres), y este struct externo los envuelve
    /// en secuencia normal. Tamaño total verificado en x64: 40 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        /// <summary>Coordenada X: absoluta (0..65535) si <see cref="MOUSEEVENTF_ABSOLUTE"/>, o delta relativo si no.</summary>
        public int dx;

        /// <summary>Coordenada Y: mismo criterio que <see cref="dx"/>.</summary>
        public int dy;

        /// <summary>
        /// Delta de la rueda cuando <c>dwFlags</c> incluye <see cref="MOUSEEVENTF_WHEEL"/>.
        /// Es <c>uint</c> en la API de Windows (aunque el valor es conceptualmente con
        /// signo); un delta negativo se pasa con <c>unchecked((uint)delta)</c> para que
        /// el patrón de bits viaje intacto y Windows lo reinterprete como signed de su lado.
        /// </summary>
        public uint mouseData;

        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
