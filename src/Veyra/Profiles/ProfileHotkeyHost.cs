using System.Windows.Interop;
using Veyra.Core;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Profiles;

/// <summary>
/// Unsichtbares Nachrichtenfenster für das globale Tastenkürzel der Profilauswahl. Es gehört zu keiner Leiste,
/// damit das Kürzel auch dann funktioniert, wenn Leisten wegen eines Profilwechsels neu entstehen oder wegfallen.
/// </summary>
internal sealed class ProfileHotkeyHost : IDisposable
{
    private const int HotkeyId = 1; // IDs gelten je Fenster – dieses Fenster hat nur dieses eine Kürzel
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly HwndSource _source;
    private bool _registered;

    public ProfileHotkeyHost()
    {
        _source = new HwndSource(new HwndSourceParameters("Veyra.ProfileHotkey") { ParentWindow = HwndMessage, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    public event EventHandler? Pressed;

    /// <summary>Meldet das Kürzel (neu) an; null/leer = keins. False, wenn es schon von einem anderen Programm belegt ist.</summary>
    public bool Register(string? text)
    {
        Unregister();
        if (!Hotkey.TryParse(text, out var hotkey)) return true;
        _registered = RegisterHotKey(_source.Handle, HotkeyId, hotkey.NativeModifiers | MOD_NOREPEAT, hotkey.VirtualKey);
        if (_registered) Log.Info("Hotkey", $"{hotkey.DisplayText} → Profilauswahl");
        else Log.Warning("Hotkey", $"{hotkey.DisplayText} ist bereits belegt – Profilauswahl ohne Tastenkürzel");
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && (int)wParam.ToInt64() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }
}
