using CustomTopBar.Core;

namespace CustomTopBar.Interop;

/// <summary>
/// Kapselt einen Out-of-Context-WinEvent-Hook. Der Callback läuft auf dem Thread, der den Hook
/// erstellt hat (hier: UI-Thread), über dessen Nachrichtenschleife – kein DLL-Injection, kein Polling.
/// </summary>
internal sealed class WinEventHook : IDisposable
{
    // Der Delegate muss referenziert bleiben, sonst sammelt der GC ihn ein und Windows ruft ins Leere.
    private readonly NativeMethods.WinEventDelegate _callback;
    private IntPtr _handle;

    public WinEventHook(uint eventMin, uint eventMax, Action<uint, IntPtr, int, int> handler)
    {
        _callback = (_, eventType, hwnd, idObject, idChild, _, _) =>
        {
            try
            {
                handler(eventType, hwnd, idObject, idChild);
            }
            catch (Exception ex)
            {
                Log.Error("WinEvent", "Fehler im WinEvent-Callback", ex);
            }
        };

        _handle = NativeMethods.SetWinEventHook(eventMin, eventMax, IntPtr.Zero, _callback, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT);

        if (_handle == IntPtr.Zero)
            Log.Error("WinEvent", $"SetWinEventHook(0x{eventMin:X}-0x{eventMax:X}) fehlgeschlagen");
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_handle);
            _handle = IntPtr.Zero;
        }
    }
}
