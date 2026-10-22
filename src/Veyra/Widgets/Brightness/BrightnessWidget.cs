using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Monitors;
using Veyra.Widgets.Volume;

namespace Veyra.Widgets.Brightness;

/// <summary>Regler eines Bildschirms im Popup.</summary>
public sealed class ScreenRow(BrightnessService service, ScreenBrightness screen) : ObservableObject
{
    private int _percent = screen.Percent;

    public string Id { get; } = screen.Id;
    public string Name { get; } = screen.Name;

    public int Percent
    {
        get => _percent;
        set
        {
            if (!SetProperty(ref _percent, value)) return;
            OnPropertyChanged(nameof(PercentText));
            service.Set(Id, value);
        }
    }

    public string PercentText => $"{_percent} %";

    /// <summary>Wert von außen (Mausrad, Tastenkürzel) übernehmen, ohne ihn erneut zu senden.</summary>
    internal void Sync(int percent)
    {
        if (_percent == percent) return;
        _percent = percent;
        OnPropertyChanged(nameof(Percent));
        OnPropertyChanged(nameof(PercentText));
    }
}

/// <summary>
/// Helligkeit und Nachtmodus: Symbol mit Helligkeit (Durchschnitt aller Bildschirme), Mausrad ändert alle Bildschirme,
/// Klick öffnet je Bildschirm einen Regler sowie den Nachtmodus. Der Nachtmodus läuft auch weiter, während die Fläche
/// bei Vollbild angehalten ist, und folgt auf Wunsch einem Zeitplan (z. B. 21:00–07:00).
/// </summary>
public sealed class BrightnessWidget : ObservableObject, IBarWidget, IHotkeyTarget
{
    private readonly BrightnessService _service;
    private readonly Action<string, Action<WidgetEntry>> _updateEntry;
    private readonly Border _view;
    private readonly TextBlock _icon;
    private readonly TextBlock _percent;
    private readonly DispatcherTimer _nightTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private BrightnessSettings _brightness = new();
    private Brush _iconBrush = Brushes.White;
    private BrightnessFlyout? _flyout;
    private bool _running;
    private bool _acquired;
    private int _wheelRemainder;
    private int _reapplyTicks;
    private bool _nightActive;

    /// <summary>Im Zeitplan von Hand ausgeschaltet – gilt bis zum Ende des Zeitfensters.</summary>
    private bool _scheduleOverride;

    private static readonly Brush NightBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x5C)));

    public BrightnessWidget(string entryId, BrightnessService service, Action<string, Action<WidgetEntry>> updateEntry)
    {
        EntryId = entryId;
        _service = service;
        _updateEntry = updateEntry;
        _icon = new TextBlock { FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        _icon.SetResourceReference(TextBlock.FontFamilyProperty, "WidgetIconFont");
        _percent = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        Typography.SetNumeralAlignment(_percent, FontNumeralAlignment.Tabular);
        _view = new Border { Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _icon, _percent } } };
        _view.SetResourceReference(FrameworkElement.StyleProperty, "WidgetHoverArea");
        _view.MouseLeftButtonUp += (_, _) => ToggleFlyout();
        _view.MouseWheel += (_, e) =>
        {
            OnWheel(e.Delta);
            e.Handled = true;
        };
        _nightTimer.Tick += (_, _) => OnNightTimer();
        ToggleNightCommand = new RelayCommand(ToggleNight);
        OpenWindowsNightLightCommand = new RelayCommand(() => _flyout?.OpenAndClose("ms-settings:nightlight"));
        RescanCommand = new RelayCommand(_service.Rescan);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
    }

    public string EntryId { get; }
    public FrameworkElement View => _view;

    // ---- Popup ------------------------------------------------------------------------------
    public System.Collections.ObjectModel.ObservableCollection<ScreenRow> Rows { get; } = [];
    public ICommand ToggleNightCommand { get; }
    public ICommand OpenWindowsNightLightCommand { get; }
    public ICommand RescanCommand { get; }

    public Visibility NoScreensVisibility => _service.HasRead && _service.Screens.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool NightActive => _nightActive;

    public string NightButtonText => _nightActive ? "Nachtmodus aus" : "Nachtmodus ein";

    public string NightStatus
    {
        get
        {
            string schedule = _brightness.NightSchedule ? $"Zeitplan {_brightness.NightFrom}–{_brightness.NightTo}" : "";
            if (!_nightActive) return schedule.Length > 0 ? $"Aus · {schedule}" : "Aus";
            string temperature = NightLight.EffectiveTemperature is { } k && k != _brightness.NightTemperature
                ? $"{k} K (wärmer lässt Windows nicht zu)"
                : $"{_brightness.NightTemperature} K";
            return schedule.Length > 0 ? $"An · {temperature} · {schedule}" : $"An · {temperature}";
        }
    }

    public int NightTemperature
    {
        get => _brightness.NightTemperature;
        set
        {
            value = Math.Clamp(value / 100 * 100, BrightnessSettings.MinTemperature, BrightnessSettings.NeutralTemperature);
            if (value == _brightness.NightTemperature) return;
            _brightness.NightTemperature = value;
            ApplyNight(); // sofort sehen, gespeichert wird beim Schließen
            OnPropertyChanged();
            OnPropertyChanged(nameof(NightStatus));
        }
    }

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == EntryId);
        _brightness = entry?.Brightness?.Clone() ?? new BrightnessSettings();
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        _iconBrush = entry?.IconColor is { } icon ? ColorUtil.ToBrush(icon, Colors.White) : barForeground;
        _percent.Foreground = entry?.LabelColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        _percent.FontFamily = new FontFamily(bar.Appearance.FontFamily);
        // Bildschirme bleiben angefordert, solange das Widget existiert: Gelesen wird nur auf Anlass (kein Abfragen),
        // und Tastenkürzel wirken so auch, während die Fläche angehalten ist.
        if (!_acquired) _service.Acquire(this);
        _acquired = true;
        ApplyNight();
        Update();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _service.Changed += OnScreensChanged;
        Update();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _service.Changed -= OnScreensChanged;
        // Der Nachtmodus bleibt – auch im Spiel soll das Licht warm bleiben.
    }

    public void Refresh() => ApplyNight();

    public void Dispose()
    {
        FlyoutWindow.CloseFor(_view);
        Stop();
        _nightTimer.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
        _service.Release(this);
        NightLight.Request(this, null);
    }

    public void OnHotkey(string action)
    {
        switch (action)
        {
            case WidgetHotkeys.BrightnessUp:
                StepAll(_brightness.WheelStep, overlay: true);
                break;
            case WidgetHotkeys.BrightnessDown:
                StepAll(-_brightness.WheelStep, overlay: true);
                break;
            case WidgetHotkeys.ToggleNightMode:
                ToggleNight();
                VolumeOverlay.Flash(_nightActive ? "" : "", _nightActive ? "Nachtmodus an" : "Nachtmodus aus", alert: false);
                break;
        }
    }

    /// <summary>Mausrad: je Raste um die eingestellte Schrittweite; Feinraster von Touchpads wird aufsummiert.</summary>
    internal void OnWheel(int delta)
    {
        _wheelRemainder += delta;
        int notches = _wheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        if (notches == 0) return;
        _wheelRemainder -= notches * Mouse.MouseWheelDeltaForOneLine;
        StepAll(notches * _brightness.WheelStep, overlay: false);
    }

    private void StepAll(int delta, bool overlay)
    {
        _service.Step(delta);
        if (overlay && Average() is { } average) VolumeOverlay.Flash("", $"Helligkeit {average} %", alert: false);
    }

    private int? Average() => _service.Screens.Count == 0 ? null : (int)Math.Round(_service.Screens.Average(s => s.Percent));

    private void OnScreensChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var average = Average();
        _icon.Text = _nightActive ? "" : "";
        _icon.Foreground = _nightActive ? NightBrush : _iconBrush;
        _percent.Text = average is { } a ? $"{a} %" : _service.HasRead ? "–" : "…";
        _percent.Visibility = _brightness.ShowPercent ? Visibility.Visible : Visibility.Collapsed;
        var lines = _service.Screens.Select(s => $"{s.Name}: {s.Percent} %").ToList();
        if (_service.HasRead && lines.Count == 0) lines.Add("Kein Bildschirm mit regelbarer Helligkeit (DDC/CI)");
        lines.Add(_nightActive ? $"Nachtmodus an ({NightLight.EffectiveTemperature ?? _brightness.NightTemperature} K)" : "Nachtmodus aus");
        lines.Add("Mausrad: Helligkeit · Klicken für Regler und Nachtmodus");
        _view.ToolTip = string.Join("\n", lines);

        // Regler im Popup nachziehen (Mausrad, Tastenkürzel) bzw. bei geänderter Bildschirmliste neu aufbauen.
        if (_flyout is not null)
        {
            if (Rows.Select(r => r.Id).SequenceEqual(_service.Screens.Select(s => s.Id)))
            {
                foreach (var screen in _service.Screens) Rows.First(r => r.Id == screen.Id).Sync(screen.Percent);
            }
            else
            {
                Rows.Clear();
                foreach (var screen in _service.Screens) Rows.Add(new ScreenRow(_service, screen));
            }
        }
        OnPropertyChanged(nameof(NoScreensVisibility));
        OnPropertyChanged(nameof(NightActive));
        OnPropertyChanged(nameof(NightButtonText));
        OnPropertyChanged(nameof(NightStatus));
    }

    // ---- Nachtmodus -------------------------------------------------------------------------

    private bool InSchedule() => _brightness.NightSchedule && _brightness.IsNightTime(DateTime.Now);

    private void ApplyNight()
    {
        bool inSchedule = InSchedule();
        if (!inSchedule) _scheduleOverride = false;
        _nightActive = _brightness.NightMode || (inSchedule && !_scheduleOverride);
        NightLight.Request(this, _nightActive ? _brightness.NightTemperature : null);
        if (_nightActive || _brightness.NightSchedule) _nightTimer.Start();
        else _nightTimer.Stop();
        Update();
    }

    private void OnNightTimer()
    {
        bool wasActive = _nightActive;
        ApplyNight();
        // Etwa alle zwei Minuten erneut setzen, falls Windows die Tabelle zurückgesetzt hat.
        if (_nightActive && wasActive && ++_reapplyTicks % 4 == 0) NightLight.Reapply();
    }

    private void ToggleNight()
    {
        bool turnOn = !_nightActive;
        bool inSchedule = InSchedule();
        _scheduleOverride = !turnOn && inSchedule;
        bool manual = turnOn && !inSchedule; // im Zeitfenster reicht der Zeitplan selbst
        _brightness.NightMode = manual;
        if (turnOn && inSchedule) _scheduleOverride = false;
        ApplyNight();
        _updateEntry(EntryId, entry =>
        {
            entry.Brightness ??= new BrightnessSettings();
            entry.Brightness.NightMode = manual;
        });
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => _view.Dispatcher.BeginInvoke(() =>
    {
        NightLight.Reapply();
        _service.Rescan();
    });

    private void OnSessionSwitch(object? sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        if (e.Reason is Microsoft.Win32.SessionSwitchReason.SessionUnlock or Microsoft.Win32.SessionSwitchReason.ConsoleConnect)
            _view.Dispatcher.BeginInvoke(NightLight.Reapply);
    }

    private void ToggleFlyout()
    {
        Rows.Clear();
        foreach (var screen in _service.Screens) Rows.Add(new ScreenRow(_service, screen));
        int temperatureBefore = _brightness.NightTemperature;
        _flyout = FlyoutWindow.Toggle(_view, () => new BrightnessFlyout { DataContext = this }) as BrightnessFlyout;
        if (_flyout is null) return;
        _flyout.Closed += (_, _) =>
        {
            _flyout = null;
            if (_brightness.NightTemperature == temperatureBefore) return;
            int temperature = _brightness.NightTemperature;
            _updateEntry(EntryId, entry =>
            {
                entry.Brightness ??= new BrightnessSettings();
                entry.Brightness.NightTemperature = temperature;
            });
        };
        _service.Rescan(); // aktuelle Werte, falls am Monitor selbst geändert
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
