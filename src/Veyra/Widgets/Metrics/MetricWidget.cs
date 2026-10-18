using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Sensors;

namespace Veyra.Widgets.Metrics;

/// <summary>
/// Mess-Widget für CPU, GPU, RAM oder GPU-Temperatur: Wert in Prozent (bzw. GB/°C), wahlweise mit Füllbalken oder
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
        _ => "", // Thermometer
    };

    public FontFamily CaptionFontFamily => _type == WidgetType.GpuTemperature ? IconFont : FontFamily;

    public double TrackWidth => 44;

    public string ValueText { get => _valueText; private set => SetProperty(ref _valueText, value); }
    public string ToolTipText { get => _toolTipText; private set => SetProperty(ref _toolTipText, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
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
        _ => SensorKind.GpuTemperature,
    };

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _metric = entry?.Metric ?? new MetricSettings();

        FontFamily = new FontFamily(bar.Appearance.FontFamily);
        OnPropertyChanged(nameof(CaptionFontFamily));
        Foreground = entry?.LabelColor is { } labelColor
            ? ColorUtil.ToBrush(labelColor, Colors.White)
            : ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        _accentNormal = ColorUtil.ToBrush(entry?.IndicatorColor ?? WidgetEntry.DefaultIndicatorColor, Colors.DeepSkyBlue);
        TrackBrush = Freeze(new SolidColorBrush(((SolidColorBrush)Foreground).Color) { Opacity = 0.14 });

        CaptionVisibility = _metric.ShowCaption ? Visibility.Visible : Visibility.Collapsed;
        BarVisibility = _metric.Style == MetricStyle.Bar ? Visibility.Visible : Visibility.Collapsed;
        GraphVisibility = _metric.Style == MetricStyle.Graph ? Visibility.Visible : Visibility.Collapsed;
        // Platz für den breitesten Wert reservieren, damit Nachbar-Widgets nicht im Sekundentakt springen.
        ValueMinWidth = _type == WidgetType.Ram && _metric.ShowAmount ? 54 : 38;

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
        var (value, _) = Read();
        if (value is { } v)
        {
            _history.Enqueue(Math.Clamp(v, 0, 100));
            while (_history.Count > HistoryLength) _history.Dequeue();
        }
        Update();
    }

    /// <summary>Aktueller Wert (0–100 bzw. °C) und Grafikkarte (nur GPU/Temperatur).</summary>
    private (double? Value, GpuInfo? Gpu) Read()
    {
        switch (_type)
        {
            case WidgetType.Cpu:
                return (_monitor.CpuUsage, null);
            case WidgetType.Ram:
                return (_monitor.RamUsage, null);
            default:
                var gpu = _monitor.ResolveGpu(_metric.Gpu);
                var reading = gpu is null ? null : _monitor.Gpu.GetValueOrDefault(gpu.CounterKey);
                return (_type == WidgetType.Gpu ? reading?.Usage : reading?.Temperature, gpu);
        }
    }

    private void Update()
    {
        var culture = CultureInfo.CurrentCulture;
        var (value, gpu) = Read();
        bool isTemperature = _type == WidgetType.GpuTemperature;

        ValueText = value is not { } v ? "–"
            : isTemperature ? $"{v:0} °C"
            : _type == WidgetType.Ram && _metric.ShowAmount ? string.Format(culture, "{0:0.0} GB", _monitor.RamUsed / 1073741824.0)
            : $"{v:0} %";

        ToolTipText = _type switch
        {
            WidgetType.Cpu => $"Prozessor: {ValueText}\n{_monitor.CpuName}",
            WidgetType.Ram => string.Format(culture, "Arbeitsspeicher: {0:0.0} von {1:0.0} GB belegt ({2:0} %)",
                _monitor.RamUsed / 1073741824.0, _monitor.RamTotal / 1073741824.0, _monitor.RamUsage ?? 0),
            WidgetType.Gpu => gpu is null ? "Keine Grafikkarte gefunden"
                : string.Format(culture, "Grafikkarte: {0}\n{1}\nVideospeicher: {2:0.0} von {3:0.0} GB", ValueText, gpu.Name,
                    (_monitor.Gpu.GetValueOrDefault(gpu.CounterKey)?.DedicatedUsed ?? 0) / 1073741824.0, gpu.DedicatedMemory / 1073741824.0),
            _ => gpu is null ? "Keine Grafikkarte gefunden"
                : value is null ? $"{gpu.Name}\nDer Treiber meldet keine Temperatur."
                : $"Temperatur: {ValueText}\n{gpu.Name}",
        };

        // Warnfarben: Last ab 80 % / 95 %, Temperatur ab 80 °C / 90 °C.
        var level = value is not { } x || !_metric.WarnColors ? 0
            : x >= (isTemperature ? 90 : 95) ? 2
            : x >= 80 ? 1
            : 0;
        ValueBrush = level switch { 2 => CriticalBrush, 1 => WarnBrush, _ => Foreground };
        AccentBrush = level switch { 2 => CriticalBrush, 1 => WarnBrush, _ => _accentNormal };

        BarWidth = TrackWidth * Math.Clamp((value ?? 0) / 100, 0, 1);
        if (_metric.Style == MetricStyle.Graph) UpdateGraph();
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
