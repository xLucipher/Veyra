using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Weather;

namespace Veyra.Widgets.Weather;

public sealed record WeatherHourRow(string Time, string Glyph, string Temperature, string Rain);

public sealed record WeatherDayRow(string Day, string Glyph, string Condition, string Max, string Min, string Rain);

/// <summary>
/// Aktuelles Wetter (Symbol und Temperatur, optional Wetterlage, Höchst-/Tiefstwert, Ort). Klick öffnet die Details
/// mit den nächsten Stunden und 7 Tagen. Abgefragt wird im eingestellten Abstand, nur solange das Widget läuft; nach
/// einer Pause (z. B. im Spiel) nur, wenn der letzte Stand älter als dieser Abstand ist.
/// </summary>
public sealed class WeatherWidget : ObservableObject, IBarWidget
{
    private readonly string _entryId;
    private readonly DispatcherTimer _timer = new();
    private readonly Border _view;
    private readonly TextBlock _glyph;
    private readonly TextBlock _temperature;
    private readonly TextBlock _detail;
    private WeatherSettings _weather = new();
    private WeatherReport? _report;
    private string _reportKey = "";
    private WeatherFlyout? _flyout;
    private bool _running;
    private bool _loading;

    public WeatherWidget(string entryId)
    {
        _entryId = entryId;
        _timer.Tick += (_, _) => _ = LoadAsync(force: false);
        _glyph = new TextBlock { FontFamily = new FontFamily(WeatherService.GlyphFont), FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        _temperature = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        _detail = new TextBlock { FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0), MaxWidth = 320, TextTrimming = TextTrimming.CharacterEllipsis };
        _view = new Border { Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _glyph, _temperature, _detail } } };
        _view.SetResourceReference(FrameworkElement.StyleProperty, "WidgetHoverArea");
        _view.MouseLeftButtonUp += (_, _) => ToggleFlyout();
        RefreshCommand = new RelayCommand(() => _ = LoadAsync(force: true));
        OpenSourceCommand = new RelayCommand(() => _flyout?.OpenAndClose("https://open-meteo.com/"));
    }

    public FrameworkElement View => _view;

    public ICommand RefreshCommand { get; }
    public ICommand OpenSourceCommand { get; }

    // ---- Popup ------------------------------------------------------------------------------
    public string Location => _weather.HasLocation ? _weather.LocationName : "Kein Ort gewählt";

    public string Status => !_weather.HasLocation
        ? "Ort in den Einstellungen des Widgets wählen."
        : _loading
            ? "Wird geladen …"
            : _report is null
                ? "Keine Wetterdaten – keine Verbindung?"
                : $"Stand {_report.FetchedAt.ToString("HH:mm", CultureInfo.CurrentCulture)} · Wetterdaten von Open-Meteo.com";

    public string CurrentGlyph => _report is null ? "" : WeatherService.Glyph(_report.Code, _report.IsDay);
    public string CurrentTemperature => _report is null ? "–" : WeatherService.FormatTemperature(_report.Temperature);
    public string CurrentCondition => _report is null ? "" : WeatherService.Describe(_report.Code);

    public string CurrentDetails => _report is null
        ? ""
        : string.Format(CultureInfo.CurrentCulture, "Gefühlt {0} · Luftfeuchte {1} % · Wind {2:0} {3}",
            WeatherService.FormatTemperature(_report.FeelsLike), _report.Humidity, _report.WindSpeed, WeatherService.WindSymbol(_weather.WindUnit));

    public string SunText => _report?.Days.FirstOrDefault() is { Sunrise: { } rise, Sunset: { } set }
        ? $"Sonnenaufgang {rise:HH:mm} · Sonnenuntergang {set:HH:mm}"
        : "";

    public IReadOnlyList<WeatherHourRow> Hours { get; private set; } = [];
    public IReadOnlyList<WeatherDayRow> Days { get; private set; } = [];
    public Visibility ForecastVisibility => _report is null ? Visibility.Collapsed : Visibility.Visible;

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _weather = entry?.Weather ?? new WeatherSettings();
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        _glyph.Foreground = entry?.IconColor is { } icon ? ColorUtil.ToBrush(icon, Colors.White) : barForeground;
        var text = entry?.LabelColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        _temperature.Foreground = text;
        _detail.Foreground = text;
        var family = new FontFamily(bar.Appearance.FontFamily);
        _temperature.FontFamily = family;
        _detail.FontFamily = family;
        _timer.Interval = TimeSpan.FromMinutes(_weather.IntervalMinutes);

        // Anderer Ort oder andere Einheit → alte Werte gelten nicht mehr.
        string key = string.Create(CultureInfo.InvariantCulture, $"{_weather.Latitude}|{_weather.Longitude}|{_weather.Unit}|{_weather.WindUnit}");
        if (key != _reportKey)
        {
            _reportKey = key;
            _report = null;
            if (_running) _ = LoadAsync(force: false);
        }
        Update();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _timer.Start();
        if (_report is null || DateTime.Now - _report.FetchedAt >= _timer.Interval) _ = LoadAsync(force: false);
    }

    public void Stop()
    {
        _running = false;
        _timer.Stop();
    }

    public void Refresh()
    {
    }

    public void Dispose()
    {
        FlyoutWindow.CloseFor(_view);
        Stop();
    }

    private async Task LoadAsync(bool force)
    {
        if (_loading || !_weather.HasLocation) return;
        _loading = true;
        Update();
        string key = _reportKey;
        try
        {
            var report = await WeatherService.GetAsync(_weather, force);
            if (key != _reportKey) return; // inzwischen anderer Ort
            if (report is not null || _report is null || DateTime.Now - _report.FetchedAt > TimeSpan.FromHours(3))
                _report = report; // bei kurzem Ausfall den letzten Stand behalten
        }
        finally
        {
            _loading = false;
            Update();
        }
    }

    private void Update()
    {
        var unit = WeatherService.UnitSymbol(_weather.Unit);
        if (!_weather.HasLocation)
        {
            _glyph.Text = "☁";
            _temperature.Text = "Wetter";
            _detail.Visibility = Visibility.Collapsed;
            _view.ToolTip = "Wetter – Ort in den Einstellungen wählen";
        }
        else if (_report is null)
        {
            _glyph.Text = "☁";
            _temperature.Text = "–";
            _detail.Visibility = Visibility.Collapsed;
            _view.ToolTip = $"Wetter in {_weather.LocationName}\n{(_loading ? "Wird geladen …" : "Keine Daten – keine Verbindung?")}";
        }
        else
        {
            _glyph.Text = WeatherService.Glyph(_report.Code, _report.IsDay);
            _temperature.Text = WeatherService.FormatTemperature(_report.Temperature);
            var parts = new List<string>();
            if (_weather.ShowCondition) parts.Add(WeatherService.Describe(_report.Code));
            if (_weather.ShowHighLow && _report.Days.FirstOrDefault() is { } today)
                parts.Add($"{WeatherService.FormatTemperature(today.Max)} / {WeatherService.FormatTemperature(today.Min)}");
            if (_weather.ShowLocation) parts.Add(_weather.LocationName.Split(',')[0]);
            _detail.Text = string.Join(" · ", parts);
            _detail.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _view.ToolTip = $"{_weather.LocationName}: {WeatherService.Describe(_report.Code)}, {_temperature.Text.TrimEnd('°')} {unit}\n" +
                            string.Format(CultureInfo.CurrentCulture, "Gefühlt {0} · Wind {1:0} {2} · Stand {3:HH:mm}\nKlicken für die Vorhersage",
                                WeatherService.FormatTemperature(_report.FeelsLike), _report.WindSpeed, WeatherService.WindSymbol(_weather.WindUnit), _report.FetchedAt);
        }

        var culture = CultureInfo.CurrentCulture;
        Hours = _report?.Hours.Select(h => new WeatherHourRow(h.Time.ToString("HH", culture) + " Uhr", WeatherService.Glyph(h.Code, h.IsDay),
            WeatherService.FormatTemperature(h.Temperature), h.PrecipitationChance is > 0 and var p ? $"{p} %" : "")).ToList() ?? [];
        Days = _report?.Days.Select((d, i) => new WeatherDayRow(
            i == 0 ? "Heute" : i == 1 ? "Morgen" : d.Date.ToString("dddd", culture),
            WeatherService.Glyph(d.Code, true), WeatherService.Describe(d.Code),
            WeatherService.FormatTemperature(d.Max), WeatherService.FormatTemperature(d.Min),
            d.PrecipitationChance is > 0 and var p ? $"{p} %" : "")).ToList() ?? [];
        foreach (var name in new[] { nameof(Location), nameof(Status), nameof(CurrentGlyph), nameof(CurrentTemperature), nameof(CurrentCondition),
                     nameof(CurrentDetails), nameof(SunText), nameof(Hours), nameof(Days), nameof(ForecastVisibility) })
            OnPropertyChanged(name);
    }

    private void ToggleFlyout()
    {
        _flyout = FlyoutWindow.Toggle(_view, () => new WeatherFlyout { DataContext = this }) as WeatherFlyout;
        if (_flyout is null) return;
        _flyout.Closed += (_, _) => _flyout = null;
        // Ältere Daten beim Öffnen auffrischen (der Zwischenspeicher verhindert doppelte Abrufe).
        if (_report is null || DateTime.Now - _report.FetchedAt > TimeSpan.FromMinutes(10)) _ = LoadAsync(force: false);
    }
}
