using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Veyra.Core;
using Veyra.Interop;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Bar;

/// <summary>
/// Das eigentliche Leistenfenster. Kümmert sich ausschließlich um Win32-Belange des Fensters
/// (Stile, Position, Nachrichten) und meldet Systemereignisse an den <see cref="BarManager"/>.
/// </summary>
public partial class TopBarWindow : Window
{
    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    private static readonly uint ShellHookMessage = RegisterWindowMessage("SHELLHOOK");

    private HwndSource? _source;
    private bool _sessionNotificationRegistered;
    private bool _shellHookRegistered;

    public TopBarWindow(TopBarViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public IntPtr Handle { get; private set; }

    internal AppBarRegistration? AppBar { get; private set; }

    /// <summary>Auflösung, Monitoranordnung, DPI geändert oder System aus dem Standby zurück.</summary>
    public event EventHandler? DisplayConfigurationChanged;

    /// <summary>explorer.exe wurde (neu) gestartet.</summary>
    public event EventHandler? ShellRestarted;

    public event EventHandler? SessionUnlocked;

    public event EventHandler? SystemTimeChanged;

    /// <summary>Fensteraktivierung, Vollbild-Aktivierung oder geschlossenes Fenster laut Shell-Hook.</summary>
    public event EventHandler? ShellWindowActivity;

    /// <summary>ABN_FULLSCREENAPP: Explorer meldet Öffnen (true) oder Schließen (false) einer Vollbildanwendung.</summary>
    public event EventHandler<bool>? FullscreenAppNotification;

    public event EventHandler? AppBarPositionChanged;

    protected override void OnSourceInitialized(EventArgs e)
    {
        Handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(Handle);
        _source?.AddHook(WndProc);

        ApplyWindowStyles();
        ConfigureDwm();

        _sessionNotificationRegistered = WTSRegisterSessionNotification(Handle, NOTIFY_FOR_THIS_SESSION);
        _shellHookRegistered = RegisterShellHookWindow(Handle);

        AppBar = new AppBarRegistration(Handle);
        AppBar.Register();

        // Erst jetzt den Basisaufruf: Abonnenten von SourceInitialized finden Handle und AppBar fertig vor.
        base.OnSourceInitialized(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        AppBar?.Unregister();
        if (_shellHookRegistered) DeregisterShellHookWindow(Handle);
        if (_sessionNotificationRegistered) WTSUnRegisterSessionNotification(Handle);
        _source?.RemoveHook(WndProc);
        base.OnClosed(e);
    }

    /// <summary>Setzt Position und Größe in physischen Pixeln (ohne Aktivierung, ohne Z-Order-Änderung).</summary>
    public void ApplyBounds(RECT rect)
    {
        if (Handle == IntPtr.Zero) return;
        SetWindowPos(Handle, IntPtr.Zero, rect.Left, rect.Top, rect.Width, rect.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    /// <summary>Holt die Leiste an die Spitze der Topmost-Ebene zurück, ohne den Fokus zu ändern.</summary>
    public void EnsureTopmost()
    {
        if (Handle == IntPtr.Zero) return;
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private void ApplyWindowStyles()
    {
        // Tool-Fenster: weder in Alt+Tab noch in der Taskleiste. NoActivate: Klicks stehlen keinen Fokus.
        long exStyle = GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64();
        exStyle = (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(exStyle));
        SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    private void ConfigureDwm()
    {
        // Keine abgerundeten Ecken im FullWidth-Modus und nicht von "Desktop anzeigen"-Peek ausblenden.
        int cornerPreference = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));
        int excludeFromPeek = 1;
        DwmSetWindowAttribute(Handle, DWMWA_EXCLUDED_FROM_PEEK, ref excludeFromPeek, sizeof(int));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        try
        {
            switch (msg)
            {
                case WM_MOUSEACTIVATE:
                    handled = true;
                    return new IntPtr(MA_NOACTIVATE);

                case WM_DISPLAYCHANGE:
                    DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);
                    break;

                case WM_DPICHANGED:
                    // WPF verarbeitet die DPI-Änderung selbst; danach positionieren wir neu.
                    Dispatcher.BeginInvoke(DispatcherPriority.Background,
                        () => DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty));
                    break;

                case WM_TIMECHANGE:
                    SystemTimeChanged?.Invoke(this, EventArgs.Empty);
                    break;

                case WM_WTSSESSION_CHANGE:
                    if ((int)wParam.ToInt64() == WTS_SESSION_UNLOCK) SessionUnlocked?.Invoke(this, EventArgs.Empty);
                    break;

                case WM_POWERBROADCAST:
                    int power = (int)wParam.ToInt64();
                    if (power is PBT_APMRESUMEAUTOMATIC or PBT_APMRESUMESUSPEND)
                        DisplayConfigurationChanged?.Invoke(this, EventArgs.Empty);
                    break;

                case WM_ACTIVATE:
                    AppBar?.NotifyActivate();
                    break;

                case WM_WINDOWPOSCHANGED:
                    AppBar?.NotifyWindowPosChanged();
                    break;
            }

            uint message = (uint)msg;
            if (message == TaskbarCreatedMessage)
            {
                ShellRestarted?.Invoke(this, EventArgs.Empty);
            }
            else if (message == ShellHookMessage)
            {
                int code = (int)wParam.ToInt64() & ~HSHELL_HIGHBIT;
                if (code is HSHELL_WINDOWACTIVATED or HSHELL_WINDOWDESTROYED or HSHELL_MONITORCHANGED)
                    ShellWindowActivity?.Invoke(this, EventArgs.Empty);
            }
            else if (AppBar is not null && message == AppBar.CallbackMessage)
            {
                switch ((int)wParam.ToInt64())
                {
                    case ABN_POSCHANGED:
                        AppBarPositionChanged?.Invoke(this, EventArgs.Empty);
                        break;
                    case ABN_FULLSCREENAPP:
                        FullscreenAppNotification?.Invoke(this, lParam != IntPtr.Zero);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Window", $"Fehler bei Nachricht 0x{msg:X}", ex);
        }

        return IntPtr.Zero;
    }
}
