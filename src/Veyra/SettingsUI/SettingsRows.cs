using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Veyra.Audio;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.SettingsUI;

public sealed record EnumOption(object Value, string Display);


/// <param name="PathData">Statt eines Schriftzeichens eine eigene 24×24-Kontur (z. B. die Sanduhr des Timers).</param>
public sealed record WidgetInfo(WidgetType Type, string Name, string Description, string Glyph, string? PathData = null);

public static class WidgetCatalog
{
    public static readonly IReadOnlyList<WidgetInfo> All =
    [
        new(WidgetType.Clock, "Uhr", "Feste Position Links, Mitte oder Rechts – Klick öffnet den Kalender", ""),
        new(WidgetType.AudioOutputs, "Wiedergabegeräte", "Buttons zum schnellen Wechsel des Ausgabegeräts", ""),
        new(WidgetType.AudioInputs, "Mikrofone", "Buttons zum schnellen Wechsel des Mikrofons", ""),
        new(WidgetType.Cpu, "CPU-Auslastung", "Prozessor-Auslastung in Prozent", ""),
        new(WidgetType.Gpu, "GPU-Auslastung", "Grafikkarten-Auslastung in Prozent, wie im Task-Manager", ""),
        new(WidgetType.Ram, "Arbeitsspeicher", "Belegter Arbeitsspeicher in Prozent oder GB", ""),
        new(WidgetType.GpuTemperature, "GPU-Temperatur", "Temperatur der Grafikkarte, wie im Task-Manager", ""),
        new(WidgetType.Network, "Netzwerk", "LAN/WLAN mit Status, Netzwerkname und Signal – Klick zeigt IP-Adresse, Adapter und Geschwindigkeit", "\uE701"),
        new(WidgetType.NetworkTraffic, "Upload / Download", "Aktuelle Datenrate in Echtzeit, optional mit Verlauf", "\uE174"),
        new(WidgetType.Volume, "Lautstärke", "Systemlautstärke – Mausrad ändert, Klick öffnet den Regler, Rechtsklick schaltet stumm", "\uE995"),
        new(WidgetType.Timer, "Timer", "Beliebig viele Countdown-Timer mit Benachrichtigung und Signalton", "", Widgets.Timers.TimerIcons.HourglassData),
        new(WidgetType.Stopwatch, "Stoppuhr", "Starten, pausieren, Rundenzeiten – Bedienung im Popup", "\uE916"),
    ];

    public static WidgetInfo Get(WidgetType type) => All.First(w => w.Type == type);

    public static IReadOnlyList<EnumOption> MetricStyles { get; } =
    [
        new(MetricStyle.Value, "Nur Wert"),
        new(MetricStyle.Bar, "Wert und Balken"),
        new(MetricStyle.Graph, "Wert und Verlauf (letzte Minute)"),
    ];

    public static IReadOnlyList<EnumOption> VolumeFlows { get; } =
    [
        new(AudioFlow.Output, "Wiedergabegerät (Lautsprecher, Kopfhörer)"),
        new(AudioFlow.Input, "Mikrofon (Aufnahmepegel)"),
    ];

    public static IReadOnlyList<EnumOption> TimerSounds { get; } =
    [
        new(TimerSound.Alarm, "Wecker"),
        new(TimerSound.Notification, "Benachrichtigung"),
        new(TimerSound.Reminder, "Erinnerung"),
        new(TimerSound.None, "Kein Signalton"),
    ];

    public static IReadOnlyList<EnumOption> TrafficDirections { get; } =
    [
        new(TrafficDirection.Both, "Download und Upload"),
        new(TrafficDirection.Download, "Nur Download"),
        new(TrafficDirection.Upload, "Nur Upload"),
    ];

    public static IReadOnlyList<IntOption> TrafficIntervals { get; } =
    [
        new(250, "0,25 s"),
        new(500, "0,5 s"),
        new(1000, "1 s"),
        new(2000, "2 s"),
        new(3000, "3 s"),
        new(5000, "5 s"),
        new(10000, "10 s"),
    ];

    public static IReadOnlyList<int> WheelSteps { get; } = [1, 2, 5, 10];

    public static IReadOnlyList<EnumOption> Slots { get; } =
    [
        new(WidgetSlot.Left, "Links"),
        new(WidgetSlot.Center, "Mitte"),
        new(WidgetSlot.Right, "Rechts"),
    ];

    public static IReadOnlyList<double> MarginPresets { get; } = [0, 4, 8, 12, 16, 24, 32, 48, 64];

    public static IReadOnlyList<double> SpacingPresets { get; } = [0, 4, 8, 12, 16, 24, 32, 48];

    public static IReadOnlyList<double> ScalePresets { get; } = [50, 75, 100, 125, 150, 200, 250, 300, 400, 500];
}

/// <summary>Ein Gerät mit Häkchen in einem Audio-Widget.</summary>
public sealed class DeviceChoice(string id, string name, string iconKey, bool connected, bool selected) : ObservableObject
{
    private bool _selected = selected;

    public string Id { get; } = id;
    public string Name { get; } = connected ? name : $"{name} (nicht verbunden)";
    public string IconKey { get; } = iconKey;
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
}

/// <summary>
/// Eine Widget-Instanz im Einstellungsfenster. Feste Widgets (Uhr) haben Bereich und Abstände,
/// freie Widgets werden auf der Leiste per Drag &amp; Drop platziert; Audio-Widgets haben eine Geräteauswahl.
/// </summary>
public sealed class WidgetRowViewModel : ObservableObject
{
    private readonly WidgetEntry _entry;
    private WidgetSlot _slot;
    private bool _enabled;
    private double _outerMargin;
    private double _innerSpacing;
    private double _scalePercent;
    private bool _allDevices;
    private string _title;
    private readonly string _barForeground;
    private string _iconColor;
    private string _labelColor;
    private bool _iconColorCustom;
    private bool _labelColorCustom;
    private string _indicatorColor;
    private bool _indicatorColorCustom;
    private string? _hotkey;
    private string _hotkeyWarning = "";
    private readonly Action? _hotkeyChanged;

    /// <param name="barForeground">Schriftfarbe der Leiste – gilt, solange das Widget keine eigene Farbe hat.</param>
    /// <param name="editClock">Öffnet die Seite "Uhr &amp; Kalender" mit dieser Uhr.</param>
    /// <param name="hotkeyChanged">Tastenkürzel geändert – Konflikte neu prüfen.</param>
    public WidgetRowViewModel(WidgetEntry entry, IReadOnlyList<AudioDevice> devices, AudioSettings audio, string barForeground,
        Action<WidgetRowViewModel> remove, Action<WidgetRowViewModel> editClock, Action? hotkeyChanged = null)
    {
        _entry = entry;
        _hotkey = entry.Hotkey;
        _hotkeyChanged = hotkeyChanged;
        Info = WidgetCatalog.Get(entry.Type);
        _title = Info.Name;
        _slot = entry.Slot;
        _enabled = entry.Enabled;
        _outerMargin = entry.OuterMargin;
        _innerSpacing = entry.InnerSpacing;
        _scalePercent = Math.Round(entry.Scale * 100);
        _allDevices = entry.Devices.Count == 0;
        _barForeground = barForeground;
        _iconColorCustom = entry.IconColor is not null;
        _labelColorCustom = entry.LabelColor is not null;
        _iconColor = entry.IconColor ?? DefaultIconColor;
        _labelColor = entry.LabelColor ?? barForeground;
        _indicatorColorCustom = entry.IndicatorColor is not null;
        _indicatorColor = entry.IndicatorColor ?? WidgetEntry.DefaultIndicatorColor;
        // Eigene Kopie: Änderungen auf der Seite "Uhr & Kalender" wirken erst beim Übernehmen.
        Clock = entry.Clock?.Clone() ?? new ClockSettings();
        Metric = entry.Metric?.Clone() ?? new MetricSettings();
        Network = entry.Network?.Clone() ?? new NetworkSettings();
        Volume = entry.Volume?.Clone() ?? new VolumeSettings();
        Timer = entry.Timer?.Clone() ?? new TimerSettings();
        foreach (var preset in Timer.Timers) TimerRows.Add(new TimerPresetRow(preset, RemoveTimerRow));
        _timersSnapshot = TimersSignature(Timer.Timers);
        Stopwatch = entry.Stopwatch?.Clone() ?? new StopwatchSettings();
        Traffic = entry.Traffic?.Clone() ?? new TrafficSettings();
        AddTimerCommand = new RelayCommand(AddTimerRow);
        foreach (var action in WidgetHotkeys.For(entry.Type))
            HotkeyActions.Add(new HotkeyActionRow(action, entry.Hotkeys?.GetValueOrDefault(action.Id), () =>
            {
                _hotkeyChanged?.Invoke();
                OnPropertyChanged(nameof(Summary));
            }));
        RemoveCommand = new RelayCommand(() => remove(this));
        EditClockCommand = new RelayCommand(() => editClock(this));
        ResetColorsCommand = new RelayCommand(() =>
        {
            IconColor = Is(WidgetType.NetworkTraffic) ? WidgetEntry.DefaultUploadColor : _barForeground;
            LabelColor = _barForeground;
            IndicatorColor = WidgetEntry.DefaultIndicatorColor;
            _iconColorCustom = false;
            _labelColorCustom = false;
            _indicatorColorCustom = false;
        });

        if (IsAudio)
        {
            var flow = entry.Type == WidgetType.AudioOutputs ? AudioFlow.Output : AudioFlow.Input;
            foreach (var device in devices)
            {
                Devices.Add(new DeviceChoice(device.Id, device.Name,
                    AudioIcons.Resolve(audio.Find(device.Id)?.Icon, flow, device.FormFactor, device.Name), connected: true,
                    selected: _allDevices || entry.Devices.Contains(device.Id, StringComparer.OrdinalIgnoreCase)));
            }
            // Gewählte, aber gerade getrennte Geräte bleiben in der Auswahl erhalten.
            foreach (var missing in entry.Devices.Where(id => devices.All(d => !d.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
            {
                var name = audio.Find(missing)?.Name;
                Devices.Add(new DeviceChoice(missing, string.IsNullOrEmpty(name) ? "Unbekanntes Gerät" : name,
                    AudioIcons.Resolve(audio.Find(missing)?.Icon, flow, AudioFormFactor.Unknown, name ?? ""), connected: false, selected: true));
            }
        }
    }

    public string Id => _entry.Id;
    public WidgetInfo Info { get; }
    public ICommand RemoveCommand { get; }

    // ---- Ein-/Ausklappen ------------------------------------------------------------------
    private bool _isExpanded;
    private ICommand? _toggleExpandedCommand;

    /// <summary>Einstellungen sichtbar; standardmäßig eingeklappt, damit viele Widgets übersichtlich bleiben.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (!SetProperty(ref _isExpanded, value)) return;
            OnPropertyChanged(nameof(DetailsVisibility));
            OnPropertyChanged(nameof(ChevronGlyph));
            OnPropertyChanged(nameof(ExpandToolTip));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public ICommand ToggleExpandedCommand => _toggleExpandedCommand ??= new RelayCommand(() => IsExpanded = !IsExpanded);
    public Visibility DetailsVisibility => IsExpanded ? Visibility.Visible : Visibility.Collapsed;
    public string ChevronGlyph => IsExpanded ? "" : ""; // ChevronUp / ChevronDown
    public string ExpandToolTip => IsExpanded ? "Einstellungen einklappen" : "Einstellungen ausklappen";

    /// <summary>
    /// Zweite Zeile der Kopfzeile: ausgeklappt die Beschreibung, eingeklappt eine Kurzinfo zum Unterscheiden
    /// (Audio: Geräte und Tastenkürzel, Uhr: Bereich und Zeitformat).
    /// </summary>
    public string Summary
    {
        get
        {
            if (IsExpanded) return Info.Description;
            var parts = new List<string>();
            if (IsAudio)
            {
                var selected = Devices.Where(d => d.Selected).Select(d => d.Name).ToList();
                parts.Add(AllDevices || selected.Count == 0 ? "Alle aktiven Geräte" : string.Join(", ", selected));
                if (Core.Hotkey.TryParse(Hotkey, out var hotkey)) parts.Add(hotkey.DisplayText);
            }
            else if (IsMetric)
            {
                parts.Add(WidgetCatalog.MetricStyles.FirstOrDefault(s => Equals(s.Value, Metric.Style))?.Display ?? "");
                if (_entry.Type == WidgetType.Ram && Metric.ShowAmount) parts.Add("in GB");
                if (GpuChoiceVisibility == Visibility.Visible) parts.Add(Metric.Gpu.Length > 0 ? Metric.Gpu : "Grafikkarte automatisch");
            }
            else if (IsClock)
            {
                parts.Add(WidgetCatalog.Slots.FirstOrDefault(s => Equals(s.Value, Slot))?.Display ?? "");
                parts.Add(Clock.TimeFormat);
            }
            else if (Is(WidgetType.Volume))
            {
                parts.Add(Volume.Flow == AudioFlow.Output ? "Wiedergabegerät" : "Mikrofon");
                if (Volume.ShowPercent) parts.Add("mit Prozent");
            }
            else if (Is(WidgetType.Timer))
            {
                parts.Add(TimerRows.Count == 1 ? "1 Timer" : $"{TimerRows.Count} Timer");
                parts.Add(WidgetCatalog.TimerSounds.FirstOrDefault(s => Equals(s.Value, Timer.Sound))?.Display ?? "");
            }
            else if (Is(WidgetType.Stopwatch))
            {
                if (Stopwatch.ShowMilliseconds) parts.Add("mit Millisekunden");
                if (Stopwatch.EnableLaps) parts.Add("Rundenzeiten");
            }
            else if (Is(WidgetType.NetworkTraffic))
            {
                parts.Add(WidgetCatalog.TrafficDirections.FirstOrDefault(d => Equals(d.Value, Traffic.Direction))?.Display ?? "");
                parts.Add(Traffic.Adapter.Length == 0 ? "alle Adapter" : Traffic.AdapterName.Length > 0 ? Traffic.AdapterName : "ein Adapter");
                if (Traffic.ShowGraph) parts.Add("mit Verlauf");
            }
            else if (Is(WidgetType.Network))
            {
                if (Network.ShowName) parts.Add("mit Netzwerkname");
                if (Network.ShowSignal) parts.Add("mit Signalstärke");
            }
            int hotkeys = HotkeyActions.Count(a => a.Hotkey is not null);
            if (hotkeys > 0) parts.Add(hotkeys == 1 ? "1 Tastenkürzel" : $"{hotkeys} Tastenkürzel");
            if (Math.Abs(ScalePercent - 100) > 0.5) parts.Add($"{ScalePercent:0} %");
            if (!Enabled) parts.Add("inaktiv");
            return string.Join("  ·  ", parts.Where(p => p.Length > 0));
        }
    }
    public ICommand EditClockCommand { get; }
    public ICommand ResetColorsCommand { get; }

    /// <summary>Anzeigename; bei mehreren Uhren auf einer Leiste nummeriert ("Uhr 2").</summary>
    public string Title { get => _title; set => SetProperty(ref _title, value); }

    public override string ToString() => Title; // Name für Auswahllisten und Bildschirmleser

    public bool IsFixed => _entry.IsFixed;
    public bool IsClock => _entry.Type == WidgetType.Clock;
    public bool IsAudio => _entry.Type is WidgetType.AudioOutputs or WidgetType.AudioInputs;
    public Visibility FixedVisibility => IsFixed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FreeVisibility => IsFixed ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AudioVisibility => IsAudio ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ClockVisibility => IsClock ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Nur Uhren: Einstellungen dieser Uhr (wird auf der Seite "Uhr &amp; Kalender" bearbeitet).</summary>
    public ClockSettings Clock { get; }

    // ---- Mess-Widgets (CPU, GPU, RAM, Temperatur) -----------------------------------------
    public bool IsMetric => _entry.IsMetric;
    public Visibility MetricVisibility => IsMetric ? Visibility.Visible : Visibility.Collapsed;

    private bool Is(WidgetType type) => _entry.Type == type;

    /// <summary>Alle Widgets außer der Uhr haben eigene Farben (die Uhr auf der Seite „Uhr &amp; Kalender“).</summary>
    public Visibility ColorsVisibility => IsClock ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Symbolfarbe: alle Widgets mit Symbol; beim Upload-/Download-Monitor die Uploadfarbe, bei Mess-Widgets die Bezeichnung.</summary>
    private bool HasIconColor => !IsClock;

    /// <summary>Akzentfarbe: Markierung, Balken, Fortschritt bzw. Download.</summary>
    private bool HasIndicatorColor => IsAudio || IsMetric || Is(WidgetType.Volume) || Is(WidgetType.Timer) || Is(WidgetType.NetworkTraffic);

    public Visibility IconColorVisibility => HasIconColor ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IndicatorColorVisibility => HasIndicatorColor ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Ohne eigene Symbolfarbe: Schriftfarbe der Leiste, beim Upload die Standard-Uploadfarbe.</summary>
    private string DefaultIconColor => Is(WidgetType.NetworkTraffic) ? WidgetEntry.DefaultUploadColor
        : IsMetric ? _entry.LabelColor ?? _barForeground // Bezeichnung folgt ohne eigene Farbe dem Wert
        : _barForeground;

    public string IconColorTitle => Is(WidgetType.NetworkTraffic) ? "Uploadfarbe" : IsMetric ? "Bezeichnungsfarbe" : "Symbolfarbe";

    public string LabelColorTitle => IsMetric ? "Wertfarbe" : "Schriftfarbe";

    public string IconColorHint => _entry.Type switch
    {
        WidgetType.NetworkTraffic => "Pfeil und Linie im Verlauf.",
        WidgetType.Network => "Bei Verbindung ohne Internetzugriff wird das Symbol orange.",
        _ when IsMetric => _entry.Type == WidgetType.GpuTemperature ? "Für das Thermometer vor dem Wert." : "Für „CPU“, „GPU“ bzw. „RAM“ vor dem Wert. Ohne eigene Farbe wie die Wertfarbe.",
        _ => "",
    };

    public Visibility IconColorHintVisibility => IconColorHint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Schriftfarbe: Audio = Gerätenamen, sonst Wert bzw. Beschriftung.</summary>
    public string LabelColorHint => _entry.Type switch
    {
        WidgetType.AudioOutputs or WidgetType.AudioInputs => "Für die Gerätenamen (Audiogeräte → Beschriftung).",
        WidgetType.Network => "Für Netzwerkname und Signalstärke.",
        WidgetType.Volume => "Für den Prozentwert.",
        WidgetType.Timer => "Für Restzeit und Namen. Abgelaufene Timer werden rot.",
        WidgetType.Stopwatch => "Für die Zeit.",
        WidgetType.NetworkTraffic => "Für die Datenraten.",
        _ => "Für den Wert, z. B. „42 %“. Bei hoher Last gelten die Warnfarben.",
    };

    public string IndicatorColorTitle => _entry.Type switch
    {
        WidgetType.AudioOutputs or WidgetType.AudioInputs => "Markierungsfarbe",
        WidgetType.Volume => "Balkenfarbe",
        WidgetType.Timer => "Fortschrittsfarbe",
        WidgetType.NetworkTraffic => "Downloadfarbe",
        _ => "Balken- und Verlaufsfarbe",
    };

    public string IndicatorColorHint => _entry.Type switch
    {
        WidgetType.AudioOutputs or WidgetType.AudioInputs => "Strich unter dem aktiven Standardgerät.",
        WidgetType.Volume => "Füllbalken neben dem Symbol (wenn eingeschaltet).",
        WidgetType.Timer => "Balken unter der Restzeit, der bis zum Ablauf leerläuft.",
        WidgetType.NetworkTraffic => "Pfeil und Fläche im Verlauf.",
        _ => "Bei hoher Last bzw. Temperatur orange und rot, wenn die Warnfarben an sind.",
    };

    public string ResetColorsHint => _entry.Type switch
    {
        WidgetType.AudioOutputs or WidgetType.AudioInputs =>
            "Ohne eigene Farbe folgt das Widget der Schriftfarbe unter „Darstellung“; die Markierung wird wieder blau.",
        WidgetType.NetworkTraffic =>
            "Ohne eigene Farbe folgt der Text der Schriftfarbe unter „Darstellung“; Download wird wieder blau, Upload orange.",
        WidgetType.Network or WidgetType.Stopwatch =>
            "Ohne eigene Farbe folgt das Widget der Schriftfarbe unter „Darstellung“.",
        _ when IsMetric =>
            "Ohne eigene Farbe folgt das Widget der Schriftfarbe unter „Darstellung“; Balken und Verlauf werden wieder blau.",
        _ => "Ohne eigene Farbe folgt das Widget der Schriftfarbe unter „Darstellung“; Akzente werden wieder blau.",
    };

    // ---- Netzwerk, Lautstärke, Timer, Stoppuhr, Datenrate ----------------------------------
    public NetworkSettings Network { get; }
    public VolumeSettings Volume { get; }

    /// <summary>Verhalten nach Ablauf; die Timer selbst stehen in <see cref="TimerRows"/>.</summary>
    public TimerSettings Timer { get; }

    public StopwatchSettings Stopwatch { get; }
    public TrafficSettings Traffic { get; }

    public Visibility NetworkVisibility => Is(WidgetType.Network) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VolumeVisibility => Is(WidgetType.Volume) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TimerVisibility => Is(WidgetType.Timer) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility StopwatchVisibility => Is(WidgetType.Stopwatch) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TrafficVisibility => Is(WidgetType.NetworkTraffic) ? Visibility.Visible : Visibility.Collapsed;

    public AudioFlow VolumeFlow
    {
        get => Volume.Flow;
        set
        {
            if (!Enum.IsDefined(value) || value == Volume.Flow) return;
            Volume.Flow = value;
            OnPropertyChanged();
        }
    }

    public int WheelStep
    {
        get => Volume.WheelStep;
        set
        {
            if (value <= 0 || value == Volume.WheelStep) return; // Listenaustausch schreibt kurz 0
            Volume.WheelStep = value;
            OnPropertyChanged();
        }
    }

    public TimerSound TimerSound
    {
        get => Timer.Sound;
        set
        {
            if (!Enum.IsDefined(value) || value == Timer.Sound) return;
            Timer.Sound = value;
            OnPropertyChanged();
        }
    }

    public TrafficDirection TrafficDirection
    {
        get => Traffic.Direction;
        set
        {
            if (!Enum.IsDefined(value) || value == Traffic.Direction) return;
            Traffic.Direction = value;
            OnPropertyChanged();
        }
    }

    public int TrafficInterval
    {
        get => Traffic.IntervalMs;
        set
        {
            if (value <= 0 || value == Traffic.IntervalMs) return;
            Traffic.IntervalMs = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Adapter-GUID; "" = alle aktiven Adapter.</summary>
    public string TrafficAdapter
    {
        get => Traffic.Adapter;
        set
        {
            if (value is null || value == Traffic.Adapter) return; // Listenaustausch schreibt kurz null
            Traffic.Adapter = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Tastenkürzel je Aktion (Lautstärke, Timer, Stoppuhr).</summary>
    public ObservableCollection<HotkeyActionRow> HotkeyActions { get; } = [];

    public Visibility HotkeyActionsVisibility => HotkeyActions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    // Timer: bearbeitbare Liste
    private readonly string _timersSnapshot;

    public ObservableCollection<TimerPresetRow> TimerRows { get; } = [];
    public ICommand AddTimerCommand { get; }

    private void AddTimerRow()
    {
        int number = TimerRows.Count + 1;
        while (TimerRows.Any(r => r.Name == $"Timer {number}")) number++;
        var duration = TimerRows.LastOrDefault()?.DurationSeconds ?? 300;
        TimerRows.Add(new TimerPresetRow(new TimerPreset { Name = $"Timer {number}", DurationSeconds = duration }, RemoveTimerRow));
        OnPropertyChanged(nameof(Summary));
    }

    private void RemoveTimerRow(TimerPresetRow row)
    {
        TimerRows.Remove(row);
        OnPropertyChanged(nameof(Summary));
    }

    private static string TimersSignature(IEnumerable<TimerPreset> timers) =>
        string.Join("|", timers.Select(t => $"{t.Id}:{t.Name}:{t.DurationSeconds}"));

    /// <summary>
    /// Timer für den gespeicherten Eintrag. Wurden sie hier nicht geändert, gilt der Stand der laufenden Konfiguration –
    /// sie können im Timer-Popup bearbeitet worden sein, während dieses Fenster offen war.
    /// </summary>
    private TimerSettings BuildTimer(WidgetEntry? live)
    {
        var timer = Timer.Clone();
        var rows = TimerRows.Select(r => r.ToPreset()).ToList();
        timer.Timers = TimersSignature(rows) == _timersSnapshot && live?.Timer is { } liveTimer
            ? liveTimer.Timers.Select(t => t.Clone()).ToList()
            : rows;
        return timer;
    }

    private TrafficSettings BuildTraffic()
    {
        var traffic = Traffic.Clone();
        // Adaptername nur zur Anzeige (Tooltip, falls der Adapter einmal fehlt).
        traffic.AdapterName = traffic.Adapter.Length == 0 ? ""
            : Veyra.Network.NetworkTrafficMonitor.EnumerateAdapters().FirstOrDefault(a => string.Equals(a.Id, traffic.Adapter, StringComparison.OrdinalIgnoreCase))?.Name
              ?? traffic.AdapterName;
        return traffic;
    }

    /// <summary>Nur Mess-Widgets: Darstellung, Bezeichnung, Warnfarben, RAM-Einheit, Grafikkarte.</summary>
    public MetricSettings Metric { get; }

    public MetricStyle MetricStyle
    {
        get => Metric.Style;
        set
        {
            if (!Enum.IsDefined(value) || value == Metric.Style) return;
            Metric.Style = value;
            OnPropertyChanged();
        }
    }

    public Visibility RamVisibility => _entry.Type == WidgetType.Ram ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GpuChoiceVisibility =>
        _entry.Type is WidgetType.Gpu or WidgetType.GpuTemperature ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Gewählte Grafikkarte; "" = automatisch.</summary>
    public string GpuChoice
    {
        get => Metric.Gpu;
        set
        {
            if (value is null || value == Metric.Gpu) return; // Listenaustausch schreibt kurz null
            Metric.Gpu = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Nur Audio-Widgets: Farbe der Symbole.</summary>
    public string IconColor
    {
        get => _iconColor;
        set
        {
            if (SetProperty(ref _iconColor, value ?? "")) _iconColorCustom = true;
        }
    }

    /// <summary>Nur Audio-Widgets: Farbe der Gerätenamen.</summary>
    public string LabelColor
    {
        get => _labelColor;
        set
        {
            if (SetProperty(ref _labelColor, value ?? "")) _labelColorCustom = true;
        }
    }

    /// <summary>Nur Audio-Widgets: Farbe des Strichs unter dem Standardgerät.</summary>
    public string IndicatorColor
    {
        get => _indicatorColor;
        set
        {
            if (SetProperty(ref _indicatorColor, value ?? "")) _indicatorColorCustom = true;
        }
    }

    /// <summary>Nur Audio-Widgets: globales Tastenkürzel ("Ctrl+Shift+I"), das zum nächsten Gerät wechselt; null = keins.</summary>
    public string? Hotkey
    {
        get => _hotkey;
        set
        {
            if (SetProperty(ref _hotkey, string.IsNullOrEmpty(value) ? null : value)) _hotkeyChanged?.Invoke();
        }
    }

    /// <summary>Hinweis, wenn das Tastenkürzel doppelt vergeben oder von einem anderen Programm belegt ist.</summary>
    public string HotkeyWarning
    {
        get => _hotkeyWarning;
        set
        {
            if (SetProperty(ref _hotkeyWarning, value)) OnPropertyChanged(nameof(HotkeyWarningVisibility));
        }
    }

    public Visibility HotkeyWarningVisibility => HotkeyWarning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value)) OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>Kurzinfo neu lesen (z. B. nach Änderung des Zeitformats auf der Seite "Uhr &amp; Kalender").</summary>
    public void RefreshSummary() => OnPropertyChanged(nameof(Summary));

    public WidgetSlot Slot
    {
        get => _slot;
        set
        {
            if (SetProperty(ref _slot, value)) OnPropertyChanged(nameof(OuterMarginEnabled));
        }
    }

    /// <summary>In der Mitte gibt es keinen Randabstand.</summary>
    public bool OuterMarginEnabled => Slot != WidgetSlot.Center;

    public double OuterMargin { get => _outerMargin; set => SetProperty(ref _outerMargin, value); }
    public double InnerSpacing { get => _innerSpacing; set => SetProperty(ref _innerSpacing, value); }

    /// <summary>Größe in Prozent (100 = normal).</summary>
    public double ScalePercent { get => _scalePercent; set => SetProperty(ref _scalePercent, value); }

    public bool AllDevices
    {
        get => _allDevices;
        set
        {
            if (!SetProperty(ref _allDevices, value)) return;
            OnPropertyChanged(nameof(DeviceListEnabled));
            if (value) foreach (var device in Devices) device.Selected = true;
        }
    }

    public bool DeviceListEnabled => !AllDevices;

    public ObservableCollection<DeviceChoice> Devices { get; } = [];

    public string PositionText => $"Position auf der Leiste: {_entry.Position:P0} – per Drag & Drop verschieben";

    /// <param name="live">Eintrag aus der laufenden Konfiguration – Positionen können sich durch Drag &amp; Drop geändert haben.</param>
    public WidgetEntry ToEntry(WidgetEntry? live) => new()
    {
        Id = _entry.Id,
        Type = _entry.Type,
        Enabled = Enabled,
        Slot = Slot,
        OuterMargin = OuterMargin,
        InnerSpacing = InnerSpacing,
        Scale = ScalePercent / 100,
        Position = live?.Position ?? _entry.Position,
        FreeX = live is null ? _entry.FreeX : live.FreeX,
        FreeY = live is null ? _entry.FreeY : live.FreeY,
        Devices = AllDevices ? [] : Devices.Where(d => d.Selected).Select(d => d.Id).ToList(),
        // Ohne eigene Farbe (null) folgt das Widget der Schriftfarbe der Leiste.
        IconColor = HasIconColor && _iconColorCustom ? IconColor : null,
        LabelColor = !IsClock && _labelColorCustom ? LabelColor : null,
        IndicatorColor = HasIndicatorColor && _indicatorColorCustom ? IndicatorColor : null,
        Metric = IsMetric ? Metric.Clone() : null,
        Network = Is(WidgetType.Network) ? Network.Clone() : null,
        Volume = Is(WidgetType.Volume) ? Volume.Clone() : null,
        Timer = Is(WidgetType.Timer) ? BuildTimer(live) : null,
        Stopwatch = Is(WidgetType.Stopwatch) ? Stopwatch.Clone() : null,
        Traffic = Is(WidgetType.NetworkTraffic) ? BuildTraffic() : null,
        Hotkey = IsAudio ? Hotkey : null,
        Hotkeys = HotkeyActions.Any(a => a.Hotkey is not null)
            ? HotkeyActions.Where(a => a.Hotkey is not null).ToDictionary(a => a.Action.Id, a => a.Hotkey!)
            : null,
        Clock = IsClock ? Clock.Clone() : null,
    };
}

/// <summary>Eine Tastenkürzel-Aktion eines Widgets (z. B. „Lauter“) mit ihrem Kürzel.</summary>
public sealed class HotkeyActionRow(HotkeyAction action, string? hotkey, Action changed) : ObservableObject
{
    private string? _hotkey = hotkey;
    private string _warning = "";

    public HotkeyAction Action { get; } = action;
    public string Name => Action.Name;
    public string Description => Action.Description;
    public Visibility DescriptionVisibility => Description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Speicherformat ("Ctrl+Alt+Up"); null = kein Kürzel.</summary>
    public string? Hotkey
    {
        get => _hotkey;
        set
        {
            if (SetProperty(ref _hotkey, string.IsNullOrEmpty(value) ? null : value)) changed();
        }
    }

    public string HotkeyWarning
    {
        get => _warning;
        set
        {
            if (SetProperty(ref _warning, value)) OnPropertyChanged(nameof(HotkeyWarningVisibility));
        }
    }

    public Visibility HotkeyWarningVisibility => HotkeyWarning.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>Ein Timer in der Liste eines Timer-Widgets: Name und Dauer.</summary>
public sealed class TimerPresetRow : ObservableObject
{
    private readonly string _id;
    private string _name;
    private int _durationSeconds;
    private string _durationText;
    private bool _durationInvalid;

    public TimerPresetRow(TimerPreset preset, Action<TimerPresetRow> remove)
    {
        _id = preset.Id;
        _name = preset.Name;
        _durationSeconds = preset.DurationSeconds;
        _durationText = Timing.TimerService.FormatRemaining(TimeSpan.FromSeconds(preset.DurationSeconds));
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Name { get => _name; set => SetProperty(ref _name, value ?? ""); }
    public int DurationSeconds => _durationSeconds;
    public ICommand RemoveCommand { get; }

    /// <summary>"5:00", "1:30:00" – Eingabe auch als Minuten ("90") oder mit Einheiten ("1h 30m").</summary>
    public string DurationText
    {
        get => _durationText;
        set
        {
            if (!SetProperty(ref _durationText, value ?? "")) return;
            bool valid = Timing.TimerService.TryParseDuration(value, out var duration);
            if (valid) _durationSeconds = (int)duration.TotalSeconds;
            DurationInvalid = !valid;
        }
    }

    /// <summary>Ungültige Eingabe: Hinweis anzeigen, gespeichert wird die zuletzt gültige Dauer.</summary>
    public bool DurationInvalid
    {
        get => _durationInvalid;
        private set
        {
            if (SetProperty(ref _durationInvalid, value)) OnPropertyChanged(nameof(DurationHintVisibility));
        }
    }

    public Visibility DurationHintVisibility => DurationInvalid ? Visibility.Visible : Visibility.Collapsed;

    public TimerPreset ToPreset() => new() { Id = _id, Name = Name.Trim(), DurationSeconds = _durationSeconds };
}

/// <summary>Symbol und Kurzname je Audiogerät (gilt für alle Audio-Widgets).</summary>
public sealed class AudioDeviceRowViewModel : ObservableObject
{
    private string _icon;
    private string _label;

    public AudioDeviceRowViewModel(AudioDevice device, AudioDeviceSettings? settings)
    {
        Device = device;
        _icon = settings?.Icon ?? AudioIcons.AutoKey;
        _label = settings?.Label ?? "";
    }

    public AudioDevice Device { get; }
    public string Name => Device.Name;
    public string State => Device.IsDefault ? "Standardgerät" : "";

    public string Icon
    {
        get => _icon;
        set
        {
            if (string.IsNullOrEmpty(value)) return; // Listenaustausch schreibt kurz null
            if (SetProperty(ref _icon, value)) OnPropertyChanged(nameof(IconKey));
        }
    }

    public string Label { get => _label; set => SetProperty(ref _label, value ?? ""); }

    /// <summary>Tatsächlich angezeigtes Symbol (bei "Auto" das automatisch gewählte).</summary>
    public string IconKey => AudioIcons.Resolve(_icon, Device.Flow, Device.FormFactor, Device.Name);

    public string AutoIconKey => AudioIcons.Auto(Device.Flow, Device.FormFactor, Device.Name);

    public bool IsCustomized => Icon != AudioIcons.AutoKey || Label.Length > 0;
}

/// <summary>Eintrag in der Leistenauswahl; der Name aktualisiert sich beim Umbenennen sofort.</summary>
public sealed class BarOption(int index, string name) : ObservableObject
{
    private string _name = name;

    public int Index { get; } = index;
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    public override string ToString() => Name; // Name für Bildschirmleser
}
