using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using CustomTopBar.Interop;
using CustomTopBar.Monitors;
using static CustomTopBar.Interop.NativeMethods;

namespace CustomTopBar.Widgets.Clock;

/// <summary>
/// Monatskalender, der sich beim Klick auf Uhrzeit/Datum direkt unter der Uhr öffnet.
/// Schließt bei Klick außerhalb (Deaktivierung) oder Esc. Läuft nur, solange er offen ist.
/// </summary>
public partial class CalendarFlyout : Window
{
    private static CalendarFlyout? _current;
    private static DateTime _lastClosedUtc;

    private readonly CultureInfo _culture;
    private readonly DispatcherTimer _timer;
    private readonly int _anchorX;
    private readonly int _anchorY;

    private CalendarFlyout(CultureInfo culture, int anchorX, int anchorY, double scale)
    {
        InitializeComponent();
        _culture = culture;
        _anchorX = anchorX;
        _anchorY = anchorY;

        Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        MonthCalendar.FirstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;

        // Grobe Startposition in DIPs; die exakte Platzierung erfolgt nach dem Rendern in physischen Pixeln.
        Left = anchorX / scale - Width / 2;
        Top = anchorY / scale;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateHeader();
        UpdateHeader();
    }

    /// <summary>Öffnet bzw. schließt den Kalender unterhalb des angegebenen Elements.</summary>
    public static void Toggle(FrameworkElement anchor, CultureInfo culture)
    {
        if (_current is not null)
        {
            _current.Close();
            return;
        }

        // Der Klick auf die Uhr deaktiviert zuerst den offenen Kalender – dieser Klick soll ihn nicht sofort wieder öffnen.
        if ((DateTime.UtcNow - _lastClosedUtc).TotalMilliseconds < 300) return;

        var barWindow = GetWindow(anchor);
        if (barWindow is null) return;
        var barHandle = new WindowInteropHelper(barWindow).Handle;
        GetWindowRect(barHandle, out var barRect);
        var monitor = MonitorService.Query(MonitorFromWindow(barHandle, MONITOR_DEFAULTTONEAREST));
        double scale = monitor?.Scale ?? 1.0;

        var center = anchor.PointToScreen(new Point(anchor.ActualWidth / 2, 0)); // physische Pixel
        var flyout = new CalendarFlyout(culture, (int)center.X, barRect.Bottom + (int)(8 * scale), scale);
        _current = flyout;
        flyout.Show();
        flyout.Activate();
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
        var handle = new WindowInteropHelper(this).Handle;
        GetWindowRect(handle, out var rect);
        int x = _anchorX - rect.Width / 2;

        // Innerhalb des Monitors halten.
        var monitor = MonitorService.Query(MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST));
        if (monitor is not null)
            x = Math.Clamp(x, monitor.WorkArea.Left + 8, Math.Max(monitor.WorkArea.Left + 8, monitor.WorkArea.Right - rect.Width - 8));

        SetWindowPos(handle, IntPtr.Zero, x, _anchorY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        _timer.Start();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (IsLoaded) Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        if (_current == this) _current = null;
        _lastClosedUtc = DateTime.UtcNow;
        base.OnClosed(e);
    }

    private void UpdateHeader()
    {
        var now = DateTime.Now;
        TimeText.Text = now.ToString("T", _culture);
        DateText.Text = $"{now.ToString("D", _culture)}  ·  KW {ISOWeek.GetWeekOfYear(now)}";
    }

    private void OnTodayClick(object sender, RoutedEventArgs e)
    {
        MonthCalendar.DisplayDate = DateTime.Today;
        MonthCalendar.DisplayMode = System.Windows.Controls.CalendarMode.Month;
        MonthCalendar.SelectedDate = DateTime.Today;
    }

    private void OnOpenSystemSettings(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true });
        }
        catch
        {
            // Einstellungen-App nicht verfügbar – ignorieren.
        }
        Close();
    }
}
