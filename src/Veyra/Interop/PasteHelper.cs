using System.Runtime.InteropServices;
using System.Windows.Threading;
using Veyra.Core;

namespace Veyra.Interop;

/// <summary>
/// Fügt die Zwischenablage in ein Fenster ein: Fokus zurück an das Fenster, dann Strg + V (SendInput).
/// Für den Zwischenablage-Verlauf, der dafür kurz selbst den Fokus hatte.
/// </summary>
internal static class PasteHelper
{
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_MENU = 0x12;
    private const ushort VK_LWIN = 0x5B;
    private const ushort VK_RWIN = 0x5C;
    private const ushort VK_V = 0x56;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    /// <summary>Gibt <paramref name="target"/> den Fokus zurück und fügt nach kurzer Pause ein.</summary>
    public static void PasteInto(IntPtr target)
    {
        if (target == IntPtr.Zero || !NativeMethods.IsWindow(target)) return;
        NativeMethods.SetForegroundWindow(target);
        // Das Zielfenster braucht einen Moment, bis es den Fokus wirklich hat (sonst landet Strg + V im Nichts).
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (NativeMethods.GetForegroundWindow() != target)
            {
                Log.Debug("Clipboard", "Einfügen übersprungen – Zielfenster hat den Fokus nicht bekommen");
                return;
            }
            SendCtrlV();
        };
        timer.Start();
    }

    private static void SendCtrlV()
    {
        var inputs = new List<INPUT>();
        // Noch gedrückte Umschalt-, Alt- oder Windows-Tasten (z. B. vom Tastenkürzel) würden aus Strg + V etwas anderes machen.
        foreach (var key in new[] { VK_SHIFT, VK_MENU, VK_LWIN, VK_RWIN })
            if ((GetAsyncKeyState(key) & 0x8000) != 0) inputs.Add(Key(key, up: true));
        inputs.Add(Key(VK_CONTROL, up: false));
        inputs.Add(Key(VK_V, up: false));
        inputs.Add(Key(VK_V, up: true));
        inputs.Add(Key(VK_CONTROL, up: true));
        var array = inputs.ToArray();
        if (SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>()) != array.Length)
            Log.Warning("Clipboard", $"Einfügen fehlgeschlagen (SendInput, Fehler {Marshal.GetLastWin32Error()})");
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    // Größe wie die größte Variante (MOUSEINPUT), sonst lehnt SendInput die Struktur ab.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
