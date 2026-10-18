using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Threading;

namespace Veyra.Widgets.Clock;

/// <summary>
/// Monatskalender, der sich beim Klick auf Uhrzeit/Datum direkt unter der Uhr öffnet (Verhalten: <see cref="FlyoutWindow"/>).
/// Die Kopfzeile tickt nur, solange er offen ist.
/// </summary>
public partial class CalendarFlyout : FlyoutWindow
{
    private readonly CultureInfo _culture;
    private readonly DispatcherTimer _timer;

    private CalendarFlyout(CultureInfo culture)
    {
        InitializeComponent();
        _culture = culture;

        Language = XmlLanguage.GetLanguage(culture.IetfLanguageTag);
        MonthCalendar.FirstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;

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
    public static void Toggle(FrameworkElement anchor, CultureInfo culture) => Toggle(anchor, () => new CalendarFlyout(culture));

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        _timer.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
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

    private void OnOpenSystemSettings(object sender, RoutedEventArgs e) => OpenAndClose("ms-settings:dateandtime");
}
