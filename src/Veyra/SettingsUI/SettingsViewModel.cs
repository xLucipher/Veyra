using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using Veyra.Audio;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Monitors;

namespace Veyra.SettingsUI;

/// <summary>
/// Bearbeitet eine Kopie der aktuellen Einstellungen. Erst "Übernehmen"/"OK" speichert sie über den
/// <see cref="SettingsService"/>, der die Leiste sofort ohne Neustart aktualisiert.
/// Auswahlfelder mit Textwerten laufen über eigene Eigenschaften, die null ignorieren: WPF schreibt beim
/// Austausch einer Auswahlliste kurzzeitig null zurück, das darf keine Einstellung löschen.
/// </summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    public const string DefaultFontLabel = "(Standardschrift)";

    private readonly SettingsService _service;
    private AppSettings _settings;
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private IReadOnlyList<Option> _monitorOptions = [];
    private IReadOnlyList<Option> _timeFormatOptions = [];
    private IReadOnlyList<Option> _dateFormatOptions = [];
    private IReadOnlyList<Option> _weekFormatOptions = [];
    private IReadOnlyList<Option> _cultureOptions = [];
    private IReadOnlyList<IntOption> _showDelayOptions = [];
    private IReadOnlyList<IntOption> _safetyIntervalOptions = [];
    private IReadOnlyList<IntOption> _borderThicknessOptions = [];
    private IReadOnlyList<string> _runningProcesses = [];
    private string? _excludeCandidate;
    private string? _forceCandidate;
    private string _statusText = "";
    private readonly AudioDeviceService _audioService = new();
    private WidgetInfo? _newWidget;

    public SettingsViewModel(SettingsService service, Action exitApplication, Func<Task<string>> checkForUpdates)
    {
        _service = service;
        _settings = service.Current.Clone();
        _geometrySnapshot = Geometry(_settings);
        CenterHorizontallyCommand = new RelayCommand(() =>
        {
            Bar.Appearance.FloatingLeft = null;
            OnPropertyChanged(nameof(Settings));
        });

        FontFamilies = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct()
            .OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToList();
        ClockFontFamilies = FontFamilies.Prepend(DefaultFontLabel).ToList();

        ApplyCommand = new RelayCommand(Apply);
        OkCommand = new RelayCommand(() =>
        {
            Apply();
            CloseRequested?.Invoke(this, EventArgs.Empty);
        });
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        ResetCommand = new RelayCommand(() =>
        {
            var defaults = new AppSettings();
            defaults.Normalize();
            ReplaceSettings(defaults);
            StatusText = "Standardwerte geladen – mit „Übernehmen“ speichern.";
        });
        OpenSettingsFileCommand = new RelayCommand(() => OpenWithShell(_service.FilePath));
        OpenLogFolderCommand = new RelayCommand(() => OpenWithShell(Log.LogDirectory ?? SettingsService.DirectoryPath));
        RefreshMonitorsCommand = new RelayCommand(RefreshMonitors);
        RefreshProcessesCommand = new RelayCommand(() => RunningProcesses = SettingsOptions.GetWindowedProcessNames());
        AddExcludedCommand = new RelayCommand(() => AddProcess(_settings.Behavior.FullscreenExcludedProcesses, ExcludeCandidate, nameof(ExcludedProcessesText)),
            () => !string.IsNullOrEmpty(ExcludeCandidate));
        AddForcedCommand = new RelayCommand(() => AddProcess(_settings.Behavior.FullscreenForcedProcesses, ForceCandidate, nameof(ForcedProcessesText)),
            () => !string.IsNullOrEmpty(ForceCandidate));
        ExitApplicationCommand = new RelayCommand(exitApplication);
        CheckForUpdatesCommand = new RelayCommand(async () =>
        {
            UpdateStatus = "Suche nach Updates …";
            UpdateStatus = await checkForUpdates();
        }, () => UpdateStatus != "Suche nach Updates …");
        OpenReleasesCommand = new RelayCommand(() => Updates.UpdateService.OpenReleasePage());
        UpdateStatus = DescribeLastCheck();
        AddWidgetCommand = new RelayCommand(AddWidget, () => NewWidget is not null);
        ResetFreePositionsCommand = new RelayCommand(ResetFreePositions);
        AddBarCommand = new RelayCommand(AddBar);
        RemoveBarCommand = new RelayCommand(RemoveBar, () => CanRemoveBar);
        RefreshAudioDevicesCommand = new RelayCommand(RebuildAudioRows);
        _audioService.Changed += (_, _) => RebuildAudioRows();

        _runningProcesses = SettingsOptions.GetWindowedProcessNames();
        RebuildBarOptions();
        RebuildOptions();
        RefreshMonitors();
        RebuildWidgetRows();
        RebuildAudioRows();
    }

    // ---- Widgets ----------------------------------------------------------------------------
    public ICommand AddWidgetCommand { get; }
    public ICommand ResetFreePositionsCommand { get; }
    public ICommand RefreshAudioDevicesCommand { get; }

    public System.Collections.ObjectModel.ObservableCollection<WidgetRowViewModel> WidgetRows { get; } = [];
    public System.Collections.ObjectModel.ObservableCollection<AudioDeviceRowViewModel> OutputDeviceRows { get; } = [];
    public System.Collections.ObjectModel.ObservableCollection<AudioDeviceRowViewModel> InputDeviceRows { get; } = [];

    public IReadOnlyList<WidgetInfo> WidgetCatalogItems => WidgetCatalog.All;
    public IReadOnlyList<EnumOption> SlotOptions => WidgetCatalog.Slots;
    public IReadOnlyList<double> MarginPresets => WidgetCatalog.MarginPresets;
    public IReadOnlyList<double> SpacingPresets => WidgetCatalog.SpacingPresets;
    public IReadOnlyList<double> ScalePresets => WidgetCatalog.ScalePresets;
    public IReadOnlyList<EnumOption> MetricStyleOptions => WidgetCatalog.MetricStyles;

    public IReadOnlyList<EnumOption> VolumeFlowOptions => WidgetCatalog.VolumeFlows;
    public IReadOnlyList<EnumOption> TimerSoundOptions => WidgetCatalog.TimerSounds;
    public IReadOnlyList<EnumOption> TrafficDirectionOptions => WidgetCatalog.TrafficDirections;
    public IReadOnlyList<IntOption> TrafficIntervalOptions => WidgetCatalog.TrafficIntervals;
    public IReadOnlyList<int> WheelStepOptions => WidgetCatalog.WheelSteps;

    private IReadOnlyList<Option>? _adapterOptions;

    /// <summary>Netzwerkadapter für Upload-/Download-Widgets; "" = alle aktiven Adapter.</summary>
    public IReadOnlyList<Option> AdapterOptions => _adapterOptions ??= BuildAdapterOptions();

    private IReadOnlyList<Option> BuildAdapterOptions()
    {
        var options = new List<Option> { new("", "Alle aktiven Netzwerkadapter") };
        options.AddRange(Network.NetworkTrafficMonitor.EnumerateAdapters()
            .Select(a => new Option(a.Id, a.IsUp ? $"{a.Name} – {a.Description}" : $"{a.Name} – {a.Description} (nicht verbunden)")));
        // Gespeicherter, gerade nicht vorhandener Adapter bleibt auswählbar.
        foreach (var missing in _settings.Bars.SelectMany(b => b.Widgets).Select(w => w.Traffic)
                     .Where(t => t is { Adapter.Length: > 0 } && options.All(o => !string.Equals(o.Value, t.Adapter, StringComparison.OrdinalIgnoreCase)))
                     .DistinctBy(t => t!.Adapter, StringComparer.OrdinalIgnoreCase))
            options.Add(new Option(missing!.Adapter, $"{(missing.AdapterName.Length > 0 ? missing.AdapterName : missing.Adapter)} (nicht gefunden)"));
        return options;
    }

    private IReadOnlyList<Option>? _gpuOptions;

    /// <summary>Grafikkarten für GPU- und Temperatur-Widgets; "" = automatisch.</summary>
    public IReadOnlyList<Option> GpuOptions => _gpuOptions ??= BuildGpuOptions();

    private IReadOnlyList<Option> BuildGpuOptions()
    {
        var gpus = Sensors.SystemMonitor.EnumerateGpus();
        var auto = gpus.OrderByDescending(g => g.DedicatedMemory).FirstOrDefault();
        var options = new List<Option> { new("", auto is null ? "Automatisch" : $"Automatisch ({auto.Name})") };
        options.AddRange(gpus.Select(g => new Option(g.Name, g.Name)));
        // Gespeicherte, gerade nicht vorhandene Karte bleibt auswählbar.
        foreach (var missing in _settings.Bars.SelectMany(b => b.Widgets).Select(w => w.Metric?.Gpu)
                     .Where(n => !string.IsNullOrEmpty(n) && options.All(o => !string.Equals(o.Value, n, StringComparison.OrdinalIgnoreCase)))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
            options.Add(new Option(missing!, $"{missing} (nicht gefunden – automatisch)"));
        return options;
    }

    // ---- Leistenmodus ----------------------------------------------------------------------
    public ICommand CenterHorizontallyCommand { get; }

    public IReadOnlyList<int> CornerRadiusPresets { get; } = [0, 4, 8, 12, 16, 24, 32, 50, 100];

    public IReadOnlyList<EnumOption> BarModeOptions { get; } =
    [
        new(BarMode.FullWidth, "Fixiert – oben, volle Breite"),
        new(BarMode.Floating, "Dynamisch – frei beweglich"),
    ];

    public IReadOnlyList<EnumOption> LayerOptions { get; } =
    [
        new(WindowLayer.Topmost, "Immer im Vordergrund"),
        new(WindowLayer.Normal, "Normal"),
        new(WindowLayer.Desktop, "Im Hintergrund (Desktop-Widget)"),
    ];

    public BarMode BarMode
    {
        get => Bar.Appearance.Mode;
        set
        {
            if (!Enum.IsDefined(value) || value == Bar.Appearance.Mode) return;
            Bar.Appearance.Mode = value;
            if (value == BarMode.Floating && Bar.Appearance.FloatingLeft is null && Bar.Appearance.FloatingTop is null)
                InitDynamicFromFixed();
            OnPropertyChanged();
            OnPropertyChanged(nameof(FixedVisibility));
            OnPropertyChanged(nameof(DynamicVisibility));
            OnPropertyChanged(nameof(Settings));
        }
    }

    public System.Windows.Visibility FixedVisibility =>
        Bar.Appearance.IsDynamic ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public System.Windows.Visibility DynamicVisibility =>
        Bar.Appearance.IsDynamic ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    /// <summary>Erster Wechsel auf dynamisch: dieselbe Leiste, nur schwebend – oben, fast volle Breite, abgerundet.</summary>
    private void InitDynamicFromFixed()
    {
        var a = Bar.Appearance;
        var monitor = MonitorService.Resolve(Bar.MonitorSelection);
        double widthDip = monitor is null ? 1600 : monitor.Bounds.Width / monitor.Scale;
        a.Width = (int)Math.Max(200, widthDip - 32);
        a.FloatingHeight = a.Height;
        a.FloatingLeft = 16;
        a.FloatingTop = 8;
    }

    /// <summary>Position/Größe/Monitor jeder Leiste beim Öffnen bzw. letzten Speichern (nach Leisten-ID).</summary>
    private Dictionary<string, (double? Left, double? Top, int Width, int Height, string Monitor)> _geometrySnapshot = [];

    private static (double? Left, double? Top, int Width, int Height, string Monitor) Geometry(BarSettings b) =>
        (b.Appearance.FloatingLeft, b.Appearance.FloatingTop, b.Appearance.Width, b.Appearance.FloatingHeight, b.MonitorSelection);

    private static Dictionary<string, (double? Left, double? Top, int Width, int Height, string Monitor)> Geometry(AppSettings s) =>
        s.Bars.ToDictionary(b => b.Id, Geometry);

    // ---- Leisten ---------------------------------------------------------------------------
    private int _barIndex;

    /// <summary>Die Leiste, deren Darstellung und Widgets gerade bearbeitet werden.</summary>
    public BarSettings Bar => _settings.Bars[Math.Clamp(_barIndex, 0, _settings.Bars.Count - 1)];

    public System.Collections.ObjectModel.ObservableCollection<BarOption> BarOptions { get; } = [];

    public ICommand AddBarCommand { get; }
    public ICommand RemoveBarCommand { get; }

    public int SelectedBarIndex
    {
        get => _barIndex;
        set
        {
            // WPF schreibt beim Austausch der Liste kurz -1 zurück – ignorieren.
            if (value < 0 || value >= _settings.Bars.Count || value == _barIndex) return;
            SyncWidgetRows();
            _barIndex = value;
            OnBarSwitched();
        }
    }

    public string BarName
    {
        get => Bar.Name;
        set
        {
            Bar.Name = value ?? "";
            if (_barIndex < BarOptions.Count) BarOptions[_barIndex].Name = DisplayName(Bar, _barIndex);
            OnPropertyChanged();
        }
    }

    public bool CanRemoveBar => _settings.Bars.Count > 1;

    public string BarHintText =>
        $"Jede Leiste ist ein eigenes Fenster mit eigener Darstellung, Ebene, Monitor und eigenen Widgets. Vorhanden: {(_settings.Bars.Count == 1 ? "1 Leiste" : $"{_settings.Bars.Count} Leisten")}.";

    /// <summary>Rechtsklick auf eine Leiste → Einstellungen öffnen sich mit dieser Leiste.</summary>
    public void SelectBarById(string id)
    {
        int index = _settings.Bars.FindIndex(b => b.Id == id);
        if (index >= 0) SelectedBarIndex = index;
    }

    private static string DisplayName(BarSettings bar, int index) =>
        string.IsNullOrWhiteSpace(bar.Name) ? $"Leiste {index + 1}" : bar.Name.Trim();

    private void RebuildBarOptions()
    {
        BarOptions.Clear();
        for (int i = 0; i < _settings.Bars.Count; i++)
            BarOptions.Add(new BarOption(i, DisplayName(_settings.Bars[i], i)));
        OnPropertyChanged(nameof(SelectedBarIndex));
        OnPropertyChanged(nameof(CanRemoveBar));
        OnPropertyChanged(nameof(BarHintText));
    }

    /// <summary>Neue Leiste: kleine, dynamische Leiste mit Uhr, mittig oben auf dem Monitor der aktuellen Leiste.</summary>
    private void AddBar()
    {
        SyncWidgetRows();
        var bar = new BarSettings
        {
            Name = $"Leiste {_settings.Bars.Count + 1}",
            MonitorSelection = Bar.MonitorSelection,
            Appearance = new AppearanceSettings
            {
                Mode = BarMode.Floating,
                Width = 320,
                FloatingHeight = 96,
                FloatingTop = 120,
                CornerRadius = 16,
                BackgroundColor = Bar.Appearance.BackgroundColor,
                ForegroundColor = Bar.Appearance.ForegroundColor,
                FontFamily = Bar.Appearance.FontFamily,
                BorderColor = Bar.Appearance.BorderColor,
            },
            Widgets = [new WidgetEntry { Type = WidgetType.Clock, Slot = WidgetSlot.Center }],
        };
        bar.Normalize(_settings.Bars.Count);
        _settings.Bars.Add(bar);
        _barIndex = _settings.Bars.Count - 1;
        OnBarSwitched();
        StatusText = $"„{bar.Name}“ hinzugefügt – mit „Übernehmen“ anzeigen.";
    }

    private void RemoveBar()
    {
        if (_settings.Bars.Count <= 1) return;
        var removed = Bar;
        _settings.Bars.RemoveAt(_barIndex);
        _barIndex = Math.Min(_barIndex, _settings.Bars.Count - 1);
        OnBarSwitched();
        StatusText = $"„{removed.Name}“ entfernt – mit „Übernehmen“ schließen.";
    }

    /// <summary>Andere Leiste ausgewählt bzw. Liste geändert → alle leistenbezogenen Felder neu lesen.</summary>
    private void OnBarSwitched()
    {
        RebuildBarOptions();
        RebuildOptions();
        RefreshMonitors();
        RebuildWidgetRows();
        OnPropertyChanged(string.Empty);
    }

    public IReadOnlyList<EnumOption> LabelModeOptions { get; } =
    [
        new(AudioLabelMode.IconOnly, "Nur Symbol"),
        new(AudioLabelMode.IconAndName, "Symbol und Name"),
        new(AudioLabelMode.NameOnly, "Nur Name"),
    ];

    public WidgetInfo? NewWidget { get => _newWidget; set => SetProperty(ref _newWidget, value); }

    public void Dispose() => _audioService.Dispose();

    private void RebuildWidgetRows()
    {
        WidgetRows.Clear();
        // Feste Widgets zuerst, freie danach von links nach rechts – so wie sie auf der Leiste stehen.
        foreach (var entry in Bar.Widgets.OrderBy(w => w.IsFixed ? 0 : 1).ThenBy(w => w.IsFixed ? (double)w.Slot : w.Position))
            WidgetRows.Add(CreateRow(entry));
        RebuildClockRows();
        UpdateHotkeyWarnings();
    }

    private WidgetRowViewModel CreateRow(WidgetEntry entry)
    {
        IReadOnlyList<AudioDevice> devices = entry.Type switch
        {
            WidgetType.AudioOutputs => _audioService.GetDevices(AudioFlow.Output),
            WidgetType.AudioInputs => _audioService.GetDevices(AudioFlow.Input),
            _ => [],
        };
        var created = new WidgetRowViewModel(entry, devices, _settings.Audio, Bar.Appearance.ForegroundColor,
            remove: row =>
            {
                WidgetRows.Remove(row);
                RebuildClockRows();
                UpdateHotkeyWarnings();
            },
            editClock: row =>
            {
                SelectedClock = row;
                SelectedPageIndex = ClockPageIndex;
            },
            hotkeyChanged: UpdateHotkeyWarnings)
        {
            IsExpanded = _expandedWidgets.Contains(entry.Id),
        };
        // Aufgeklappte Widgets merken – nach "Übernehmen" werden die Zeilen neu erzeugt.
        created.PropertyChanged += (sender, e) =>
        {
            if (e.PropertyName != nameof(WidgetRowViewModel.IsExpanded) || sender is not WidgetRowViewModel row) return;
            if (row.IsExpanded) _expandedWidgets.Add(row.Id);
            else _expandedWidgets.Remove(row.Id);
        };
        return created;
    }

    /// <summary>IDs der ausgeklappten Widget-Karten (standardmäßig sind alle eingeklappt).</summary>
    private readonly HashSet<string> _expandedWidgets = [];

    /// <summary>
    /// Tastenkürzel prüfen: doppelt vergeben (auch auf anderen Leisten) oder von einem anderen Programm belegt.
    /// Kürzel, die Veyra gerade selbst angemeldet hat, gelten als frei – sie werden beim Übernehmen neu vergeben.
    /// </summary>
    private void UpdateHotkeyWarnings()
    {
        static IEnumerable<string?> EntryHotkeys(WidgetEntry w) => (w.Hotkeys?.Values ?? Enumerable.Empty<string>()).Prepend(w.Hotkey);

        var otherBars = _settings.Bars.Where(b => b != Bar).SelectMany(b => b.Widgets).SelectMany(EntryHotkeys);
        var thisBar = WidgetRows.Where(r => r.IsAudio).Select(r => r.Hotkey)
            .Concat(WidgetRows.SelectMany(r => r.HotkeyActions).Select(a => a.Hotkey));
        var all = thisBar.Concat(otherBars).OfType<string>().ToList();
        var ownRegistered = _service.Current.Bars.SelectMany(b => b.Widgets).SelectMany(EntryHotkeys).OfType<string>().ToHashSet();

        string Warning(string? hotkey) => hotkey is null ? ""
            : all.Count(h => h == hotkey) > 1 ? "Dieses Tastenkürzel ist bei einem anderen Widget bzw. einer anderen Aktion schon vergeben."
            : !ownRegistered.Contains(hotkey) && !IsHotkeyAvailable(hotkey) ? "Dieses Tastenkürzel ist bereits von Windows oder einem anderen Programm belegt."
            : "";

        foreach (var row in WidgetRows.Where(r => r.IsAudio)) row.HotkeyWarning = Warning(row.Hotkey);
        foreach (var action in WidgetRows.SelectMany(r => r.HotkeyActions)) action.HotkeyWarning = Warning(action.Hotkey);
    }

    /// <summary>Probeweise anmelden und sofort wieder abmelden.</summary>
    private static bool IsHotkeyAvailable(string text)
    {
        if (!Hotkey.TryParse(text, out var hotkey)) return false;
        const int probeId = 0xBFFF;
        if (!Interop.NativeMethods.RegisterHotKey(IntPtr.Zero, probeId, hotkey.NativeModifiers | Interop.NativeMethods.MOD_NOREPEAT, hotkey.VirtualKey))
            return false;
        Interop.NativeMethods.UnregisterHotKey(IntPtr.Zero, probeId);
        return true;
    }

    // ---- Uhren (jede Uhr hat eigene Einstellungen) ------------------------------------------
    private const int ClockPageIndex = 2;
    private int _selectedPageIndex;
    private WidgetRowViewModel? _selectedClock;

    /// <summary>Platzhalter, solange die Leiste keine Uhr hat – Änderungen daran werden nirgends gespeichert.</summary>
    private readonly ClockSettings _noClock = new();

    public int SelectedPageIndex { get => _selectedPageIndex; set => SetProperty(ref _selectedPageIndex, value); }

    /// <summary>Uhren der ausgewählten Leiste.</summary>
    public System.Collections.ObjectModel.ObservableCollection<WidgetRowViewModel> ClockRows { get; } = [];

    public WidgetRowViewModel? SelectedClock
    {
        get => _selectedClock;
        set
        {
            // WPF schreibt beim Austausch der Liste kurz null zurück – ignorieren.
            if (value is null || value == _selectedClock) return;
            _selectedClock = value;
            OnClockSwitched();
        }
    }

    /// <summary>Die Uhr, deren Einstellungen auf der Seite "Uhr &amp; Kalender" bearbeitet werden.</summary>
    public ClockSettings ClockEdit => _selectedClock?.Clock ?? _noClock;

    public System.Windows.Visibility ClockEditorVisibility =>
        ClockRows.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public System.Windows.Visibility NoClockVisibility =>
        ClockRows.Count > 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public System.Windows.Visibility ClockSelectorVisibility =>
        ClockRows.Count > 1 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    /// <summary>Uhrenliste neu aufbauen (Widgets hinzugefügt/entfernt, andere Leiste) – Auswahl möglichst beibehalten.</summary>
    private void RebuildClockRows()
    {
        var clocks = WidgetRows.Where(r => r.IsClock).ToList();
        for (int i = 0; i < clocks.Count; i++)
            clocks[i].Title = clocks.Count > 1 ? $"Uhr {i + 1}" : "Uhr";

        var previous = _selectedClock;
        ClockRows.Clear();
        foreach (var clock in clocks) ClockRows.Add(clock);

        // Nach "Übernehmen" sind die Zeilen neu erzeugt – deshalb über die Widget-ID wiederfinden.
        _selectedClock = clocks.FirstOrDefault(c => c.Id == previous?.Id) ?? clocks.FirstOrDefault();
        OnClockSwitched();
        OnPropertyChanged(nameof(ClockEditorVisibility));
        OnPropertyChanged(nameof(NoClockVisibility));
        OnPropertyChanged(nameof(ClockSelectorVisibility));
    }

    private void OnClockSwitched()
    {
        OnPropertyChanged(nameof(SelectedClock));
        OnPropertyChanged(nameof(ClockEdit));
        OnPropertyChanged(nameof(FontWeight));
        OnPropertyChanged(nameof(ClockFontFamily));
        RebuildOptions(); // Formatlisten mit Sprache und Werten dieser Uhr
    }

    /// <summary>Wirkt sofort auf der Leiste: Positionen werden direkt in der laufenden Konfiguration gelöscht.</summary>
    private void ResetFreePositions()
    {
        var settings = _service.Current.Clone();
        var bar = settings.FindBar(Bar.Id);
        if (bar is null)
        {
            StatusText = "Diese Leiste ist noch nicht übernommen.";
            return;
        }
        foreach (var widget in bar.Widgets)
        {
            widget.FreeX = null;
            widget.FreeY = null;
        }
        _service.Save(settings);
        StatusText = "Freie Positionen zurückgesetzt.";
    }

    private void AddWidget()
    {
        if (NewWidget is null) return;
        var entry = new WidgetEntry { Type = NewWidget.Type, Enabled = true, Position = 0.75 };
        _expandedWidgets.Add(entry.Id); // neues Widget gleich zum Einstellen öffnen
        if (entry.IsFixed)
        {
            // Bevorzugt einen noch freien Bereich wählen.
            var used = WidgetRows.Where(r => r.IsFixed).Select(r => r.Slot).ToHashSet();
            entry.Slot = new[] { WidgetSlot.Center, WidgetSlot.Right, WidgetSlot.Left }.FirstOrDefault(s => !used.Contains(s), WidgetSlot.Right);
        }
        WidgetRows.Add(CreateRow(entry));
        RebuildClockRows();
        NewWidget = null;
        StatusText = "Widget hinzugefügt – mit „Übernehmen“ auf die Leiste bringen.";
    }

    private void RebuildAudioRows()
    {
        FillAudioRows(OutputDeviceRows, AudioFlow.Output);
        FillAudioRows(InputDeviceRows, AudioFlow.Input);
    }

    private void FillAudioRows(System.Collections.ObjectModel.ObservableCollection<AudioDeviceRowViewModel> rows, AudioFlow flow)
    {
        // Bereits bearbeitete, noch nicht gespeicherte Werte beim Aktualisieren behalten.
        var pending = rows.ToDictionary(r => r.Device.Id, StringComparer.OrdinalIgnoreCase);
        rows.Clear();
        foreach (var device in _audioService.GetDevices(flow))
        {
            var row = new AudioDeviceRowViewModel(device, _settings.Audio.Find(device.Id));
            if (pending.TryGetValue(device.Id, out var old))
            {
                row.Icon = old.Icon;
                row.Label = old.Label;
            }
            rows.Add(row);
        }
    }

    /// <summary>Widget-Zeilen der ausgewählten Leiste zurück in deren Einstellungen schreiben.</summary>
    private void SyncWidgetRows()
    {
        // Positionen aus der laufenden Konfiguration übernehmen: Sie können sich per Drag & Drop
        // geändert haben, während dieses Fenster offen war.
        var live = _service.Current.Bars.SelectMany(b => b.Widgets).ToDictionary(w => w.Id);
        Bar.Widgets = WidgetRows
            .Select(r => r.ToEntry(live.GetValueOrDefault(r.Id)))
            .ToList();
    }

    private void SyncRowsToSettings()
    {
        SyncWidgetRows();

        // Update-Entscheidungen ("Später"/"Nein") werden vom Update-Dienst gespeichert – nicht überschreiben.
        // Position/Größe dynamischer Leisten: Wurde sie hier nicht geändert, gilt der Stand aus der laufenden
        // Konfiguration (sie kann per Maus verschoben worden sein, während dieses Fenster offen war).
        foreach (var bar in _settings.Bars)
        {
            var liveBar = _service.Current.FindBar(bar.Id);
            if (liveBar is null || !_geometrySnapshot.TryGetValue(bar.Id, out var snapshot) || Geometry(bar) != snapshot) continue;
            bar.Appearance.FloatingLeft = liveBar.Appearance.FloatingLeft;
            bar.Appearance.FloatingTop = liveBar.Appearance.FloatingTop;
            bar.Appearance.Width = liveBar.Appearance.Width;
            bar.Appearance.FloatingHeight = liveBar.Appearance.FloatingHeight;
            bar.MonitorSelection = liveBar.MonitorSelection;
        }

        var liveUpdates = _service.Current.Updates;
        _settings.Updates.SkippedVersion = liveUpdates.SkippedVersion;
        _settings.Updates.RemindAfterUtc = liveUpdates.RemindAfterUtc;
        _settings.Updates.LastCheckUtc = liveUpdates.LastCheckUtc;

        foreach (var row in OutputDeviceRows.Concat(InputDeviceRows))
        {
            var entry = _settings.Audio.Find(row.Device.Id);
            if (!row.IsCustomized)
            {
                if (entry is not null) _settings.Audio.Devices.Remove(entry); // Standardwerte nicht speichern
                continue;
            }
            if (entry is null)
            {
                entry = new AudioDeviceSettings { Id = row.Device.Id };
                _settings.Audio.Devices.Add(entry);
            }
            entry.Name = row.Device.Name;
            entry.Icon = row.Icon;
            entry.Label = row.Label.Trim();
        }
    }

    public event EventHandler? CloseRequested;

    public ICommand ApplyCommand { get; }
    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand OpenSettingsFileCommand { get; }
    public ICommand OpenLogFolderCommand { get; }
    public ICommand RefreshMonitorsCommand { get; }
    public ICommand RefreshProcessesCommand { get; }
    public ICommand AddExcludedCommand { get; }
    public ICommand AddForcedCommand { get; }
    public ICommand ExitApplicationCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand OpenReleasesCommand { get; }

    public string CurrentVersionText => $"Installierte Version: {Updates.UpdateService.CurrentVersion}";

    private string _updateStatus = "";
    public string UpdateStatus { get => _updateStatus; private set => SetProperty(ref _updateStatus, value); }

    private string DescribeLastCheck() =>
        _service.Current.Updates.LastCheckUtc is { } last
            ? $"Zuletzt geprüft: {last.ToLocalTime():dd.MM.yyyy HH:mm}"
            : "Noch nicht geprüft.";

    public AppSettings Settings => _settings;

    public string SettingsFilePath => _service.FilePath;
    public string LogDirectory => Log.LogDirectory ?? "-";
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }

    // ---- Auswahllisten --------------------------------------------------------------------
    public IReadOnlyList<string> FontFamilies { get; }
    public IReadOnlyList<string> ClockFontFamilies { get; }
    public IReadOnlyList<Option> FontWeightOptions => SettingsOptions.FontWeights;
    public IReadOnlyList<int> HeightPresets => SettingsOptions.HeightPresets;
    public IReadOnlyList<double> FontSizePresets => SettingsOptions.FontSizePresets;
    public IReadOnlyList<double> DateFontSizePresets => SettingsOptions.DateFontSizePresets;

    public IReadOnlyList<Option> TimeFormatOptions { get => _timeFormatOptions; private set => SetProperty(ref _timeFormatOptions, value); }
    public IReadOnlyList<Option> DateFormatOptions { get => _dateFormatOptions; private set => SetProperty(ref _dateFormatOptions, value); }
    public IReadOnlyList<Option> WeekFormatOptions { get => _weekFormatOptions; private set => SetProperty(ref _weekFormatOptions, value); }
    public IReadOnlyList<Option> CultureOptions { get => _cultureOptions; private set => SetProperty(ref _cultureOptions, value); }
    public IReadOnlyList<IntOption> ShowDelayOptions { get => _showDelayOptions; private set => SetProperty(ref _showDelayOptions, value); }
    public IReadOnlyList<IntOption> SafetyIntervalOptions { get => _safetyIntervalOptions; private set => SetProperty(ref _safetyIntervalOptions, value); }
    public IReadOnlyList<IntOption> BorderThicknessOptions { get => _borderThicknessOptions; private set => SetProperty(ref _borderThicknessOptions, value); }
    public IReadOnlyList<MonitorInfo> Monitors { get => _monitors; private set => SetProperty(ref _monitors, value); }
    public IReadOnlyList<Option> MonitorOptions { get => _monitorOptions; private set => SetProperty(ref _monitorOptions, value); }
    public IReadOnlyList<string> RunningProcesses { get => _runningProcesses; private set => SetProperty(ref _runningProcesses, value); }

    // ---- Ausgewählte Werte ----------------------------------------------------------------
    public string TimeFormat
    {
        get => ClockEdit.TimeFormat;
        set
        {
            if (string.IsNullOrEmpty(value)) return;
            ClockEdit.TimeFormat = value;
            _selectedClock?.RefreshSummary();
            ClockEdit.ShowSeconds = ClockSettings.HasSeconds(value);
            OnPropertyChanged();
        }
    }

    public string DateFormat
    {
        get => ClockEdit.DateFormat;
        set => SetIfNotEmpty(value, v => ClockEdit.DateFormat = v);
    }

    public string WeekNumberFormat
    {
        get => ClockEdit.WeekNumberFormat;
        set => SetIfNotEmpty(value, v => ClockEdit.WeekNumberFormat = v);
    }

    public string Culture
    {
        get => ClockEdit.Culture;
        set
        {
            if (value is null || value == ClockEdit.Culture) return;
            ClockEdit.Culture = value;
            OnPropertyChanged();
            RebuildOptions(); // Vorschauen in der neuen Sprache
        }
    }

    public string FontWeight
    {
        get => ClockEdit.FontWeight;
        set => SetIfNotEmpty(value, v => ClockEdit.FontWeight = v);
    }

    public string AppearanceFontFamily
    {
        get => Bar.Appearance.FontFamily;
        set => SetIfNotEmpty(value, v => Bar.Appearance.FontFamily = v);
    }

    public string ClockFontFamily
    {
        get => string.IsNullOrEmpty(ClockEdit.FontFamily) ? DefaultFontLabel : ClockEdit.FontFamily;
        set => SetIfNotEmpty(value, v => ClockEdit.FontFamily = v == DefaultFontLabel ? "" : v);
    }

    public string MonitorSelection
    {
        get => Bar.MonitorSelection;
        set => SetIfNotEmpty(value, v => Bar.MonitorSelection = v);
    }

    public string ExcludedProcessesText
    {
        get => string.Join(Environment.NewLine, _settings.Behavior.FullscreenExcludedProcesses);
        set => _settings.Behavior.FullscreenExcludedProcesses = SplitLines(value);
    }

    public string ForcedProcessesText
    {
        get => string.Join(Environment.NewLine, _settings.Behavior.FullscreenForcedProcesses);
        set => _settings.Behavior.FullscreenForcedProcesses = SplitLines(value);
    }

    public string? ExcludeCandidate { get => _excludeCandidate; set => SetProperty(ref _excludeCandidate, value); }
    public string? ForceCandidate { get => _forceCandidate; set => SetProperty(ref _forceCandidate, value); }

    private void Apply()
    {
        SyncRowsToSettings();
        _service.Save(_settings.Clone());
        ReplaceSettings(_service.Current.Clone()); // zeigt korrigierte/begrenzte Werte an
        StatusText = $"Gespeichert um {DateTime.Now:HH:mm:ss}";
    }

    private void ReplaceSettings(AppSettings settings)
    {
        // Ausgewählte Leiste beibehalten (Index kann sich nach dem Entfernen einer anderen verschoben haben).
        string selectedId = Bar.Id;
        _settings = settings;
        _barIndex = Math.Max(0, settings.Bars.FindIndex(b => b.Id == selectedId));
        _geometrySnapshot = Geometry(settings);
        RebuildBarOptions();
        RebuildOptions();
        RefreshMonitors();
        RebuildWidgetRows();
        OutputDeviceRows.Clear(); // keine "ungespeicherten" Werte übernehmen
        InputDeviceRows.Clear();
        RebuildAudioRows();
        OnPropertyChanged(string.Empty); // alle Bindungen neu lesen
    }

    private void RebuildOptions()
    {
        var culture = ClockSettings.ResolveCulture(ClockEdit.Culture);

        TimeFormatOptions = SettingsOptions.BuildTimeFormats(culture, ClockEdit.TimeFormat);
        DateFormatOptions = SettingsOptions.BuildDateFormats(culture, ClockEdit.DateFormat);
        WeekFormatOptions = SettingsOptions.BuildWeekFormats(culture, ClockEdit.WeekNumberFormat);
        CultureOptions = SettingsOptions.BuildCultures(ClockEdit.Culture);
        ShowDelayOptions = SettingsOptions.BuildDelays(_settings.Behavior.FullscreenShowDelayMs);
        SafetyIntervalOptions = SettingsOptions.BuildSafetyIntervals(_settings.Behavior.FullscreenSafetyCheckIntervalMs);
        BorderThicknessOptions = SettingsOptions.BuildBorderThickness(Bar.Appearance.BorderThickness);

        // Nach dem Austausch der Listen die Auswahl erneut setzen.
        OnPropertyChanged(nameof(TimeFormat));
        OnPropertyChanged(nameof(DateFormat));
        OnPropertyChanged(nameof(WeekNumberFormat));
        OnPropertyChanged(nameof(Culture));
    }

    private void RefreshMonitors()
    {
        Monitors = MonitorService.GetMonitors();
        var options = new List<Option> { new(MonitorService.PrimarySelection, "Primärer Monitor (automatisch)") };
        options.AddRange(Monitors.Select(m => new Option(m.DeviceName, m.Description)));

        // Gespeicherter, aktuell nicht angeschlossener Monitor bleibt auswählbar.
        var selected = Bar.MonitorSelection;
        if (!options.Any(o => o.Value.Equals(selected, StringComparison.OrdinalIgnoreCase)))
            options.Add(new Option(selected, $"{selected} (nicht verbunden – primärer Monitor wird verwendet)"));

        MonitorOptions = options;
        OnPropertyChanged(nameof(MonitorSelection));
    }

    private void AddProcess(List<string> target, string? name, string textProperty)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!target.Contains(name, StringComparer.OrdinalIgnoreCase)) target.Add(name);
        OnPropertyChanged(textProperty);
    }

    private void SetIfNotEmpty(string? value, Action<string> assign, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (string.IsNullOrEmpty(value)) return;
        assign(value);
        OnPropertyChanged(propertyName);
    }

    private static List<string> SplitLines(string? text) =>
        BehaviorSettings.NormalizeProcessList((text ?? "").Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries));

    private static void OpenWithShell(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning("Settings", $"'{path}' konnte nicht geöffnet werden: {ex.Message}");
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
            }
            catch
            {
                // Nichts weiter möglich.
            }
        }
    }
}
