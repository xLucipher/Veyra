using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Widgets.Clock;

/// <summary>
/// Digitale Uhr. Der Timer wird jeweils exakt auf die nächste Sekunden- bzw. Minutengrenze gestellt,
/// statt in festen Intervallen zu laufen: dadurch kein Driften, kein Zittern und – wenn keine Sekunden
/// angezeigt werden – nur ein Tick pro Minute. Texte (und damit Rendering) ändern sich nur bei echten Änderungen.
/// </summary>
public sealed class ClockWidget : ObservableObject, IBarWidget
{
    private static readonly Color DefaultForeground = Colors.White;

    private readonly DispatcherTimer _timer;
    private ClockView? _view;
    private bool _running;

    private string _timeFormat = "HH:mm:ss";
    private string _dateFormat = "";
    private string _weekFormat = "KW {0}";
    private bool _showDate;
    private bool _showWeek;
    private bool _secondResolution = true;
    private CultureInfo _culture = CultureInfo.CurrentCulture;

    private string _timeText = "";
    private string _dateText = "";
    private Visibility _dateVisibility = Visibility.Collapsed;
    private FontFamily _fontFamily = new("Segoe UI");
    private double _fontSize = 20;
    private FontWeight _fontWeight = FontWeights.SemiBold;
    private Brush _foreground = Brushes.White;
    private double _dateFontSize = 11;
    private Brush _dateForeground = Brushes.LightGray;

    public ClockWidget()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Normal);
        _timer.Tick += OnTick;
    }

    /// <summary>Kultur der Uhr – auch für Wochentage/Monatsnamen im Kalender-Flyout.</summary>
    public CultureInfo Culture => _culture;

    public FrameworkElement View => _view ??= new ClockView { DataContext = this };

    public string TimeText { get => _timeText; private set => SetProperty(ref _timeText, value); }
    public string DateText { get => _dateText; private set => SetProperty(ref _dateText, value); }
    public Visibility DateVisibility { get => _dateVisibility; private set => SetProperty(ref _dateVisibility, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; private set => SetProperty(ref _fontSize, value); }
    public FontWeight FontWeight { get => _fontWeight; private set => SetProperty(ref _fontWeight, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public double DateFontSize { get => _dateFontSize; private set => SetProperty(ref _dateFontSize, value); }
    public Brush DateForeground { get => _dateForeground; private set => SetProperty(ref _dateForeground, value); }

    public void ApplySettings(AppSettings settings)
    {
        var clock = settings.Clock;

        _timeFormat = clock.TimeFormat;
        _dateFormat = clock.DateFormat;
        _weekFormat = clock.WeekNumberFormat;
        _showDate = clock.ShowDate;
        _showWeek = clock.ShowWeekNumber;
        // Ohne Sekunden genügt ein Tick pro Minute (exakt zum Minutenwechsel).
        _secondResolution = ClockSettings.HasSeconds(_timeFormat);
        _culture = ClockSettings.ResolveCulture(clock.Culture);

        var family = string.IsNullOrWhiteSpace(clock.FontFamily) ? settings.Appearance.FontFamily : clock.FontFamily;
        FontFamily = new FontFamily(family);
        FontSize = clock.FontSize;
        FontWeight = ParseWeight(clock.FontWeight);
        Foreground = ColorUtil.ToBrush(clock.FontColor, DefaultForeground);
        DateFontSize = clock.DateFontSize;
        DateForeground = ColorUtil.ToBrush(clock.DateFontColor, Colors.LightGray);
        DateVisibility = _showDate || _showWeek ? Visibility.Visible : Visibility.Collapsed;

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
        UpdateTexts();
        if (_running) ScheduleNextTick();
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTick;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        UpdateTexts();
        ScheduleNextTick();
    }

    private void ScheduleNextTick()
    {
        var now = DateTime.Now;
        long unit = _secondResolution ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMinute;
        long remaining = unit - now.Ticks % unit;
        // Kleiner Puffer, damit der Tick sicher NACH der Grenze liegt. Kommt er doch zu früh,
        // ist der nächste Tick nur Millisekunden entfernt – die Uhr korrigiert sich selbst.
        _timer.Interval = TimeSpan.FromTicks(remaining) + TimeSpan.FromMilliseconds(15);
        _timer.Start();
    }

    private void UpdateTexts()
    {
        var now = DateTime.Now;
        TimeText = Format(now, _timeFormat);

        if (_showDate || _showWeek)
        {
            var parts = new List<string>(2);
            if (_showDate) parts.Add(Format(now, _dateFormat));
            if (_showWeek) parts.Add(string.Format(_culture, _weekFormat, ISOWeek.GetWeekOfYear(now)));
            DateText = string.Join("  ·  ", parts);
        }
        else
        {
            DateText = "";
        }
    }

    private string Format(DateTime value, string format)
    {
        try
        {
            return value.ToString(format, _culture);
        }
        catch (FormatException)
        {
            return value.ToString("HH:mm:ss", _culture);
        }
    }

    private static FontWeight ParseWeight(string? weight) => weight switch
    {
        "Normal" => FontWeights.Normal,
        "Medium" => FontWeights.Medium,
        "Bold" => FontWeights.Bold,
        _ => FontWeights.SemiBold,
    };
}
