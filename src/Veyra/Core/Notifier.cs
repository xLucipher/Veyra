using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Veyra.Configuration;

namespace Veyra.Core;

/// <summary>
/// Windows-Benachrichtigungen ohne Zusatzpaket: Ein Infoballon (Shell_NotifyIcon, NIF_INFO) erscheint unter
/// Windows 10/11 als normale Benachrichtigung samt Eintrag in der Mitteilungszentrale und respektiert „Nicht stören“.
/// Das dafür nötige Infobereich-Symbol existiert nur, solange eine Benachrichtigung angezeigt wird.
/// Signaltöne kommen aus dem Windows-Soundschema (PlaySound mit Ereignisnamen).
/// </summary>
public static class Notifier
{
    private const int CallbackMessage = 0x8000 + 0x56; // WM_APP + x
    private const uint IconId = 1;
    private static HwndSource? _window;
    private static IntPtr _smallIcon, _largeIcon;
    private static bool _iconAdded;
    private static DispatcherTimer? _removeTimer;

    /// <summary>Zeigt eine Windows-Benachrichtigung „Veyra – <paramref name="title"/>“.</summary>
    /// <param name="silent">Ohne Benachrichtigungston (z. B. weil gleich ein eigener Signalton folgt).</param>
    public static void Show(string title, string text, bool silent)
    {
        try
        {
            EnsureWindow();
            var data = CreateData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_INFO;
            data.szInfoTitle = Truncate(title, 63);
            data.szInfo = Truncate(text, 255);
            data.dwInfoFlags = NIIF_USER | NIIF_LARGE_ICON | NIIF_RESPECT_QUIET_TIME | (silent ? NIIF_NOSOUND : 0);
            data.hBalloonIcon = _largeIcon;

            if (!_iconAdded)
            {
                if (!Shell_NotifyIconW(NIM_ADD, ref data))
                {
                    Log.Warning("Notify", "Benachrichtigung konnte nicht angezeigt werden (Shell_NotifyIcon)");
                    return;
                }
                _iconAdded = true;
                data.uVersion = NOTIFYICON_VERSION_4;
                Shell_NotifyIconW(NIM_SETVERSION, ref data);
            }
            else
            {
                Shell_NotifyIconW(NIM_MODIFY, ref data);
            }

            // Spätestens nach einer Minute aufräumen, falls Windows das Ausblenden nicht meldet.
            _removeTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _removeTimer.Tick -= OnRemoveTimer;
            _removeTimer.Tick += OnRemoveTimer;
            _removeTimer.Stop();
            _removeTimer.Start();
            Log.Info("Notify", $"Benachrichtigung: {title} – {text}");
        }
        catch (Exception ex)
        {
            Log.Error("Notify", "Benachrichtigung fehlgeschlagen", ex);
        }
    }

    /// <summary>Spielt einen Signalton aus dem Windows-Soundschema (asynchron).</summary>
    public static void Play(TimerSound sound)
    {
        string? alias = sound switch
        {
            TimerSound.Alarm => "Notification.Looping.Alarm",
            TimerSound.Notification => "Notification.Default",
            TimerSound.Reminder => "Notification.Reminder",
            _ => null,
        };
        if (alias is null) return;
        try
        {
            // Ohne SND_NODEFAULT: Fehlt der Klang im Schema, ertönt der Standardton.
            PlaySoundW(alias, IntPtr.Zero, SND_ALIAS | SND_ASYNC);
        }
        catch (Exception ex)
        {
            Log.Warning("Notify", $"Signalton konnte nicht abgespielt werden: {ex.Message}");
        }
    }

    /// <summary>Beim Beenden: Symbol entfernen, damit kein verwaistes Symbol im Infobereich bleibt.</summary>
    public static void Shutdown()
    {
        RemoveIcon();
        _window?.Dispose();
        _window = null;
        if (_smallIcon != IntPtr.Zero) DestroyIcon(_smallIcon);
        if (_largeIcon != IntPtr.Zero) DestroyIcon(_largeIcon);
        _smallIcon = _largeIcon = IntPtr.Zero;
    }

    private static void OnRemoveTimer(object? sender, EventArgs e) => RemoveIcon();

    private static void RemoveIcon()
    {
        _removeTimer?.Stop();
        if (!_iconAdded) return;
        var data = CreateData();
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _iconAdded = false;
    }

    private static void EnsureWindow()
    {
        if (_window is not null) return;
        // Nachrichtenfenster (HWND_MESSAGE): unsichtbar, nur für die Rückmeldungen des Infobereichs.
        _window = new HwndSource(new HwndSourceParameters("VeyraNotifier") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _window.AddHook(WndProc);
        if (Environment.ProcessPath is { } exe) ExtractIconExW(exe, 0, out _largeIcon, out _smallIcon, 1);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != CallbackMessage) return IntPtr.Zero;
        int notification = (int)(lParam.ToInt64() & 0xFFFF);
        // Ausgeblendet, abgelaufen oder angeklickt: Symbol wird nicht mehr gebraucht (der Eintrag in der Mitteilungszentrale bleibt).
        if (notification is NIN_BALLOONHIDE or NIN_BALLOONTIMEOUT or NIN_BALLOONUSERCLICK) RemoveIcon();
        handled = true;
        return IntPtr.Zero;
    }

    private static NOTIFYICONDATAW CreateData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _window?.Handle ?? IntPtr.Zero,
        uID = IconId,
        uCallbackMessage = CallbackMessage,
        hIcon = _smallIcon,
        szTip = "Veyra",
        szInfo = "",
        szInfoTitle = "",
    };

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    // ---- Interop ----------------------------------------------------------------------------

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4, NIF_INFO = 0x10;
    private const uint NIIF_USER = 0x4, NIIF_NOSOUND = 0x10, NIIF_LARGE_ICON = 0x20, NIIF_RESPECT_QUIET_TIME = 0x80;
    private const uint NOTIFYICON_VERSION_4 = 4;
    private const int NIN_BALLOONHIDE = 0x403, NIN_BALLOONTIMEOUT = 0x404, NIN_BALLOONUSERCLICK = 0x405;
    private const uint SND_ASYNC = 0x1, SND_ALIAS = 0x10000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion; // Union mit uTimeout
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(uint message, ref NOTIFYICONDATAW data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySoundW(string sound, IntPtr module, uint flags);
}
