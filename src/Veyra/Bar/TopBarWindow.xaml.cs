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

    private const double ResizeBorder = 8;   // Greifbereich an den Rändern (geräteunabhängige Pixel)
    private const double MoveThreshold = 4;  // ab dieser Bewegung wird aus einem Klick ein Verschieben

    private HwndSource? _source;
    private bool _sessionNotificationRegistered;
    private bool _shellHookRegistered;

    // Dynamischer Modus: Verschieben/Größenänderung mit der Maus.
    private Point? _movePressPoint;
    private ResizeEdge _resizeEdge;
    private POINT _resizeStartCursor;
    private RECT _resizeStartRect;

    [Flags]
    private enum ResizeEdge
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
    }

    /// <param name="dynamic">Dynamischer Modus: transparentes, frei bewegliches Fenster. Fixiert: deckendes Fenster (schneller).</param>
    public TopBarWindow(TopBarViewModel viewModel, bool dynamic)
    {
        InitializeComponent();
        IsDynamic = dynamic;
        // Transparenz geht nur vor dem ersten Anzeigen – ein Moduswechsel erzeugt deshalb ein neues Fenster.
        AllowsTransparency = dynamic;
        DataContext = viewModel;
    }

    public IntPtr Handle { get; private set; }

    public bool IsDynamic { get; }

    /// <summary>Dynamischer Modus: Verschieben und Größenänderung mit der Maus sperren.</summary>
    public bool IsLocked { get; set; }

    /// <summary>Dynamischer Modus: Fensterebene. Im fixierten Modus gilt <see cref="Window.Topmost"/>.</summary>
    public Veyra.Configuration.WindowLayer Layer { get; set; } = Veyra.Configuration.WindowLayer.Topmost;

    /// <summary>Der Benutzer hat das dynamische Fenster verschoben oder seine Größe geändert (physische Pixel).</summary>
    public event EventHandler<RECT>? UserBoundsChanged;

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

    /// <summary>
    /// True, wenn eine Windows-Taskleiste in der Z-Reihenfolge über der Leiste liegt. Sie holt sich z. B. beim
    /// Anklicken selbst nach vorne – erst nach dem Vordergrundwechsel, auf den die Leiste bereits reagiert hat.
    /// </summary>
    public bool IsBelowTaskbar()
    {
        if (Handle == IntPtr.Zero) return false;
        // Nur die Fenster oberhalb der Leiste durchgehen – bei einer Topmost-Leiste sind das wenige.
        for (IntPtr hwnd = NativeMethods.GetWindow(Handle, GW_HWNDPREV); hwnd != IntPtr.Zero; hwnd = NativeMethods.GetWindow(hwnd, GW_HWNDPREV))
        {
            if (!IsWindowVisible(hwnd)) continue;
            if (Fullscreen.WindowInspector.GetClassName(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return true;
        }
        return false;
    }

    /// <summary>Setzt die Z-Ebene laut <see cref="Layer"/> (dynamischer Modus).</summary>
    public void ApplyLayer()
    {
        if (Handle == IntPtr.Zero || !IsDynamic) return;
        Topmost = Layer == Veyra.Configuration.WindowLayer.Topmost;
        var insertAfter = Layer switch
        {
            Veyra.Configuration.WindowLayer.Topmost => HWND_TOPMOST,
            Veyra.Configuration.WindowLayer.Desktop => HWND_BOTTOM,
            _ => HWND_NOTOPMOST,
        };
        SetWindowPos(Handle, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    // ---- Kontextmenü -----------------------------------------------------------------------

    private IntPtr _menuHandle;
    private IntPtr _foregroundBeforeMenu;

    /// <summary>
    /// Die Leiste wird nie aktiviert (WS_EX_NOACTIVATE). Ohne aktives Fenster sieht das Menü Klicks in andere
    /// Fenster nicht und bliebe offen. Deshalb holt es sich beim Öffnen den Vordergrund: Dann schließt jeder
    /// Klick außerhalb (und Esc) das Menü.
    /// </summary>
    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (PresentationSource.FromVisual((System.Windows.Controls.ContextMenu)sender) is not HwndSource source) return;
        _menuHandle = source.Handle;
        _foregroundBeforeMenu = GetForegroundWindow();
        KeepMenuAboveBar();
        SetForegroundWindow(_menuHandle);
    }

    private bool _raisingMenu;

    /// <summary>
    /// Das Menü ist ein eigenes Popup-Fenster. Die Leiste holt sich bei jedem Vordergrundwechsel (auch dem durch
    /// das Menü selbst) wieder nach oben und würde es sonst verdecken – deshalb liegt das offene Menü immer
    /// ganz oben in der Topmost-Ebene, auch bei Leisten der Ebene "Normal" oder "Im Hintergrund".
    /// </summary>
    private void KeepMenuAboveBar()
    {
        if (_menuHandle == IntPtr.Zero || _raisingMenu) return;
        _raisingMenu = true;
        try
        {
            SetWindowPos(_menuHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        finally
        {
            _raisingMenu = false;
        }
    }

    /// <summary>
    /// Menü ohne Folgefenster geschlossen (Esc, Klick auf die Leiste) → Fokus an das vorherige Fenster zurückgeben.
    /// Hat ein Klick ein anderes Fenster aktiviert oder ein Menüpunkt die Einstellungen geöffnet, bleibt es dabei.
    /// </summary>
    private void OnContextMenuClosed(object sender, RoutedEventArgs e)
    {
        IntPtr menu = _menuHandle, previous = _foregroundBeforeMenu;
        _menuHandle = IntPtr.Zero;
        _foregroundBeforeMenu = IntPtr.Zero;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            var foreground = GetForegroundWindow();
            bool stillOurs = foreground == IntPtr.Zero || foreground == menu || foreground == Handle;
            if (stillOurs && previous != IntPtr.Zero && previous != menu && IsWindowVisible(previous))
                SetForegroundWindow(previous);
        });
    }

    // ---- Dynamischer Modus: Verschieben und Größenänderung ---------------------------------

    protected override void OnPreviewMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!IsDynamic || IsLocked) return;

        if (_resizeEdge != ResizeEdge.None)
        {
            ContinueResize();
            e.Handled = true;
            return;
        }

        if (_movePressPoint is { } press && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
        {
            var now = e.GetPosition(this);
            if (Math.Abs(now.X - press.X) < MoveThreshold && Math.Abs(now.Y - press.Y) < MoveThreshold) return;
            _movePressPoint = null;
            try
            {
                DragMove(); // läuft, bis die Maustaste losgelassen wird; der Klick (z. B. Kalender) entfällt dann
            }
            catch (InvalidOperationException)
            {
                return;
            }
            RaiseUserBoundsChanged();
            e.Handled = true;
            return;
        }

        if (e.LeftButton == System.Windows.Input.MouseButtonState.Released)
            Cursor = CursorFor(EdgeAt(e.GetPosition(this)));
    }

    protected override void OnPreviewMouseLeftButtonDown(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (!IsDynamic || IsLocked) return;

        var edge = EdgeAt(e.GetPosition(this));
        if (edge != ResizeEdge.None)
        {
            _resizeEdge = edge;
            GetCursorPos(out _resizeStartCursor);
            GetWindowRect(Handle, out _resizeStartRect);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        // Ziehbare Widgets verschiebt das Layout-Panel selbst; überall sonst (oder mit Strg) wird das Fenster verschoben.
        if (BarLayoutPanel.IsWindowMoveModifier || !WidgetPanel.IsDraggableWidgetAt(e.GetPosition(WidgetPanel)))
            _movePressPoint = e.GetPosition(this);
    }

    protected override void OnPreviewMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        _movePressPoint = null;
        if (_resizeEdge == ResizeEdge.None) return;
        _resizeEdge = ResizeEdge.None;
        ReleaseMouseCapture();
        RaiseUserBoundsChanged();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(System.Windows.Input.MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_resizeEdge == ResizeEdge.None) return;
        _resizeEdge = ResizeEdge.None;
        RaiseUserBoundsChanged();
    }

    private void ContinueResize()
    {
        GetCursorPos(out var cursor);
        int dx = cursor.X - _resizeStartCursor.X;
        int dy = cursor.Y - _resizeStartCursor.Y;
        double scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
        int minW = (int)(40 * scale), minH = (int)(16 * scale);
        var r = _resizeStartRect;

        if (_resizeEdge.HasFlag(ResizeEdge.Left)) r.Left = Math.Min(r.Left + dx, r.Right - minW);
        if (_resizeEdge.HasFlag(ResizeEdge.Right)) r.Right = Math.Max(r.Right + dx, r.Left + minW);
        if (_resizeEdge.HasFlag(ResizeEdge.Top)) r.Top = Math.Min(r.Top + dy, r.Bottom - minH);
        if (_resizeEdge.HasFlag(ResizeEdge.Bottom)) r.Bottom = Math.Max(r.Bottom + dy, r.Top + minH);

        SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER);
    }

    private ResizeEdge EdgeAt(Point p)
    {
        var edge = ResizeEdge.None;
        if (p.X <= ResizeBorder) edge |= ResizeEdge.Left;
        else if (p.X >= ActualWidth - ResizeBorder) edge |= ResizeEdge.Right;
        if (p.Y <= ResizeBorder) edge |= ResizeEdge.Top;
        else if (p.Y >= ActualHeight - ResizeBorder) edge |= ResizeEdge.Bottom;
        return edge;
    }

    private static System.Windows.Input.Cursor? CursorFor(ResizeEdge edge) => edge switch
    {
        ResizeEdge.Left or ResizeEdge.Right => System.Windows.Input.Cursors.SizeWE,
        ResizeEdge.Top or ResizeEdge.Bottom => System.Windows.Input.Cursors.SizeNS,
        ResizeEdge.Left | ResizeEdge.Top or ResizeEdge.Right | ResizeEdge.Bottom => System.Windows.Input.Cursors.SizeNWSE,
        ResizeEdge.Right | ResizeEdge.Top or ResizeEdge.Left | ResizeEdge.Bottom => System.Windows.Input.Cursors.SizeNESW,
        _ => null,
    };

    private void RaiseUserBoundsChanged()
    {
        if (Handle == IntPtr.Zero) return;
        GetWindowRect(Handle, out var rect);
        UserBoundsChanged?.Invoke(this, rect);
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
                    KeepMenuAboveBar(); // Leiste nach oben geholt → offenes Menü bleibt darüber
                    break;

                case WM_WINDOWPOSCHANGING when IsDynamic && Layer == Veyra.Configuration.WindowLayer.Desktop:
                    // Ebene "Im Hintergrund": jede Z-Order-Änderung wird nach ganz unten umgelenkt.
                    var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<WINDOWPOS>(lParam);
                    pos.hwndInsertAfter = HWND_BOTTOM;
                    pos.flags &= ~SWP_NOZORDER;
                    System.Runtime.InteropServices.Marshal.StructureToPtr(pos, lParam, false);
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
