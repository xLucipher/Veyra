using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Network;

namespace Veyra.Widgets.Network;

/// <summary>
/// Upload- und Downloadgeschwindigkeit in Echtzeit mit automatischer Einheit, wahlweise mit Verlaufsgrafik.
/// Die Byte-Zähler liest der gemeinsame <see cref="NetworkTrafficMonitor"/>; jedes Widget rechnet die Rate in
/// seinem eigenen Intervall aus zwei Messungen. Angehalten (Leiste bei Vollbild ausgeblendet) wird nichts gemessen.
/// </summary>
public sealed class TrafficWidget : ObservableObject, IBarWidget
{
    private const int HistoryLength = 40;
    private const double GraphHeight = 18;
    private const double GraphWidth = 52;

    private readonly string _entryId;
    private readonly NetworkTrafficMonitor _monitor;
    private readonly Queue<(double Down, double Up)> _history = new();
    private TrafficView? _view;
    private TrafficSettings _traffic = new();
    private bool _running;
    private int _acquiredInterval;

    private TrafficCounters? _lastCounters;
    private long _lastTimestamp;
    private double _down, _up, _peakDown, _peakUp;

    private string _downText = "–";
    private string _upText = "–";
    private string _toolTipText = "";
    private Brush _foreground = Brushes.White;
    private Brush _downBrush = Brushes.White;
    private Brush _upBrush = Brushes.White;
    private Brush _trackBrush = Brushes.Transparent;
    private FontFamily _fontFamily = new("Segoe UI");
    private Orientation _orientation = Orientation.Horizontal;
    private Visibility _downVisibility = Visibility.Visible;
    private Visibility _upVisibility = Visibility.Visible;
    private Visibility _graphVisibility = Visibility.Collapsed;
    private Thickness _upMargin = new(10, 0, 0, 0);
    private double _valueMinWidth = 62;
    private PointCollection _downArea = [];
    private PointCollection _upLine = [];

    public TrafficWidget(string entryId, NetworkTrafficMonitor monitor)
    {
        _entryId = entryId;
        _monitor = monitor;
    }

    public FrameworkElement View => _view ??= new TrafficView { DataContext = this };

    public string DownText { get => _downText; private set => SetProperty(ref _downText, value); }
    public string UpText { get => _upText; private set => SetProperty(ref _upText, value); }
    public string ToolTipText { get => _toolTipText; private set => SetProperty(ref _toolTipText, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public Brush DownBrush { get => _downBrush; private set => SetProperty(ref _downBrush, value); }
    public Brush UpBrush { get => _upBrush; private set => SetProperty(ref _upBrush, value); }
    public Brush TrackBrush { get => _trackBrush; private set => SetProperty(ref _trackBrush, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }
    public Orientation Orientation { get => _orientation; private set => SetProperty(ref _orientation, value); }
    public Visibility DownVisibility { get => _downVisibility; private set => SetProperty(ref _downVisibility, value); }
    public Visibility UpVisibility { get => _upVisibility; private set => SetProperty(ref _upVisibility, value); }
    public Visibility GraphVisibility { get => _graphVisibility; private set => SetProperty(ref _graphVisibility, value); }
    public Thickness UpMargin { get => _upMargin; private set => SetProperty(ref _upMargin, value); }
    public double ValueMinWidth { get => _valueMinWidth; private set => SetProperty(ref _valueMinWidth, value); }
    public PointCollection DownArea { get => _downArea; private set => SetProperty(ref _downArea, value); }
    public PointCollection UpLine { get => _upLine; private set => SetProperty(ref _upLine, value); }
    public double GraphWidthValue => GraphWidth;
    public double GraphHeightValue => GraphHeight;

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        var previous = _traffic;
        _traffic = entry?.Traffic ?? new TrafficSettings();

        FontFamily = new FontFamily(bar.Appearance.FontFamily);
        Foreground = entry?.LabelColor is { } label
            ? ColorUtil.ToBrush(label, Colors.White)
            : ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        // Download = Akzentfarbe (wie Balken der Mess-Widgets), Upload = Symbolfarbe des Eintrags.
        DownBrush = ColorUtil.ToBrush(entry?.IndicatorColor ?? WidgetEntry.DefaultIndicatorColor, Colors.DeepSkyBlue);
        UpBrush = ColorUtil.ToBrush(entry?.IconColor ?? WidgetEntry.DefaultUploadColor, Colors.Orange);
        TrackBrush = Units.Freeze(new SolidColorBrush(((SolidColorBrush)Foreground).Color) { Opacity = 0.14 });

        Orientation = _traffic.Stacked ? Orientation.Vertical : Orientation.Horizontal;
        UpMargin = _traffic.Stacked ? new Thickness(0, -2, 0, 0) : new Thickness(10, 0, 0, 0);
        DownVisibility = _traffic.Direction != TrafficDirection.Upload ? Visibility.Visible : Visibility.Collapsed;
        UpVisibility = _traffic.Direction != TrafficDirection.Download ? Visibility.Visible : Visibility.Collapsed;
        GraphVisibility = _traffic.ShowGraph ? Visibility.Visible : Visibility.Collapsed;
        // Platz für den breitesten Wert reservieren, damit Nachbar-Widgets nicht springen.
        ValueMinWidth = _traffic.UseBits ? 72 : 64;

        if (previous.Adapter != _traffic.Adapter || previous.UseBits != _traffic.UseBits)
        {
            // Anderer Adapter: Zähler passen nicht mehr zusammen → neu beginnen.
            _lastCounters = null;
            _history.Clear();
            _peakDown = _peakUp = 0;
        }
        if (_running && _acquiredInterval != _traffic.IntervalMs)
        {
            _monitor.Release(_acquiredInterval);
            _acquiredInterval = _traffic.IntervalMs;
            _monitor.Acquire(_acquiredInterval);
        }
        UpdateTexts();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _lastCounters = null; // Lücke während der Pause nicht als eine riesige Rate werten
        _monitor.Sampled += OnSampled;
        _acquiredInterval = _traffic.IntervalMs;
        _monitor.Acquire(_acquiredInterval);
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _monitor.Sampled -= OnSampled;
        _monitor.Release(_acquiredInterval);
    }

    public void Refresh() => UpdateTexts();

    public void Dispose() => Stop();

    private void OnSampled(object? sender, EventArgs e)
    {
        long now = _monitor.Timestamp;
        var counters = _monitor.Get(_traffic.Adapter);
        if (counters is not { } current)
        {
            // Gewählter Adapter fehlt bzw. ist nicht verbunden.
            _lastCounters = null;
            _down = _up = 0;
            UpdateTexts(missing: true);
            return;
        }

        if (_lastCounters is not { } last)
        {
            _lastCounters = current;
            _lastTimestamp = now;
            return;
        }

        double seconds = Stopwatch.GetElapsedTime(_lastTimestamp, now).TotalSeconds;
        // Eigenes Intervall abwarten (kleine Toleranz, weil der gemeinsame Takt leicht schwankt).
        if (seconds < _traffic.IntervalMs / 1000.0 * 0.9 || seconds <= 0) return;

        // Zähler können beim Wiederverbinden zurückspringen – dann diese Messung auslassen.
        _down = current.Received >= last.Received ? (current.Received - last.Received) / seconds : 0;
        _up = current.Sent >= last.Sent ? (current.Sent - last.Sent) / seconds : 0;
        _lastCounters = current;
        _lastTimestamp = now;

        _history.Enqueue((_down, _up));
        while (_history.Count > HistoryLength) _history.Dequeue();
        _peakDown = _history.Max(h => h.Down);
        _peakUp = _history.Max(h => h.Up);

        UpdateTexts();
        if (_traffic.ShowGraph) UpdateGraph();
    }

    private void UpdateTexts(bool missing = false)
    {
        bool bits = _traffic.UseBits;
        DownText = missing ? "–" : Units.FormatRate(_down, bits);
        UpText = missing ? "–" : Units.FormatRate(_up, bits);

        string adapter = _traffic.Adapter.Length == 0 ? "Alle aktiven Netzwerkadapter"
            : _traffic.AdapterName.Length > 0 ? _traffic.AdapterName : "Gewählter Adapter";
        ToolTipText = missing
            ? $"{adapter}\nNicht verbunden"
            : $"{adapter}\nDownload: {DownText} (Spitze {Units.FormatRate(_peakDown, bits)})\nUpload: {UpText} (Spitze {Units.FormatRate(_peakUp, bits)})";
    }

    /// <summary>Download als Fläche, Upload als Linie – gemeinsam skaliert auf den höchsten Wert im Verlauf.</summary>
    private void UpdateGraph()
    {
        bool down = _traffic.Direction != TrafficDirection.Upload, up = _traffic.Direction != TrafficDirection.Download;
        double max = Math.Max(1024, _history.Max(h => Math.Max(down ? h.Down : 0, up ? h.Up : 0)));
        double step = GraphWidth / (HistoryLength - 1);
        double x0 = GraphWidth - (_history.Count - 1) * step;

        var area = new PointCollection(HistoryLength + 2);
        var line = new PointCollection(HistoryLength);
        double x = x0;
        if (down) area.Add(new Point(x0, GraphHeight));
        foreach (var (d, u) in _history)
        {
            if (down) area.Add(new Point(x, Y(d)));
            if (up) line.Add(new Point(x, Y(u)));
            x += step;
        }
        if (down) area.Add(new Point(GraphWidth, GraphHeight));
        area.Freeze();
        line.Freeze();
        DownArea = area;
        UpLine = line;

        double Y(double value) => GraphHeight - value / max * (GraphHeight - 2) - 1;
    }
}
