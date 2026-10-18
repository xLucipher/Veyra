using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Veyra.Core;
using Veyra.Monitors;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Widgets;

/// <summary>
/// Basis aller Widget-Popups (Kalender, Netzwerk, Lautstärke, Timer, Stoppuhr): öffnet sich direkt unter dem
/// Widget – bzw. darüber, wenn die Leiste unten am Bildschirm liegt –, bleibt im Monitor, schließt bei Klick
/// außerhalb (Deaktivierung) oder Esc. Es ist immer höchstens ein Popup offen; ein Klick auf dasselbe Widget
/// schließt es wieder. Popups laufen nur, solange sie offen sind.
/// </summary>
public abstract class FlyoutWindow : Window
{
    private static FlyoutWindow? _current;
    private static FrameworkElement? _lastClosedAnchor;
    private static DateTime _lastClosedUtc;

    private FrameworkElement? _anchor;
    private int _anchorX;
    private int _anchorY;
    private int _barTop;
    private int _barBottom;
    private bool _placed;
    private bool _closing;

    protected FlyoutWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.Height;
        // Wächst der Inhalt (neuer Timer, weitere Runde), Position neu berechnen – ein Popup über der Leiste bleibt unten bündig.
        SizeChanged += (_, _) =>
        {
            if (_placed) Place();
        };
    }

    /// <summary>Öffnet ein Popup unter <paramref name="anchor"/>; ist bereits eines offen, wird es geschlossen.</summary>
    /// <returns>Das geöffnete Popup oder null, wenn nur geschlossen wurde.</returns>
    public static FlyoutWindow? Toggle(FrameworkElement anchor, Func<FlyoutWindow> create)
    {
        if (_current is not null)
        {
            bool same = _current._anchor == anchor;
            _current.Close();
            if (same) return null;
        }

        // Der Klick auf das Widget deaktiviert zuerst das offene Popup – dieser Klick soll es nicht sofort wieder öffnen.
        if (_lastClosedAnchor == anchor && (DateTime.UtcNow - _lastClosedUtc).TotalMilliseconds < 300) return null;

        var barWindow = GetWindow(anchor);
        if (barWindow is null) return null;
        var barHandle = new WindowInteropHelper(barWindow).Handle;
        GetWindowRect(barHandle, out var barRect);
        var monitor = MonitorService.Query(MonitorFromWindow(barHandle, MONITOR_DEFAULTTONEAREST));
        double scale = monitor?.Scale ?? 1.0;

        var flyout = create();
        var center = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0)); // physische Pixel
        flyout._anchor = anchor;
        flyout._anchorX = (int)center.X;
        flyout._anchorY = barRect.Bottom + (int)(8 * scale);
        flyout._barTop = barRect.Top;
        flyout._barBottom = barRect.Bottom;
        // Grobe Startposition in DIPs; die exakte Platzierung erfolgt nach dem Rendern in physischen Pixeln.
        flyout.Left = center.X / scale - flyout.Width / 2;
        flyout.Top = flyout._anchorY / scale;

        _current = flyout;
        flyout.Show();
        flyout.Activate();
        return flyout;
    }

    /// <summary>Schließt das Popup eines Widgets, z. B. wenn das Widget entfernt wird.</summary>
    public static void CloseFor(FrameworkElement anchor)
    {
        if (_current?._anchor == anchor) _current.Close();
    }

    /// <summary>Öffnet eine ms-settings:-Seite bzw. ein Systemprogramm und schließt das Popup.</summary>
    public void OpenAndClose(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning("Flyout", $"'{target}' konnte nicht geöffnet werden: {ex.Message}");
        }
        Close();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        // Nicht in Alt+Tab, abgerundete Ecken wie Windows-11-Flyouts.
        long exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(exStyle));
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Place();
        _placed = true;
    }

    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        GetWindowRect(handle, out var rect);
        int x = _anchorX - rect.Width / 2;

        // Innerhalb des Monitors halten.
        int y = _anchorY;
        var monitor = MonitorService.Query(MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST));
        if (monitor is not null)
        {
            x = Math.Clamp(x, monitor.WorkArea.Left + 8, Math.Max(monitor.WorkArea.Left + 8, monitor.WorkArea.Right - rect.Width - 8));
            // Dynamische Leiste weit unten: Popup oberhalb öffnen, damit es nicht aus dem Bild ragt.
            if (y + rect.Height > monitor.WorkArea.Bottom)
                y = Math.Max(monitor.WorkArea.Top, _barTop - rect.Height - (_anchorY - _barBottom));
        }

        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        _closing = !e.Cancel;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // Beim Schließen meldet Windows oft noch die Deaktivierung – dann nicht ein zweites Mal schließen.
        if (IsLoaded && !_closing) Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_current == this) _current = null;
        _lastClosedAnchor = _anchor;
        _lastClosedUtc = DateTime.UtcNow;
        base.OnClosed(e);
    }
}
