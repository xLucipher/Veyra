using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Timing;

namespace Veyra.Widgets.Timers;

/// <summary>Symbole der Timer- und Stoppuhr-Widgets (24×24-Raster im Linienstil der Audio-Symbole).</summary>
public static class TimerIcons
{
    public const string HourglassData =
        "M6,3 H18 M6,21 H18 " +
        "M7.5,3 V6.5 C7.5,9.5 12,10.5 12,12 C12,13.5 7.5,14.5 7.5,17.5 V21 " +
        "M16.5,3 V6.5 C16.5,9.5 12,10.5 12,12 C12,13.5 16.5,14.5 16.5,17.5 V21 " +
        "M9.5,19 C10.5,17.6 13.5,17.6 14.5,19";

    public static readonly Geometry Hourglass = Freeze(Geometry.Parse(HourglassData));

    private static Geometry Freeze(Geometry geometry)
    {
        geometry.Freeze();
        return geometry;
    }
}

/// <summary>Ein Timer – gemeinsam für die Anzeige auf der Leiste und die Zeile im Popup.</summary>
public sealed class TimerItem : ObservableObject
{
    private readonly TimerWidget _owner;
    private string _remainingText = "";
    private double _remainingFraction = 1;
    private Brush _textBrush = Brushes.White;

    internal TimerItem(TimerWidget owner, CountdownState state)
    {
        _owner = owner;
        State = state;
        PrimaryCommand = new RelayCommand(() => owner.Primary(this));
        ResetCommand = new RelayCommand(() => owner.Reset(this), () => State.Phase != TimerPhase.Idle);
        RemoveCommand = new RelayCommand(() => owner.Remove(this));
    }

    public CountdownState State { get; }
    public string Id => State.TimerId;

    public string Name
    {
        get => State.Name;
        set => _owner.Rename(this, value);
    }

    /// <summary>Dauer als Text ("5:00", "1:30:00"); Eingabe auch als "90", "1h 30m" usw.</summary>
    public string DurationText
    {
        get => TimerService.FormatRemaining(State.Duration);
        set => _owner.ChangeDuration(this, value);
    }

    public string RemainingText { get => _remainingText; private set => SetProperty(ref _remainingText, value); }

    /// <summary>Verbleibender Anteil 1..0 – der Balken unter der Restzeit läuft leer.</summary>
    public double RemainingFraction { get => _remainingFraction; private set => SetProperty(ref _remainingFraction, value); }

    public Brush TextBrush { get => _textBrush; private set => SetProperty(ref _textBrush, value); }

    public bool IsEditable => State.Phase == TimerPhase.Idle;
    public Visibility EditVisibility => IsEditable ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NameVisibility => _owner.ShowNames ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ProgressVisibility => _owner.ShowProgress && State.Phase != TimerPhase.Idle ? Visibility.Visible : Visibility.Collapsed;

    public string PrimaryGlyph => State.Phase == TimerPhase.Running ? "" : ""; // Pause / Wiedergabe
    public string PrimaryToolTip => State.Phase switch
    {
        TimerPhase.Running => "Pausieren",
        TimerPhase.Paused => "Fortsetzen",
        TimerPhase.Finished => "Neu starten",
        _ => "Starten",
    };

    public string StatusText => State.Phase switch
    {
        TimerPhase.Running => "Läuft",
        TimerPhase.Paused => "Pausiert",
        TimerPhase.Finished => "Abgelaufen",
        _ => TimerService.FormatDuration(State.Duration),
    };

    public ICommand PrimaryCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand RemoveCommand { get; }

    /// <summary>Restzeit neu berechnen (sekündlich, solange der Timer läuft).</summary>
    internal void Tick(Brush normal)
    {
        RemainingText = TimerService.FormatRemaining(State.Remaining);
        RemainingFraction = 1 - State.Progress;
        TextBrush = State.Phase == TimerPhase.Finished ? Units.CriticalBrush : State.Phase == TimerPhase.Paused ? WithOpacity(normal, 0.6) : normal;
    }

    /// <summary>Phase, Name oder Dauer geändert.</summary>
    internal void Refresh(Brush normal)
    {
        Tick(normal);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(EditVisibility));
        OnPropertyChanged(nameof(NameVisibility));
        OnPropertyChanged(nameof(ProgressVisibility));
        OnPropertyChanged(nameof(PrimaryGlyph));
        OnPropertyChanged(nameof(PrimaryToolTip));
        OnPropertyChanged(nameof(StatusText));
    }

    private static Brush WithOpacity(Brush brush, double opacity) =>
        brush is SolidColorBrush solid ? Units.Freeze(new SolidColorBrush(solid.Color) { Opacity = opacity }) : brush;
}

/// <summary>
/// Beliebig viele Timer mit eigener Dauer. Auf der Leiste steht die Restzeit des nächsten (oder aller aktiven) Timer,
/// das Popup startet, pausiert, setzt zurück und bearbeitet. Ablauf und Benachrichtigung übernimmt der gemeinsame
/// <see cref="TimerService"/> – auch während die Leiste ausgeblendet ist. Die Anzeige tickt nur, solange ein Timer
/// läuft und das Widget sichtbar ist, und zwar genau zum Sekundenwechsel der Restzeit.
/// </summary>
public sealed class TimerWidget : ObservableObject, IBarWidget, IHotkeyTarget
{
    private readonly string _entryId;
    private readonly TimerService _service;
    private readonly Action<string, Action<WidgetEntry>> _updateEntry;
    private readonly DispatcherTimer _tick;
    private TimerView? _view;
    private TimerSettings _timer = new();
    private bool _running;

    private Brush _foreground = Brushes.White;
    private Brush _iconBrush = Brushes.White;
    private Brush _nameBrush = Brushes.White;
    private Brush _accentBrush = Brushes.White;
    private Brush _trackBrush = Brushes.Transparent;
    private FontFamily _fontFamily = new("Segoe UI");
    private string _toolTipText = "";

    /// <param name="updateEntry">Ändert den Widget-Eintrag in der gespeicherten Konfiguration (Timer im Popup bearbeitet).</param>
    public TimerWidget(string entryId, TimerService service, Action<string, Action<WidgetEntry>> updateEntry)
    {
        _entryId = entryId;
        _service = service;
        _updateEntry = updateEntry;
        _tick = new DispatcherTimer(DispatcherPriority.Normal);
        _tick.Tick += (_, _) => OnTick();
        AddTimerCommand = new RelayCommand(AddTimer);
    }

    public FrameworkElement View => _view ??= new TimerView { DataContext = this };

    /// <summary>Alle Timer (Popup).</summary>
    public ObservableCollection<TimerItem> Items { get; } = [];

    /// <summary>Auf der Leiste angezeigte Timer.</summary>
    public ObservableCollection<TimerItem> BarItems { get; } = [];

    public ICommand AddTimerCommand { get; }
    public Geometry IconGeometry => TimerIcons.Hourglass;

    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public Brush IconBrush { get => _iconBrush; private set => SetProperty(ref _iconBrush, value); }

    /// <summary>Timername auf der Leiste: Schriftfarbe der Leiste, nicht die Wertfarbe der Restzeit.</summary>
    public Brush NameBrush { get => _nameBrush; private set => SetProperty(ref _nameBrush, value); }
    public Brush AccentBrush { get => _accentBrush; private set => SetProperty(ref _accentBrush, value); }
    public Brush TrackBrush { get => _trackBrush; private set => SetProperty(ref _trackBrush, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }
    public string ToolTipText { get => _toolTipText; private set => SetProperty(ref _toolTipText, value); }
    public Visibility NoTimersVisibility => Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    internal bool ShowNames => _timer.ShowName;
    internal bool ShowProgress => _timer.ShowProgress;

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _timer = entry?.Timer ?? new TimerSettings();

        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        Foreground = entry?.LabelColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        IconBrush = entry?.IconColor is { } icon ? ColorUtil.ToBrush(icon, Colors.White) : barForeground;
        NameBrush = barForeground;
        AccentBrush = ColorUtil.ToBrush(entry?.IndicatorColor ?? WidgetEntry.DefaultIndicatorColor, Colors.DeepSkyBlue);
        // Hintergrundspur aus der Leistenfarbe, nicht aus der Wertfarbe – sonst färbt sie sich mit.
        TrackBrush = Units.Freeze(new SolidColorBrush(((SolidColorBrush)barForeground).Color) { Opacity = 0.14 });
        FontFamily = new FontFamily(bar.Appearance.FontFamily);

        SyncItems();
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

    /// <summary>
    /// Klick auf das Widget: Ist ein Timer abgelaufen, wird der Alarm quittiert (zurückgesetzt); sonst öffnet bzw.
    /// schließt sich das Popup.
    /// </summary>
    internal void OnClick(FrameworkElement anchor)
    {
        var finished = Items.Where(i => i.State.Phase == TimerPhase.Finished).ToList();
        if (finished.Count > 0)
        {
            foreach (var item in finished) _service.Reset(item.State);
            return;
        }
        FlyoutWindow.Toggle(anchor, () => new TimerFlyout { DataContext = this });
    }

    public string EntryId => _entryId;

    /// <summary>Tastenkürzel – funktioniert auch bei ausgeblendeter Leiste, der Zustand liegt im <see cref="TimerService"/>.</summary>
    public void OnHotkey(string action)
    {
        var finished = Items.Where(i => i.State.Phase == TimerPhase.Finished).ToList();
        switch (action)
        {
            case WidgetHotkeys.StartPause when finished.Count > 0:
                foreach (var item in finished) _service.Reset(item.State); // Alarm beenden
                break;
            case WidgetHotkeys.StartPause:
                // Der Timer, der auf der Leiste vorne steht (läuft vor pausiert); ohne aktiven Timer der erste.
                var target = Items.Where(i => i.State.IsActive)
                    .OrderBy(i => i.State.Phase == TimerPhase.Running ? 0 : 1)
                    .ThenBy(i => i.State.Remaining)
                    .FirstOrDefault() ?? Items.FirstOrDefault();
                if (target is not null) Primary(target);
                break;
            case WidgetHotkeys.Reset:
                foreach (var item in Items.Where(i => i.State.IsActive).ToList()) _service.Reset(item.State);
                break;
        }
    }

    // ---- Bedienung (Popup) ---------------------------------------------------------------------

    internal void Primary(TimerItem item)
    {
        switch (item.State.Phase)
        {
            case TimerPhase.Running:
                _service.Pause(item.State);
                break;
            case TimerPhase.Finished:
                _service.Reset(item.State);
                _service.Start(item.State);
                break;
            default:
                _service.Start(item.State);
                break;
        }
    }

    internal void Reset(TimerItem item) => _service.Reset(item.State);

    internal void Rename(TimerItem item, string? name)
    {
        name = name?.Trim() ?? "";
        if (name.Length == 0 || name == item.State.Name) return;
        UpdatePreset(item.Id, preset => preset.Name = name);
    }

    internal void ChangeDuration(TimerItem item, string? text)
    {
        if (!TimerService.TryParseDuration(text, out var duration) || duration == item.State.Duration)
        {
            item.Refresh(Foreground); // ungültige Eingabe → alten Wert wieder anzeigen
            return;
        }
        _service.SetDuration(item.State, duration);
        UpdatePreset(item.Id, preset => preset.DurationSeconds = (int)duration.TotalSeconds);
    }

    internal void Remove(TimerItem item)
    {
        _updateEntry(_entryId, entry => entry.Timer?.Timers.RemoveAll(t => t.Id == item.Id));
    }

    private void AddTimer()
    {
        int number = Items.Count + 1;
        while (Items.Any(i => i.Name == $"Timer {number}")) number++;
        var last = Items.LastOrDefault()?.State.Duration ?? TimeSpan.FromMinutes(5);
        _updateEntry(_entryId, entry =>
        {
            entry.Timer ??= new TimerSettings { Timers = [] };
            entry.Timer.Timers.Add(new TimerPreset { Name = $"Timer {number}", DurationSeconds = (int)last.TotalSeconds });
        });
    }

    private void UpdatePreset(string timerId, Action<TimerPreset> change) =>
        _updateEntry(_entryId, entry =>
        {
            var preset = entry.Timer?.Timers.FirstOrDefault(t => t.Id == timerId);
            if (preset is not null) change(preset);
        });

    // ---- Anzeige ---------------------------------------------------------------------------------

    /// <summary>Zeilen an die Timer der Einstellungen anpassen; bestehende Zeilen bleiben erhalten (Eingabefokus im Popup).</summary>
    private void SyncItems()
    {
        var states = _service.Configure(_entryId, _timer);
        for (int i = 0; i < states.Count; i++)
        {
            int existing = IndexOf(Items, states[i].TimerId);
            if (existing == i) continue;
            if (existing > i) Items.Move(existing, i);
            else Items.Insert(i, new TimerItem(this, states[i]));
        }
        while (Items.Count > states.Count) Items.RemoveAt(Items.Count - 1);
        OnPropertyChanged(nameof(NoTimersVisibility));
        Update();
    }

    private static int IndexOf(ObservableCollection<TimerItem> items, string id)
    {
        for (int i = 0; i < items.Count; i++)
            if (items[i].Id == id) return i;
        return -1;
    }

    private void OnServiceChanged(object? sender, string entryId)
    {
        if (entryId == _entryId) Update();
    }

    private void Update()
    {
        foreach (var item in Items) item.Refresh(Foreground);

        // Abgelaufene zuerst (Alarm), dann laufende nach Restzeit, dann pausierte.
        var active = Items.Where(i => i.State.IsActive)
            .OrderBy(i => i.State.Phase switch { TimerPhase.Finished => 0, TimerPhase.Running => 1, _ => 2 })
            .ThenBy(i => i.State.Remaining)
            .ToList();
        if (!_timer.ShowAllActive && active.Count > 1) active = active.Take(1).ToList();
        if (!BarItems.SequenceEqual(active))
        {
            BarItems.Clear();
            foreach (var item in active) BarItems.Add(item);
        }

        List<string> tip = Items.Count == 0
            ? ["Keine Timer – im Popup oder in den Einstellungen anlegen"]
            : Items.Select(i => $"{i.Name}: {(i.State.Phase == TimerPhase.Idle ? TimerService.FormatDuration(i.State.Duration) : $"{i.RemainingText} ({i.StatusText})")}").ToList();
        tip.Add(Items.Any(i => i.State.Phase == TimerPhase.Finished) ? "Klicken: Alarm beenden" : "Klicken: Timer bedienen");
        ToolTipText = string.Join("\n", tip);

        ScheduleTick();
    }

    private void OnTick()
    {
        _tick.Stop();
        foreach (var item in Items.Where(i => i.State.Phase == TimerPhase.Running)) item.Tick(Foreground);
        ScheduleTick();
    }

    /// <summary>Nächster Tick genau dann, wenn sich die angezeigte Sekunde eines laufenden Timers ändert.</summary>
    private void ScheduleTick()
    {
        _tick.Stop();
        if (!_running) return;
        var running = Items.Where(i => i.State.Phase == TimerPhase.Running).ToList();
        if (running.Count == 0) return;
        long next = running.Min(i =>
        {
            long fraction = i.State.Remaining.Ticks % TimeSpan.TicksPerSecond;
            return fraction == 0 ? TimeSpan.TicksPerSecond : fraction;
        });
        _tick.Interval = TimeSpan.FromTicks(next) + TimeSpan.FromMilliseconds(10);
        _tick.Start();
    }
}
