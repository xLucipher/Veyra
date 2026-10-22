using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Widgets.Timers;

/// <summary>
/// Verbleibende Zeit bis zu einem Datum, z. B. „Urlaub 12 Tage“ – optional wiederholt (täglich, wöchentlich …),
/// z. B. bis zum nächsten Reset in einem Spiel. Wie bei der Uhr wird der Timer genau auf den nächsten Wechsel der
/// Anzeige gestellt: mit Sekunden einmal pro Sekunde, sonst einmal pro Minute.
/// </summary>
public sealed class CountdownWidget : IBarWidget
{
    private readonly string _entryId;
    private readonly DispatcherTimer _timer;
    private readonly StackPanel _view;
    private readonly TextBlock _title;
    private readonly TextBlock _value;
    private CountdownSettings _countdown = new();
    private bool _running;

    public CountdownWidget(string entryId)
    {
        _entryId = entryId;
        _timer = new DispatcherTimer(DispatcherPriority.Normal);
        _timer.Tick += OnTick;
        _title = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        _value = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Typography.SetNumeralAlignment(_value, FontNumeralAlignment.Tabular);
        _view = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
            Background = Brushes.Transparent,
            Children = { _title, _value },
        };
    }

    public FrameworkElement View => _view;

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _countdown = entry?.Countdown ?? new CountdownSettings();

        var family = new FontFamily(bar.Appearance.FontFamily);
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        _title.FontFamily = family;
        _value.FontFamily = family;
        _title.Foreground = entry?.IconColor is { } titleColor ? ColorUtil.ToBrush(titleColor, Colors.White) : barForeground;
        _title.Opacity = entry?.IconColor is null ? 0.65 : 1;
        _value.Foreground = entry?.LabelColor is { } valueColor ? ColorUtil.ToBrush(valueColor, Colors.White) : barForeground;
        _title.Text = _countdown.Title;
        _title.Visibility = _countdown.ShowTitle && _countdown.Title.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    /// <summary>So lange steht bei Wiederholung der Text nach Ablauf, bevor es zum nächsten Termin weitergeht.</summary>
    private static readonly TimeSpan DoneDuration = TimeSpan.FromMinutes(1);

    private string ToolTipFor(DateTime target)
    {
        string text = (_countdown.Title.Length > 0 ? _countdown.Title + ": " : "") +
                      target.ToString("dddd, d. MMMM yyyy, HH:mm 'Uhr'", CultureInfo.CurrentCulture);
        return _countdown.Repeat switch
        {
            CountdownRepeat.Daily => text + "\nWiederholt sich täglich",
            CountdownRepeat.Weekly => text + "\nWiederholt sich wöchentlich",
            CountdownRepeat.Monthly => text + "\nWiederholt sich monatlich",
            CountdownRepeat.Yearly => text + "\nWiederholt sich jährlich",
            _ => text,
        };
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
        Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        Update();
    }

    private void Update()
    {
        var now = DateTime.Now;

        // Wiederholung: Gerade erreicht → eine Minute lang den Text nach Ablauf zeigen, danach zum nächsten Termin.
        if (_countdown.Repeat != CountdownRepeat.None && _countdown.DoneText.Length > 0 &&
            _countdown.NextOccurrence(now - DoneDuration) is var reached && reached <= now)
        {
            _value.Text = _countdown.DoneText;
            _view.ToolTip = ToolTipFor(_countdown.NextOccurrence(now));
            if (!_running) return;
            _timer.Interval = reached + DoneDuration - now + TimeSpan.FromMilliseconds(15);
            _timer.Start();
            return;
        }

        var target = _countdown.NextOccurrence(now);
        var remaining = target - now;
        _value.Text = Format(_countdown, target, now, remaining);
        _view.ToolTip = ToolTipFor(target);
        if (!_running || remaining <= TimeSpan.Zero) return; // erreicht (ohne Wiederholung): nichts mehr zu tun

        // Auf den nächsten Wechsel der Anzeige stellen – mit Sekunden im Sekundentakt, sonst zur vollen Minute.
        long unit = _countdown.Style == CountdownStyle.Clock ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
        long untilChange = remaining.Ticks % unit;
        if (untilChange == 0) untilChange = unit;
        _timer.Interval = TimeSpan.FromTicks(untilChange) + TimeSpan.FromMilliseconds(15);
        _timer.Start();
    }

    /// <summary>Anzeige der Restzeit; aufgerundet auf die angezeigte Einheit, damit „0 Min.“ nie vor dem Ziel erscheint.</summary>
    internal static string Format(CountdownSettings countdown, DateTime target, DateTime now, TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return countdown.DoneText.Length > 0 ? countdown.DoneText : "0";

        switch (countdown.Style)
        {
            case CountdownStyle.Clock:
            {
                var r = TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds));
                string clock = $"{r.Hours:00}:{r.Minutes:00}:{r.Seconds:00}";
                return r.Days > 0 ? $"{r.Days} T {clock}" : clock;
            }
            case CountdownStyle.DaysHours:
            {
                var r = TimeSpan.FromMinutes(Math.Ceiling(remaining.TotalMinutes));
                if (r.Days > 0) return r.Hours > 0 ? $"{r.Days} T {r.Hours} Std." : $"{r.Days} T";
                return r.Hours > 0 ? $"{r.Hours} Std. {r.Minutes} Min." : $"{r.Minutes} Min.";
            }
            default:
            {
                // Ab einem Tag Kalendertage („morgen“ = 1 Tag), darunter Stunden bzw. Minuten.
                if (remaining.TotalHours >= 24)
                {
                    int days = Math.Max(1, (target.Date - now.Date).Days);
                    return days == 1 ? "1 Tag" : $"{days} Tage";
                }
                if (remaining.TotalMinutes > 60) return $"{(int)Math.Ceiling(remaining.TotalHours)} Std.";
                return $"{(int)Math.Ceiling(remaining.TotalMinutes)} Min.";
            }
        }
    }
}
