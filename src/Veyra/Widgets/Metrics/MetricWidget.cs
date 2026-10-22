using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Sensors;

namespace Veyra.Widgets.Metrics;

/// <summary>
/// Mess-Widget für CPU, GPU, RAM, GPU-Temperatur, Videospeicher, GPU-Takt, GPU-Lüfter, Akku oder Laufwerk: Wert in
/// Prozent (bzw. GB/°C/MHz/U/min/Restzeit), wahlweise mit Füllbalken oder
/// Verlauf der letzten Minute. Die Werte kommen vom gemeinsamen <see cref="SystemMonitor"/>; solange das Widget
/// angehalten ist (Leiste bei Vollbild ausgeblendet), wird dafür nichts gemessen.
/// </summary>
public sealed class MetricWidget : ObservableObject, IBarWidget
{
    private const int HistoryLength = 60;
    private const double GraphHeight = 18;

    private static readonly Brush WarnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF6, 0x74, 0x00)));
    private static readonly Brush CriticalBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xDA, 0x44, 0x53)));
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly string _entryId;
    private readonly WidgetType _type;
    private readonly SystemMonitor _monitor;
    private readonly Queue<double> _history = new();
    private MetricView? _view;
    private bool _running;
    private MetricSettings _metric = new();

    private string _valueText = "–";
    private string _toolTipText = "";
    private Brush _foreground = Brushes.White;
    private Brush _valueBrush = Brushes.White;
    private Brush _accentBrush = Brushes.White;
    private Brush _accentNormal = Brushes.White;
    private Brush _captionBrush = Brushes.White;
    private double _captionOpacity = 0.65;
    private Brush _trackBrush = Brushes.Transparent;
    private FontFamily _fontFamily = new("Segoe UI");
    private double _barWidth;
    private PointCollection _graphLine = [];
    private PointCollection _graphArea = [];
    private Visibility _captionVisibility = Visibility.Visible;
    private Visibility _barVisibility = Visibility.Visible;
    private Visibility _graphVisibility = Visibility.Collapsed;
    private double _valueMinWidth = 38;

    public MetricWidget(string entryId, WidgetType type, SystemMonitor monitor)
    {
        _entryId = entryId;
        _type = type;
        _monitor = monitor;
    }

    public FrameworkElement View => _view ??= new MetricView { DataContext = this };

    public string Caption => _type switch
    {
        WidgetType.Cpu => "CPU",
        WidgetType.Gpu => "GPU",
        WidgetType.Ram => "RAM",
        WidgetType.GpuMemory => "VRAM",
        WidgetType.GpuClock => "Takt",
        WidgetType.GpuFan => "Lüfter",
        WidgetType.Drive => _metric.Drive.TrimEnd('\\'),
        WidgetType.Battery => BatteryGlyph(_monitor.Battery),
        _ => "", // Thermometer
    };

    public FontFamily CaptionFontFamily => _type is WidgetType.GpuTemperature or WidgetType.Battery ? IconFont : FontFamily;

    /// <summary>Akkusymbol mit Füllstand in Zehnerstufen (Segoe Fluent Icons), beim Laden mit Blitz.</summary>
    private static string BatteryGlyph(BatteryReading battery)
    {
        if (battery.Percent is not { } percent) return ""; // unbekannt bzw. kein Akku
        int level = (int)Math.Clamp(Math.Round(percent / 10), 0, 10);
        if (battery.Charging) return level switch { 10 => "", 9 => "", _ => ((char)(0xE85A + level)).ToString() };
        return level == 10 ? "" : ((char)(0xE850 + level)).ToString();
    }

    public double TrackWidth => 44;

    public string ValueText { get => _valueText; private set => SetProperty(ref _valueText, value); }
    public string ToolTipText { get => _toolTipText; private set => SetProperty(ref _toolTipText, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }

    /// <summary>Farbe der Bezeichnung; ohne eigene Farbe = <see cref="Foreground"/>.</summary>
    public Brush CaptionBrush { get => _captionBrush; private set => SetProperty(ref _captionBrush, value); }

    public double CaptionOpacity { get => _captionOpacity; private set => SetProperty(ref _captionOpacity, value); }
    public Brush ValueBrush { get => _valueBrush; private set => SetProperty(ref _valueBrush, value); }
    public Brush AccentBrush { get => _accentBrush; private set => SetProperty(ref _accentBrush, value); }
    public Brush TrackBrush { get => _trackBrush; private set => SetProperty(ref _trackBrush, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }
    public double BarWidth { get => _barWidth; private set => SetProperty(ref _barWidth, value); }
    public PointCollection GraphLine { get => _graphLine; private set => SetProperty(ref _graphLine, value); }
    public PointCollection GraphArea { get => _graphArea; private set => SetProperty(ref _graphArea, value); }
    public Visibility CaptionVisibility { get => _captionVisibility; private set => SetProperty(ref _captionVisibility, value); }
    public Visibility BarVisibility { get => _barVisibility; private set => SetProperty(ref _barVisibility, value); }
    public Visibility GraphVisibility { get => _graphVisibility; private set => SetProperty(ref _graphVisibility, value); }
    public double ValueMinWidth { get => _valueMinWidth; private set => SetProperty(ref _valueMinWidth, value); }

    private SensorKind Kind => _type switch
    {
        WidgetType.Cpu => SensorKind.Cpu,
        WidgetType.Ram => SensorKind.Ram,
        WidgetType.Gpu => SensorKind.GpuUsage,
        WidgetType.Battery => SensorKind.Battery,
        WidgetType.Drive => SensorKind.Drive,
        WidgetType.GpuMemory => SensorKind.GpuMemory,
        WidgetType.GpuClock => SensorKind.GpuClock,
        WidgetType.GpuFan => SensorKind.GpuFan,
        _ => SensorKind.GpuTemperature,
    };

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _metric = entry?.Metric ?? new MetricSettings();

        FontFamily = new FontFamily(bar.Appearance.FontFamily);
        OnPropertyChanged(nameof(CaptionFontFamily));
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        Foreground = entry?.LabelColor is { } labelColor ? ColorUtil.ToBrush(labelColor, Colors.White) : barForeground;
        // Bezeichnung (CPU, GPU, RAM, Thermometer) eigene Farbe – ohne sie die Schriftfarbe der Leiste, etwas
        // zurückgenommen. Nie die Wertfarbe: Die gilt nur für den Wert.
        CaptionBrush = entry?.IconColor is { } captionColor ? ColorUtil.ToBrush(captionColor, Colors.White) : barForeground;
        CaptionOpacity = entry?.IconColor is null ? 0.65 : 1;
        _accentNormal = ColorUtil.ToBrush(entry?.IndicatorColor ?? WidgetEntry.DefaultIndicatorColor, Colors.DeepSkyBlue);
        // Hintergrundspur aus der Leistenfarbe, nicht aus der Wertfarbe – sonst färbt sie sich mit.
        TrackBrush = Freeze(new SolidColorBrush(((SolidColorBrush)barForeground).Color) { Opacity = 0.14 });

        CaptionVisibility = _metric.ShowCaption ? Visibility.Visible : Visibility.Collapsed;
        BarVisibility = _metric.Style == MetricStyle.Bar ? Visibility.Visible : Visibility.Collapsed;
        GraphVisibility = _metric.Style == MetricStyle.Graph ? Visibility.Visible : Visibility.Collapsed;
        // Platz für den breitesten Wert reservieren, damit Nachbar-Widgets nicht im Sekundentakt springen.
        ValueMinWidth = _type switch
        {
            WidgetType.Ram or WidgetType.GpuMemory when _metric.ShowAmount => 54,
            WidgetType.GpuClock => 66,
            WidgetType.GpuFan => 76,
            WidgetType.Drive when _metric.ShowAmount => 62,
            WidgetType.Battery when _metric.ShowAmount => 50,
            _ => 38,
        };
        OnPropertyChanged(nameof(Caption));

        Update();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _monitor.Updated += OnUpdated;
        _monitor.Acquire(Kind);
        Update();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _monitor.Updated -= OnUpdated;
        _monitor.Release(Kind);
    }

    public void Refresh() => Update();

    public void Dispose() => Stop();

    private void OnUpdated(object? sender, EventArgs e)
    {
        var (value, _, _) = Read();
        if (value is { } v)
        {
            _history.Enqueue(Math.Clamp(v, 0, 100));
            while (_history.Count > HistoryLength) _history.Dequeue();
        }
        Update();
    }

    /// <summary>
    /// Aktueller Wert (0–100 bzw. °C) und Grafikkarte (nur GPU-Widgets). Takt und Lüfter liefern hier den Anteil am
    /// Höchstwert für Balken und Verlauf; angezeigt werden MHz bzw. U/min.
    /// </summary>
    private (double? Value, GpuInfo? Gpu, GpuReading? Reading) Read()
    {
        switch (_type)
        {
            case WidgetType.Cpu:
                return (_monitor.CpuUsage, null, null);
            case WidgetType.Ram:
                return (_monitor.RamUsage, null, null);
            case WidgetType.Battery:
                return (_monitor.Battery.Percent, null, null);
            case WidgetType.Drive:
                return (_monitor.GetDrive(_metric.Drive)?.Usage, null, null);
            default:
                var gpu = _monitor.ResolveGpu(_metric.Gpu);
                var reading = gpu is null ? null : _monitor.Gpu.GetValueOrDefault(gpu.CounterKey);
                double? value = _type switch
                {
                    WidgetType.Gpu => reading?.Usage,
                    WidgetType.GpuMemory => reading is not null && gpu!.DedicatedMemory > 0 ? 100.0 * reading.DedicatedUsed / gpu.DedicatedMemory : null,
                    WidgetType.GpuClock => reading?.ClockMhz is { } clock ? Share(clock, reading.MaxClockMhz ?? Math.Max(_peak, clock)) : null,
                    // Ohne bekannte Höchstdrehzahl gilt die höchste bisher gemessene, mindestens 3000 U/min.
                    WidgetType.GpuFan => reading?.FanRpm is { } rpm ? Share(rpm, reading.MaxFanRpm ?? Math.Max(Math.Max(_peak, rpm), 3000)) : null,
                    _ => reading?.Temperature,
                };
                return (value, gpu, reading);
        }
    }

    /// <summary>Höchster bisher gemessener Takt bzw. Drehzahl – Maßstab, falls der Treiber keinen Höchstwert meldet.</summary>
    private double _peak;

    private static double Share(double value, double max) => max > 0 ? Math.Clamp(100 * value / max, 0, 100) : 0;

    private void Update()
    {
        var culture = CultureInfo.CurrentCulture;
        var (value, gpu, reading) = Read();
        bool isTemperature = _type == WidgetType.GpuTemperature;
        if (_type == WidgetType.GpuClock && reading?.ClockMhz is { } clockNow) _peak = Math.Max(_peak, clockNow);
        if (_type == WidgetType.GpuFan && reading?.FanRpm is { } fanNow) _peak = Math.Max(_peak, fanNow);

        var drive = _type == WidgetType.Drive ? _monitor.GetDrive(_metric.Drive) : null;
        var battery = _monitor.Battery;
        if (_type == WidgetType.Battery) OnPropertyChanged(nameof(Caption)); // Symbol zeigt Füllstand und Laden

        ValueText = value is not { } v ? (_type == WidgetType.Battery ? "Kein Akku" : "–")
            : isTemperature ? $"{v:0} °C"
            : _type == WidgetType.GpuClock ? string.Format(culture, "{0:0} MHz", reading!.ClockMhz)
            : _type == WidgetType.GpuFan ? string.Format(culture, "{0:N0} U/min", reading!.FanRpm)
            : _type == WidgetType.Ram && _metric.ShowAmount ? string.Format(culture, "{0:0.0} GB", _monitor.RamUsed / 1073741824.0)
            : _type == WidgetType.GpuMemory && _metric.ShowAmount ? string.Format(culture, "{0:0.0} GB", reading!.DedicatedUsed / 1073741824.0)
            : _type == WidgetType.Drive && _metric.ShowAmount && drive is not null ? FormatSize(drive.Free, culture) + " frei"
            : _type == WidgetType.Battery && _metric.ShowAmount && battery.Remaining is { } left ? FormatDuration(left)
            : $"{v:0} %";

        ToolTipText = _type switch
        {
            WidgetType.Cpu => $"Prozessor: {ValueText}\n{_monitor.CpuName}",
            WidgetType.Ram => string.Format(culture, "Arbeitsspeicher: {0:0.0} von {1:0.0} GB belegt ({2:0} %)",
                _monitor.RamUsed / 1073741824.0, _monitor.RamTotal / 1073741824.0, _monitor.RamUsage ?? 0),
            WidgetType.Gpu => gpu is null ? "Keine Grafikkarte gefunden"
                : string.Format(culture, "Grafikkarte: {0}\n{1}\nVideospeicher: {2:0.0} von {3:0.0} GB", ValueText, gpu.Name,
                    (_monitor.Gpu.GetValueOrDefault(gpu.CounterKey)?.DedicatedUsed ?? 0) / 1073741824.0, gpu.DedicatedMemory / 1073741824.0),
            WidgetType.GpuMemory => gpu is null ? "Keine Grafikkarte gefunden"
                : string.Format(culture, "Videospeicher: {0:0.0} von {1:0.0} GB belegt ({2:0} %)\n{3}",
                    (reading?.DedicatedUsed ?? 0) / 1073741824.0, gpu.DedicatedMemory / 1073741824.0, value ?? 0, gpu.Name),
            WidgetType.GpuClock => gpu is null ? "Keine Grafikkarte gefunden"
                : reading?.ClockMhz is null ? $"{gpu.Name}\nDer Treiber meldet keinen Takt."
                : string.Format(culture, "GPU-Takt: {0}{1}\n{2}", ValueText,
                    reading.MaxClockMhz is { } maxClock ? string.Format(culture, " (höchstens {0:0} MHz)", maxClock) : "", gpu.Name),
            WidgetType.GpuFan => gpu is null ? "Keine Grafikkarte gefunden"
                : reading?.FanRpm is not { } rpm ? $"{gpu.Name}\nDer Treiber meldet keine Lüfterdrehzahl."
                : string.Format(culture, "Lüfter: {0}{1}{2}\n{3}", ValueText,
                    reading.MaxFanRpm is { } maxFan ? string.Format(culture, " (höchstens {0:N0} U/min)", maxFan) : "",
                    rpm == 0 ? "\nLüfter steht – viele Karten schalten ihn bei geringer Last ab." : "", gpu.Name),
            WidgetType.Battery => BatteryToolTip(battery),
            WidgetType.Drive => drive is null ? $"Laufwerk {_metric.Drive} nicht gefunden"
                : string.Format(culture, "{0}{1}\n{2} frei von {3} ({4:0} % belegt)",
                    drive.Label.Length > 0 ? drive.Label + " " : "Laufwerk ", $"({drive.Root.TrimEnd('\\')})",
                    FormatSize(drive.Free, culture), FormatSize(drive.Total, culture), drive.Usage),
            _ => gpu is null ? "Keine Grafikkarte gefunden"
                : value is null ? $"{gpu.Name}\nDer Treiber meldet keine Temperatur."
                : $"Temperatur: {ValueText}\n{gpu.Name}",
        };

        // Warnfarben: Last ab 80 % / 95 %, Temperatur ab 80 °C / 90 °C, Laufwerk ab 90 % / 95 %,
        // Akku umgekehrt: ab 20 % / 10 % abwärts – aber nicht, solange er lädt.
        // Takt und Lüfter: kein Warnwert – hoher Takt bzw. laufender Lüfter ist unter Last normal.
        var level = value is not { } x || !_metric.WarnColors || _type is WidgetType.GpuClock or WidgetType.GpuFan ? 0
            : _type == WidgetType.Battery ? (battery.Charging || battery.OnAc ? 0 : x <= 10 ? 2 : x <= 20 ? 1 : 0)
            : _type == WidgetType.Drive ? (x >= 95 ? 2 : x >= 90 ? 1 : 0)
            : x >= (isTemperature ? 90 : 95) ? 2
            : x >= 80 ? 1
            : 0;
        ValueBrush = level switch { 2 => CriticalBrush, 1 => WarnBrush, _ => Foreground };
        AccentBrush = level switch { 2 => CriticalBrush, 1 => WarnBrush, _ => _accentNormal };

        BarWidth = TrackWidth * Math.Clamp((value ?? 0) / 100, 0, 1);
        if (_type == WidgetType.Battery)
        {
            // Ohne Akku (Desktop-PC) keine leere Spur neben „Kein Akku“.
            BarVisibility = _metric.Style == MetricStyle.Bar && value is not null ? Visibility.Visible : Visibility.Collapsed;
            GraphVisibility = _metric.Style == MetricStyle.Graph && value is not null ? Visibility.Visible : Visibility.Collapsed;
        }
        if (_metric.Style == MetricStyle.Graph) UpdateGraph();
    }

    private static string BatteryToolTip(BatteryReading battery)
    {
        if (battery.Percent is not { } percent) return "Kein Akku gefunden";
        var lines = new List<string> { $"Akku: {percent:0} %" };
        lines.Add(battery.Charging ? "Wird geladen" : battery.OnAc ? "Netzbetrieb" : "Akkubetrieb");
        if (battery.Remaining is { } left) lines.Add($"Restlaufzeit: {FormatDuration(left)}");
        if (battery.Saver) lines.Add("Energiesparmodus aktiv");
        return string.Join("\n", lines);
    }

    /// <summary>"3:25 h" bzw. "45 min".</summary>
    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00} h" : $"{Math.Max(1, span.Minutes)} min";

    /// <summary>"512 GB", "1,82 TB", "86,4 GB" – in 1024er-Schritten wie der Explorer.</summary>
    private static string FormatSize(ulong bytes, CultureInfo culture)
    {
        double gb = bytes / 1073741824.0;
        if (gb >= 1000) return string.Format(culture, "{0:0.00} TB", gb / 1024);
        return string.Format(culture, gb >= 100 ? "{0:0} GB" : "{0:0.0} GB", gb);
    }

    private void UpdateGraph()
    {
        var line = new PointCollection(HistoryLength);
        var area = new PointCollection(HistoryLength + 2);
        double step = TrackWidth / (HistoryLength - 1);
        // Rechtsbündig: der neueste Wert steht rechts, ältere wandern nach links hinaus.
        double x = TrackWidth - (_history.Count - 1) * step;
        if (_history.Count > 0) area.Add(new Point(x, GraphHeight));
        foreach (double value in _history)
        {
            var point = new Point(x, GraphHeight - value / 100 * (GraphHeight - 2) - 1);
            line.Add(point);
            area.Add(point);
            x += step;
        }
        if (_history.Count > 0) area.Add(new Point(TrackWidth, GraphHeight));
        line.Freeze();
        area.Freeze();
        GraphLine = line;
        GraphArea = area;
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
