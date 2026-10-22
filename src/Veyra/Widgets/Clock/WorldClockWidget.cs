using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Widgets.Clock;

/// <summary>Eine Zeitzone im Popup der Weltuhr.</summary>
public sealed record WorldClockRow(string Label, string Time, string Detail, string Glyph);

/// <summary>
/// Uhrzeit in anderen Zeitzonen, z. B. „New York 08:31 · Tokio 21:31“. Wie die Uhr wird der Timer genau auf den
/// nächsten Wechsel gestellt (zur vollen Minute bzw. Sekunde). Klick öffnet alle Orte mit Datum und Zeitunterschied.
/// </summary>
public sealed class WorldClockWidget : ObservableObject, IBarWidget
{
    private readonly string _entryId;
    private readonly DispatcherTimer _timer;
    private readonly Border _view;
    private readonly StackPanel _panel;
    private WorldClockSettings _clock = new();
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private Brush _labelBrush = Brushes.White;
    private Brush _timeBrush = Brushes.White;
    private FontFamily _fontFamily = new("Segoe UI");
    private WorldClockFlyout? _flyout;
    private bool _running;

    public WorldClockWidget(string entryId)
    {
        _entryId = entryId;
        _timer = new DispatcherTimer(DispatcherPriority.Normal);
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Update();
        };
        _panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _view = new Border { Child = _panel };
        _view.SetResourceReference(FrameworkElement.StyleProperty, "WidgetHoverArea");
        _view.MouseLeftButtonUp += (_, _) => ToggleFlyout();
    }

    public FrameworkElement View => _view;

    public IReadOnlyList<WorldClockRow> Rows { get; private set; } = [];
    public string LocalText { get; private set; } = "";

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _clock = entry?.WorldClock ?? new WorldClockSettings();
        _culture = ClockSettings.ResolveCulture("");
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        _labelBrush = entry?.IconColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        _timeBrush = entry?.LabelColor is { } time ? ColorUtil.ToBrush(time, Colors.White) : barForeground;
        _fontFamily = new FontFamily(bar.Appearance.FontFamily);
        Refresh();
    }

    public void Start()
    {
        _running = true;
        Refresh();
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
    }

    public void Refresh()
    {
        _timer.Stop();
        Update();
    }

    public void Dispose()
    {
        FlyoutWindow.CloseFor(_view);
        Stop();
    }

    private IEnumerable<(WorldClockZone Zone, TimeZoneInfo Info)> Zones() =>
        _clock.Zones.Select(z => (z, WorldClockSettings.FindZone(z.TimeZoneId))).Where(z => z.Item2 is not null)!;

    private static string LabelOf(WorldClockZone zone, TimeZoneInfo info) => zone.Label.Length > 0 ? zone.Label : WorldClockSettings.DefaultLabel(info);

    private void Update()
    {
        var utc = DateTime.UtcNow;
        var local = DateTime.Now;
        var zones = Zones().ToList();

        _panel.Children.Clear();
        var shown = _clock.ShowAllInBar ? zones : zones.Take(1).ToList();
        if (shown.Count == 0)
        {
            _panel.Children.Add(new TextBlock { Text = "Weltuhr", Foreground = _timeBrush, FontFamily = _fontFamily, FontSize = 13, Opacity = 0.6 });
        }
        for (int i = 0; i < shown.Count; i++)
        {
            var (zone, info) = shown[i];
            var time = TimeZoneInfo.ConvertTimeFromUtc(utc, info);
            if (i > 0) _panel.Children.Add(new TextBlock { Text = "·", Foreground = _labelBrush, Opacity = 0.4, FontSize = 13, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            if (_clock.ShowLabels)
            {
                _panel.Children.Add(new TextBlock
                {
                    Text = LabelOf(zone, info),
                    Foreground = _labelBrush,
                    Opacity = 0.7,
                    FontFamily = _fontFamily,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0),
                });
            }
            var value = new TextBlock
            {
                Text = time.ToString(_clock.TimeFormat, _culture),
                Foreground = _timeBrush,
                FontFamily = _fontFamily,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Typography.SetNumeralAlignment(value, FontNumeralAlignment.Tabular);
            _panel.Children.Add(value);
            int days = (time.Date - local.Date).Days;
            if (_clock.ShowDayOffset && days != 0)
            {
                _panel.Children.Add(new TextBlock
                {
                    Text = days > 0 ? $"+{days}" : $"−{-days}",
                    Foreground = _timeBrush,
                    Opacity = 0.6,
                    FontSize = 9.5,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(2, 0, 0, 0),
                });
            }
        }

        Rows = zones.Select(z => BuildRow(z.Zone, z.Info, utc, local)).ToList();
        LocalText = $"Hier: {local.ToString("dddd, d. MMMM, " + _clock.TimeFormat, _culture)} ({FormatOffset(TimeZoneInfo.Local.GetUtcOffset(utc))})";
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(LocalText));
        _view.ToolTip = zones.Count == 0
            ? "Weltuhr – Zeitzonen in den Einstellungen hinzufügen"
            : string.Join("\n", Rows.Select(r => $"{r.Label}: {r.Time}  ({r.Detail})")) + "\nKlicken für Details";

        if (!_running) return;
        // Auf den nächsten Wechsel der Anzeige stellen.
        long unit = ClockSettings.HasSeconds(_clock.TimeFormat) || _flyout is not null ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
        long untilChange = unit - local.Ticks % unit;
        _timer.Interval = TimeSpan.FromTicks(untilChange) + TimeSpan.FromMilliseconds(15);
        _timer.Start();
    }

    private WorldClockRow BuildRow(WorldClockZone zone, TimeZoneInfo info, DateTime utc, DateTime local)
    {
        var time = TimeZoneInfo.ConvertTimeFromUtc(utc, info);
        var difference = info.GetUtcOffset(utc) - TimeZoneInfo.Local.GetUtcOffset(utc);
        int days = (time.Date - local.Date).Days;
        string day = days switch { 0 => "Heute", 1 => "Morgen", -1 => "Gestern", _ => time.ToString("ddd, d. MMM", _culture) };
        string diff = difference == TimeSpan.Zero ? "gleiche Zeit" : FormatDifference(difference);
        string glyph = time.Hour is >= 6 and < 20 ? "" : ""; // Sonne bzw. Mond
        string detail = $"{day} · {diff} · {FormatOffset(info.GetUtcOffset(utc))}" + (info.IsDaylightSavingTime(utc) ? " · Sommerzeit" : "");
        return new WorldClockRow(LabelOf(zone, info), time.ToString(_clock.TimeFormat, _culture), detail, glyph);
    }

    private static string FormatDifference(TimeSpan difference)
    {
        string sign = difference > TimeSpan.Zero ? "+" : "−";
        var d = difference.Duration();
        return d.Minutes == 0 ? $"{sign}{(int)d.TotalHours} Std." : $"{sign}{(int)d.TotalHours}:{d.Minutes:00} Std.";
    }

    private static string FormatOffset(TimeSpan offset)
    {
        string sign = offset < TimeSpan.Zero ? "−" : "+";
        var o = offset.Duration();
        return o.Minutes == 0 ? $"UTC{sign}{o.Hours}" : $"UTC{sign}{o.Hours}:{o.Minutes:00}";
    }

    private void ToggleFlyout()
    {
        _flyout = FlyoutWindow.Toggle(_view, () => new WorldClockFlyout { DataContext = this }) as WorldClockFlyout;
        if (_flyout is not null) _flyout.Closed += (_, _) => _flyout = null;
        Refresh(); // im Popup sekundengenau
    }
}
