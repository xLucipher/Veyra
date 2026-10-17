using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Threading;
using Veyra.Interop;
using Veyra.Monitors;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Widgets.Clock;

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
    private readonly int _barTop;
    private readonly int _barBottom;

    private CalendarFlyout(CultureInfo culture, int anchorX, int anchorY, int barTop, int barBottom, double scale)
    {
        InitializeComponent();
        _culture = culture;
        _anchorX = anchorX;
        _anchorY = anchorY;
        _barTop = barTop;
        _barBottom = barBottom;

        Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        MonthCalendar.FirstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;

        // Grobe Startposition in DIPs; die exakte Platzierung erfolgt nach dem Rendern in physischen Pixeln.
        Left = anchorX / scale - Width / 2;
        Top = anchorY / scale;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => UpdateHeader();
        UpdateHeader();

        MonthCalendar.PreviewMouseLeftButtonUp += OnCalendarMouseUp;
    }

    private void OnCalendarMouseUp(object sender, MouseButtonEventArgs e)
    {
        var button = FindAncestor<System.Windows.Controls.Primitives.CalendarDayButton>(e.OriginalSource as DependencyObject);
        if (button is null || !button.IsToday || button.DataContext is not DateTime day || day.Date != DateTime.Today) return;

        // Nur innerhalb des runden Markers, nicht in den Ecken der Schaltfläche.
        var p = e.GetPosition(button);
        double radius = Math.Min(button.ActualWidth, button.ActualHeight) / 2;
        double dx = p.X - button.ActualWidth / 2, dy = p.Y - button.ActualHeight / 2;
        if (dx * dx + dy * dy > radius * radius) return;

        // Der Kalender setzt nach dem Klick die Beschriftungen neu – danach ersetzen.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (!button.IsToday) return;
            button.Content = CreateGlyph(button);
        });
    }

    private static readonly System.Windows.Media.Geometry Glyph = System.Windows.Media.Geometry.Parse(
        "M9.9,11.5 V3.9 A2.1,2.1 0 0 1 14.1,3.9 V11.5 " +
        "M9.9,10.3 A1.45,1.45 0 0 0 7,10.3 V16 C7,18.8 7.6,20.3 9,20.8 V22.5 " +
        "M14.1,10.3 A1.45,1.45 0 0 1 17,10.3 V12.4 " +
        "M17,11.1 A1.4,1.4 0 0 1 19.8,11.1 V15.5 C19.8,19 17.6,20.4 15.5,20.8 V22.5 " +
        "M7,13.4 C5.1,13.4 4.2,14.6 4.4,16 C4.6,17.4 5.8,17.9 7,17.7");

    /// <summary>24×24-Kontur wie die Audio-Symbole, in der Schriftfarbe des Tages und größer als die Zahl.</summary>
    private static FrameworkElement CreateGlyph(System.Windows.Controls.Control button)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Glyph,
            StrokeThickness = 1.7,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
            StrokeStartLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
        };
        path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,
            new System.Windows.Data.Binding(nameof(button.Foreground)) { Source = button });

        var canvas = new System.Windows.Controls.Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        double size = Math.Max(16, Math.Min(button.ActualWidth, button.ActualHeight) * 0.72);
        return new System.Windows.Controls.Viewbox
        {
            Child = canvas,
            Width = size,
            Height = size,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null and not T)
            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        return element as T;
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
        var flyout = new CalendarFlyout(culture, (int)center.X, barRect.Bottom + (int)(8 * scale), barRect.Top, barRect.Bottom, scale);
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
        int y = _anchorY;
        var monitor = MonitorService.Query(MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST));
        if (monitor is not null)
        {
            x = Math.Clamp(x, monitor.WorkArea.Left + 8, Math.Max(monitor.WorkArea.Left + 8, monitor.WorkArea.Right - rect.Width - 8));
            // Dynamische Leiste weit unten: Kalender oberhalb öffnen, damit er nicht aus dem Bild ragt.
            if (y + rect.Height > monitor.WorkArea.Bottom)
                y = Math.Max(monitor.WorkArea.Top, _barTop - rect.Height - (_anchorY - _barBottom));
        }

        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
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
