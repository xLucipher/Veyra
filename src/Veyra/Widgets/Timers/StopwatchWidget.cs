using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Timing;

namespace Veyra.Widgets.Timers;

/// <summary>Eine Runde im Popup; schnellste grün, langsamste rot (ab drei Runden).</summary>
public sealed record LapItem(string Number, string LapTime, string Total, Brush Brush);

/// <summary>
/// Stoppuhr: Stunden, Minuten, Sekunden und optional Millisekunden, Bedienung im Popup samt Rundenzeiten. Der Zustand
/// liegt im gemeinsamen <see cref="TimerService"/>, die Zeit läuft also weiter, wenn die Leiste neu aufgebaut oder
/// ausgeblendet wird. Die Anzeige tickt nur, solange die Stoppuhr läuft und sichtbar ist – im Sekundentakt, mit
/// Millisekunden bzw. bei offenem Popup etwa 20-mal je Sekunde.
/// </summary>
public sealed class StopwatchWidget : ObservableObject, IBarWidget, IHotkeyTarget
{
    private static readonly Brush FastestBrush = Units.Freeze(new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F)));
    private static readonly TimeSpan FastInterval = TimeSpan.FromMilliseconds(47);

    private readonly string _entryId;
    private readonly TimerService _service;
    private readonly DispatcherTimer _tick;
    private StopwatchView? _view;
    private StopwatchSettings _stopwatch = new();
    private bool _running;
    private bool _flyoutOpen;

    private string _elapsedText = "";
    private string _flyoutElapsedText = "";
    private string _toolTipText = "";
    private Brush _foreground = Brushes.White;
    private Brush _iconBrush = Brushes.White;
    private FontFamily _fontFamily = new("Segoe UI");

    public StopwatchWidget(string entryId, TimerService service)
    {
        _entryId = entryId;
        _service = service;
        _tick = new DispatcherTimer(DispatcherPriority.Normal);
        _tick.Tick += (_, _) => OnTick();
        StartPauseCommand = new RelayCommand(() => _service.StartPause(_entryId));
        LapCommand = new RelayCommand(() => _service.Lap(_entryId), () => State.Phase == TimerPhase.Running);
        ResetCommand = new RelayCommand(() => _service.ResetStopwatch(_entryId), () => State.Phase != TimerPhase.Idle);
    }

    private StopwatchState State => _service.GetStopwatch(_entryId);

    public FrameworkElement View => _view ??= new StopwatchView { DataContext = this };

    public string ElapsedText { get => _elapsedText; private set => SetProperty(ref _elapsedText, value); }
    public string FlyoutElapsedText { get => _flyoutElapsedText; private set => SetProperty(ref _flyoutElapsedText, value); }
    public string ToolTipText { get => _toolTipText; private set => SetProperty(ref _toolTipText, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public Brush IconBrush { get => _iconBrush; private set => SetProperty(ref _iconBrush, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }

    /// <summary>Pausiert oder zurückgesetzt: Anzeige leicht abgedunkelt.</summary>
    public double TextOpacity => State.Phase == TimerPhase.Running ? 1 : 0.65;

    public ICommand StartPauseCommand { get; }
    public ICommand LapCommand { get; }
    public ICommand ResetCommand { get; }

    public string StartPauseGlyph => State.Phase == TimerPhase.Running ? "" : "";
    public string StartPauseText => State.Phase switch
    {
        TimerPhase.Running => "Pause",
        TimerPhase.Paused => "Weiter",
        _ => "Start",
    };

    public Visibility LapsVisibility => _stopwatch.EnableLaps ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LapListVisibility => _stopwatch.EnableLaps && State.Laps.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public ObservableCollection<LapItem> Laps { get; } = [];

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _stopwatch = entry?.Stopwatch ?? new StopwatchSettings();
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        Foreground = entry?.LabelColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        IconBrush = entry?.IconColor is { } icon ? ColorUtil.ToBrush(icon, Colors.White) : barForeground;
        FontFamily = new FontFamily(bar.Appearance.FontFamily);
        Update();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _service.Changed += OnServiceChanged;
        Update();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _service.Changed -= OnServiceChanged;
        _tick.Stop();
    }

    public void Refresh() => Update();

    public void Dispose()
    {
        if (_view is not null) FlyoutWindow.CloseFor(_view);
        Stop();
    }

    public string EntryId => _entryId;

    /// <summary>Tastenkürzel – funktioniert auch bei ausgeblendeter Leiste, der Zustand liegt im <see cref="TimerService"/>.</summary>
    public void OnHotkey(string action)
    {
        switch (action)
        {
            case WidgetHotkeys.StartPause:
                _service.StartPause(_entryId);
                break;
            case WidgetHotkeys.Lap when _stopwatch.EnableLaps:
                _service.Lap(_entryId);
                break;
            case WidgetHotkeys.Reset:
                _service.ResetStopwatch(_entryId);
                break;
        }
    }

    internal void ToggleFlyout(FrameworkElement anchor)
    {
        var flyout = FlyoutWindow.Toggle(anchor, () => new StopwatchFlyout { DataContext = this });
        if (flyout is null) return;
        _flyoutOpen = true;
        flyout.Closed += (_, _) =>
        {
            _flyoutOpen = false;
            ScheduleTick();
        };
        Update();
    }

    private void OnServiceChanged(object? sender, string entryId)
    {
        if (entryId == _entryId) Update();
    }

    private void Update()
    {
        var state = State;
        UpdateTexts();
        OnPropertyChanged(nameof(TextOpacity));
        OnPropertyChanged(nameof(StartPauseGlyph));
        OnPropertyChanged(nameof(StartPauseText));
        OnPropertyChanged(nameof(LapsVisibility));
        OnPropertyChanged(nameof(LapListVisibility));
        CommandManager.InvalidateRequerySuggested();

        // Runden: schnellste und langsamste markieren, sobald es mindestens drei gibt.
        Laps.Clear();
        if (_stopwatch.EnableLaps && state.Laps.Count > 0)
        {
            var fastest = state.Laps.Count >= 3 ? state.Laps.MinBy(l => l.LapTime) : null;
            var slowest = state.Laps.Count >= 3 ? state.Laps.MaxBy(l => l.LapTime) : null;
            foreach (var lap in state.Laps)
            {
                var brush = lap == fastest ? FastestBrush : lap == slowest ? Units.CriticalBrush : Foreground;
                Laps.Add(new LapItem($"Runde {lap.Number}", Format(lap.LapTime, true, false), Format(lap.Total, true, false), brush));
            }
        }

        string status = state.Phase switch { TimerPhase.Running => "läuft", TimerPhase.Paused => "pausiert", _ => "bereit" };
        ToolTipText = $"Stoppuhr {status}: {Format(state.Elapsed, _stopwatch.ShowMilliseconds, true)}" +
                      (state.Laps.Count > 0 ? $"\n{state.Laps.Count} {(state.Laps.Count == 1 ? "Runde" : "Runden")}" : "") +
                      "\nKlicken: Stoppuhr bedienen";
        ScheduleTick();
    }

    private void UpdateTexts()
    {
        var elapsed = State.Elapsed;
        ElapsedText = Format(elapsed, _stopwatch.ShowMilliseconds, _stopwatch.AlwaysShowHours);
        if (_flyoutOpen) FlyoutElapsedText = Format(elapsed, true, true);
    }

    private void OnTick()
    {
        _tick.Stop();
        UpdateTexts();
        ScheduleTick();
    }

    private void ScheduleTick()
    {
        _tick.Stop();
        if (!_running || State.Phase != TimerPhase.Running) return;
        if (_stopwatch.ShowMilliseconds || _flyoutOpen)
        {
            _tick.Interval = FastInterval;
        }
        else
        {
            // Genau zum nächsten Sekundenwechsel der angezeigten Zeit.
            long fraction = State.Elapsed.Ticks % TimeSpan.TicksPerSecond;
            _tick.Interval = TimeSpan.FromTicks(TimeSpan.TicksPerSecond - fraction) + TimeSpan.FromMilliseconds(5);
        }
        _tick.Start();
    }

    /// <summary>"05:12", "1:05:12", mit Millisekunden "05:12,345".</summary>
    private static string Format(TimeSpan value, bool milliseconds, bool alwaysHours)
    {
        long hours = (long)value.TotalHours;
        string text = hours > 0 || alwaysHours
            ? $"{hours}:{value.Minutes:00}:{value.Seconds:00}"
            : $"{value.Minutes:00}:{value.Seconds:00}";
        return milliseconds ? text + CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator + value.Milliseconds.ToString("000") : text;
    }
}
