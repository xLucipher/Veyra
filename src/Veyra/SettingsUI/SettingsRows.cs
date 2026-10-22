using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Veyra.Audio;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.SettingsUI;

public sealed record EnumOption(object Value, string Display);


/// <param name="PathData">Statt eines Schriftzeichens eine eigene 24×24-Kontur (z. B. die Sanduhr des Timers).</param>
/// <param name="Preset">Voreinstellung für neue Widgets – so kann ein Typ mehrfach in der Auswahl stehen (Mikrofon stumm).</param>
public sealed record WidgetInfo(WidgetType Type, string Name, string Description, string Glyph, string? PathData = null,
    Action<WidgetEntry>? Preset = null);

/// <summary>Eine Kategorie der Widget-Auswahl („Widget hinzufügen“).</summary>
public sealed record WidgetCatalogGroup(string Name, IReadOnlyList<WidgetInfo> Items);

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
        new(WidgetType.GpuMemory, "Videospeicher (VRAM)", "Belegter Grafikspeicher in Prozent oder GB", ""),
        new(WidgetType.GpuClock, "GPU-Takt", "Aktueller Takt der Grafikkarte in MHz", ""),
        new(WidgetType.GpuFan, "GPU-Lüfter", "Lüfterdrehzahl der Grafikkarte, sofern der Treiber sie meldet", ""),
        new(WidgetType.Fps, "FPS", "Bilder pro Sekunde des Spiels im Vordergrund, optional mit Frametime und 1 % Low – ohne Overlay im Spiel", "\uE7FC"),
        new(WidgetType.Network, "Netzwerk","LAN/WLAN mit Status, Netzwerkname und Signal – Klick zeigt IP-Adresse, Adapter und Geschwindigkeit", "\uE701"),
        new(WidgetType.NetworkTraffic, "Upload / Download", "Aktuelle Datenrate in Echtzeit, optional mit Verlauf", "\uE174"),
        new(WidgetType.Volume, "Lautstärke", "Systemlautstärke – Mausrad ändert, Klick öffnet den Regler, Rechtsklick schaltet stumm", "\uE995"),
        new(WidgetType.Timer, "Timer", "Beliebig viele Countdown-Timer mit Benachrichtigung und Signalton", "", Widgets.Timers.TimerIcons.HourglassData),
        new(WidgetType.Stopwatch, "Stoppuhr", "Starten, pausieren, Rundenzeiten – Bedienung im Popup", "\uE916"),
        new(WidgetType.Countdown, "Countdown", "Verbleibende Zeit bis zu einem Datum, z. B. „Urlaub: 12 Tage“", "\uE787"),
        new(WidgetType.Media, "Medien", "Gerade gespielter Titel mit Cover – Zurück, Wiedergabe/Pause, Weiter (Spotify, Browser …)", "\uE8D6"),
        new(WidgetType.Battery, "Akku", "Ladestand, beim Laden mit Blitz – optional mit Restlaufzeit", "\uE83F"),
        new(WidgetType.Drive, "Laufwerk", "Belegter oder freier Speicher eines Laufwerks", "\uEDA2"),
        new(WidgetType.Image, "Bild", "Ein Bild aus einer Datei – z. B. Logo oder Foto, optional mit Klick-Aktion", "\uE91B"),
        new(WidgetType.Note, "Notiz", "Freier Text, per Klick direkt auf der Fläche bearbeitbar", "\uE70B"),
        new(WidgetType.Separator, "Trennlinie", "Linie, Punkt oder Abstand zwischen Widgets", "\uE738"),
        new(WidgetType.Draw, "Zeichnen", "Freihand auf den Bildschirm malen – umkreisen, unterstreichen, Pfeile; liegt immer über allem", "\uE76D"),
        new(WidgetType.Clipboard, "Zwischenablage", "Verlauf der zuletzt kopierten Texte, Bilder und Dateien – durchsuchen, anheften, per Klick einfügen", ""),
        new(WidgetType.WorldClock, "Weltuhr", "Uhrzeit in anderen Zeitzonen, z. B. New York und Tokio – alle Orte auch im Popup", ""),
        new(WidgetType.Launcher, "Schnellstart", "Programme, Ordner, Dateien und Webadressen per Klick öffnen – Dateien einfach auf das Widget ziehen", ""),
        new(WidgetType.Weather, "Wetter", "Temperatur und Wetterlage für deinen Ort, im Popup mit Stunden- und 7-Tage-Vorhersage", "",
            "M7.5 19h9.5a4 4 0 0 0 .4-8 5.5 5.5 0 0 0-10.6 1.4A3.3 3.3 0 0 0 7.5 19zM5.5 8.5a3.5 3.5 0 0 1 6-2.4M8.5 2.5v1.2M3.2 5l.9.8M2 9.5h1.2"),
        new(WidgetType.DeviceBattery, "Geräte-Akku", "Akkustand von Bluetooth-Geräten und kabellosen Razer-Mäusen und -Tastaturen, mit Warnung bei wenig Akku", ""),
        new(WidgetType.PublicIp, "Öffentliche IP", "Deine öffentliche IPv4- und IPv6-Adresse – auf Wunsch verborgen, z. B. beim Streamen; Klick zeigt und kopiert", ""),
        new(WidgetType.Brightness, "Helligkeit & Nachtmodus", "Bildschirmhelligkeit per Mausrad oder Regler, Nachtmodus mit warmem Licht – auch nach Zeitplan", ""),
    ];

    public static WidgetInfo Get(WidgetType type) => All.First(w => w.Type == type);

    /// <summary>Lautstärke-Widget für das Mikrofon, fertig zum Stummschalten per Tastenkürzel.</summary>
    public static readonly WidgetInfo Microphone = new(WidgetType.Volume, "Mikrofon stumm",
        "Mikrofon per Tastenkürzel stummschalten – rot hervorgehoben, mit Anzeige und Signalton auch im Spiel", "",
        Preset: entry => entry.Volume = new VolumeSettings { Flow = AudioFlow.Input, ShowPercent = false });

    /// <summary>Eintrag für die Anzeige eines Widgets: Lautstärke-Widgets fürs Mikrofon erscheinen als „Mikrofon stumm“.</summary>
    public static WidgetInfo For(WidgetType type, VolumeSettings? volume) =>
        type == WidgetType.Volume && volume?.Flow == AudioFlow.Input ? Microphone : Get(type);

    /// <summary>Kategorien der Auswahl in Anzeigereihenfolge. Neue Widgets hier einer Kategorie zuordnen.</summary>
    private static readonly (string Name, WidgetInfo[] Items)[] Categories =
    [
        ("Zeit & Wetter", [Get(WidgetType.Clock), Get(WidgetType.WorldClock), Get(WidgetType.Weather), Get(WidgetType.Timer),
            Get(WidgetType.Stopwatch), Get(WidgetType.Countdown)]),
        ("Audio & Medien", [Get(WidgetType.Media), Get(WidgetType.Volume), Microphone, Get(WidgetType.AudioOutputs), Get(WidgetType.AudioInputs)]),
        ("System", [Get(WidgetType.Cpu), Get(WidgetType.Gpu), Get(WidgetType.Ram), Get(WidgetType.GpuTemperature), Get(WidgetType.GpuMemory),
            Get(WidgetType.GpuClock), Get(WidgetType.GpuFan), Get(WidgetType.Fps), Get(WidgetType.Battery), Get(WidgetType.DeviceBattery), Get(WidgetType.Drive),
            Get(WidgetType.Brightness)]),
        ("Netzwerk", [Get(WidgetType.Network), Get(WidgetType.NetworkTraffic), Get(WidgetType.PublicIp)]),
        ("Gestaltung", [Get(WidgetType.Image), Get(WidgetType.Note), Get(WidgetType.Separator)]),
        ("Werkzeuge", [Get(WidgetType.Clipboard), Get(WidgetType.Launcher), Get(WidgetType.Draw)]),
    ];

    /// <summary>Widgets nach Kategorien; <paramref name="search"/> filtert nach Name, Beschreibung oder Kategorie.</summary>
    public static IReadOnlyList<WidgetCatalogGroup> Groups(string search)
    {
        search = search.Trim();
        bool Matches(string category, WidgetInfo w) => search.Length == 0 ||
            w.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
            w.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase) ||
            category.Contains(search, StringComparison.CurrentCultureIgnoreCase);

        var groups = Categories
            .Select(c => new WidgetCatalogGroup(c.Name, c.Items.Where(w => Matches(c.Name, w)).ToList()))
            .ToList();
        // Noch keiner Kategorie zugeordnete Widgets nicht verlieren.
        var uncategorized = All.Where(w => !Categories.Any(c => c.Items.Contains(w)) && Matches("Weitere", w)).ToList();
        if (uncategorized.Count > 0) groups.Add(new WidgetCatalogGroup("Weitere", uncategorized));
        return groups.Where(g => g.Items.Count > 0).ToList();
    }

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

    public static IReadOnlyList<EnumOption> CountdownStyles { get; } =
    [
        new(CountdownStyle.Days, "Tage („12 Tage“)"),
        new(CountdownStyle.DaysHours, "Tage und Stunden („12 T 4 Std.“)"),
        new(CountdownStyle.Clock, "Mit Sekunden („12 T 04:31:10“)"),
    ];

    public static IReadOnlyList<EnumOption> CountdownRepeats { get; } =
    [
        new(CountdownRepeat.None, "Nicht wiederholen"),
        new(CountdownRepeat.Daily, "Täglich"),
        new(CountdownRepeat.Weekly, "Wöchentlich"),
        new(CountdownRepeat.Monthly, "Monatlich"),
        new(CountdownRepeat.Yearly, "Jährlich"),
    ];

    public static IReadOnlyList<EnumOption> SeparatorStyles { get; } =
    [
        new(SeparatorStyle.Line, "Linie"),
        new(SeparatorStyle.Dot, "Punkt"),
        new(SeparatorStyle.Space, "Nur Abstand"),
    ];

    public static IReadOnlyList<EnumOption> TextAlignments { get; } =
    [
        new(TextAlignment.Left, "Links"),
        new(TextAlignment.Center, "Mitte"),
        new(TextAlignment.Right, "Rechts"),
    ];

    public static IReadOnlyList<double> NoteFontSizes { get; } = [10, 11, 12, 13, 14, 16, 18, 20, 24, 28, 32, 40];
    public static IReadOnlyList<double> NoteWidthPresets { get; } = [0, 120, 160, 200, 260, 320, 400, 600];
    public static IReadOnlyList<double> ImageHeightPresets { get; } = [16, 20, 24, 28, 32, 40, 48, 64, 96, 128, 200];
    public static IReadOnlyList<double> SeparatorLengthPresets { get; } = [8, 12, 16, 20, 24, 32, 48, 64, 100];

    public static IReadOnlyList<EnumOption> Slots { get; } =
    [
        new(WidgetSlot.Left, "Links"),
        new(WidgetSlot.Center, "Mitte"),
        new(WidgetSlot.Right, "Rechts"),
    ];

    public static IReadOnlyList<double> MarginPresets { get; } = [0, 4, 8, 12, 16, 24, 32, 48, 64];

    public static IReadOnlyList<double> SpacingPresets { get; } = [0, 4, 8, 12, 16, 24, 32, 48];

    public static IReadOnlyList<double> ScalePresets { get; } = [50, 75, 100, 125, 150, 200, 250, 300, 400, 500];

    public static IReadOnlyList<int> ClipboardSizes { get; } = [10, 25, 50, 100];

    public static IReadOnlyList<EnumOption> IpVersions { get; } =
    [
        new(IpVersionDisplay.IPv4, "IPv4"),
        new(IpVersionDisplay.IPv6, "IPv6"),
        new(IpVersionDisplay.Both, "IPv4 und IPv6"),
    ];

    public static IReadOnlyList<IntOption> PublicIpIntervals { get; } =
    [
        new(5, "5 Minuten"),
        new(15, "15 Minuten"),
        new(30, "30 Minuten"),
        new(60, "1 Stunde"),
        new(360, "6 Stunden"),
    ];

    public static IReadOnlyList<EnumOption> TemperatureUnits { get; } =
    [
        new(TemperatureUnit.Celsius, "Celsius (°C)"),
        new(TemperatureUnit.Fahrenheit, "Fahrenheit (°F)"),
    ];

    public static IReadOnlyList<EnumOption> WindUnits { get; } =
    [
        new(WindSpeedUnit.KilometersPerHour, "km/h"),
        new(WindSpeedUnit.MetersPerSecond, "m/s"),
        new(WindSpeedUnit.MilesPerHour, "mph"),
    ];

    public static IReadOnlyList<IntOption> WeatherIntervals { get; } =
    [
        new(15, "15 Minuten"),
        new(30, "30 Minuten"),
        new(60, "1 Stunde"),
        new(120, "2 Stunden"),
    ];

    public static IReadOnlyList<IntOption> NightTemperatures { get; } =
    [
        new(5500, "5500 K – kaum wärmer"),
        new(4500, "4500 K – leicht warm"),
        new(3800, "3800 K – warm"),
        new(3400, "3400 K – sehr warm"),
        new(3000, "3000 K – Kerzenlicht"),
        new(2700, "2700 K – am wärmsten"),
    ];

    private static IReadOnlyList<Option>? _timeZones;

    /// <summary>Alle Zeitzonen dieses Systems, sortiert nach Abstand zu UTC.</summary>
    public static IReadOnlyList<Option> TimeZones => _timeZones ??= TimeZoneInfo.GetSystemTimeZones()
        .Select(z => new Option(z.Id, z.DisplayName)).ToList();
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
    /// <param name="hotkeyChanged">Tastenkürzel geändert – Konflikte neu prüfen.</param>
    public WidgetRowViewModel(WidgetEntry entry, IReadOnlyList<AudioDevice> devices, AudioSettings audio, string barForeground,
        Action<WidgetRowViewModel> remove, Action? hotkeyChanged = null)
    {
        _entry = entry;
        _hotkey = entry.Hotkey;
        _hotkeyChanged = hotkeyChanged;
        _info = WidgetCatalog.For(entry.Type, entry.Volume);
        _title = _info.Name;
        _slot = entry.Slot;
        _enabled = entry.Enabled;
        _outerMargin = entry.OuterMargin;
        _innerSpacing = entry.InnerSpacing;
        _scalePercent = Math.Round(entry.Scale * 100);
        _allDevices = entry.Devices.Count == 0;
        _allBatteryDevices = entry.Devices.Count == 0;
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
        Image = entry.Image?.Clone() ?? new ImageSettings();
        Note = entry.Note?.Clone() ?? new NoteSettings();
        _noteSnapshot = Note.Text;
        _noteBackground = Note.Background ?? NoteSettings.DefaultBackground;
        Media = entry.Media?.Clone() ?? new MediaSettings();
        Countdown = entry.Countdown?.Clone() ?? new CountdownSettings();
        _countdownTime = Countdown.Target.ToString("HH:mm", CultureInfo.CurrentCulture);
        Separator = entry.Separator?.Clone() ?? new SeparatorSettings();
        Draw = entry.Draw?.Clone() ?? new DrawSettings();
        Clipboard = entry.Clipboard?.Clone() ?? new ClipboardSettings();
        _pinnedSnapshot = string.Join("\u0001", Clipboard.Pinned);
        WorldClock = entry.WorldClock?.Clone() ?? new WorldClockSettings();
        foreach (var zone in WorldClock.Zones) ZoneRows.Add(new WorldClockZoneRow(zone, RemoveZoneRow));
        Launcher = entry.Launcher?.Clone() ?? new LauncherSettings();
        foreach (var item in Launcher.Items) LauncherRows.Add(new LauncherItemRow(item, RemoveLauncherRow));
        _launcherSnapshot = LauncherSignature(Launcher.Items);
        Weather = entry.Weather?.Clone() ?? new WeatherSettings();
        DeviceBattery = entry.DeviceBattery?.Clone() ?? new DeviceBatterySettings();
        PublicIp = entry.PublicIp?.Clone() ?? new PublicIpSettings();
        Brightness = entry.Brightness?.Clone() ?? new BrightnessSettings();
        Fps = entry.Fps?.Clone() ?? new FpsSettings();
        GrantFpsCommand = new RelayCommand(() => _ = GrantFpsAsync());
        AddZoneCommand = new RelayCommand(AddZoneRow);
        AddLauncherItemCommand = new RelayCommand(() => AddLauncherRow(false));
        AddLauncherFolderCommand = new RelayCommand(() => AddLauncherRow(true));
        AddLauncherUrlCommand = new RelayCommand(() =>
        {
            LauncherRows.Add(new LauncherItemRow(new LauncherItem { Path = "https://" }, RemoveLauncherRow));
            OnPropertyChanged(nameof(Summary));
        });
        SearchLocationCommand = new RelayCommand(() => _ = SearchLocationAsync());
        BrowseImageCommand = new RelayCommand(BrowseImage);
        AddTimerCommand = new RelayCommand(AddTimerRow);
        foreach (var action in WidgetHotkeys.For(entry.Type))
            HotkeyActions.Add(new HotkeyActionRow(action, entry.Hotkeys?.GetValueOrDefault(action.Id), () =>
            {
                _hotkeyChanged?.Invoke();
                OnPropertyChanged(nameof(Summary));
            }));
        RemoveCommand = new RelayCommand(() => remove(this));
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

    private WidgetInfo _info;

    /// <summary>Name, Symbol und Beschreibung; ändert sich beim Lautstärke-Widget mit dem Gerät (Mikrofon stumm).</summary>
    public WidgetInfo Info { get => _info; private set => SetProperty(ref _info, value); }
    public ICommand RemoveCommand { get; }

    /// <summary>
    /// Zweite Zeile in der Widget-Liste: Kurzinfo zum Unterscheiden gleichartiger Widgets
    /// (Audio: Geräte und Tastenkürzel, Uhr: Bereich und Zeitformat).
    /// </summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            if (IsAudio)
            {
                var selected = Devices.Where(d => d.Selected).Select(d => d.Name).ToList();
                parts.Add(AllDevices || selected.Count == 0 ? "Alle aktiven Geräte" : string.Join(", ", selected));
                if (Core.Hotkey.TryParse(Hotkey, out var hotkey)) parts.Add(hotkey.DisplayText);
            }
            else if (IsMetric)
            {
                if (Is(WidgetType.Drive)) parts.Add($"Laufwerk {Metric.Drive.TrimEnd('\\')}");
                parts.Add(WidgetCatalog.MetricStyles.FirstOrDefault(s => Equals(s.Value, Metric.Style))?.Display ?? "");
                if (_entry.Type is WidgetType.Ram or WidgetType.GpuMemory && Metric.ShowAmount) parts.Add("in GB");
                if (_entry.Type == WidgetType.Drive && Metric.ShowAmount) parts.Add("freier Speicher");
                if (_entry.Type == WidgetType.Battery && Metric.ShowAmount) parts.Add("Restlaufzeit");
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
            else if (Is(WidgetType.Image))
            {
                parts.Add(Image.Path.Length == 0 ? "kein Bild gewählt" : System.IO.Path.GetFileName(Image.Path));
                if (Image.ClickTarget.Length > 0) parts.Add("mit Klick-Aktion");
            }
            else if (Is(WidgetType.Note))
            {
                string firstLine = Note.Text.Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
                parts.Add(firstLine.Length == 0 ? "leer" : firstLine.Length > 40 ? firstLine[..40] + " …" : firstLine);
            }
            else if (Is(WidgetType.Countdown))
            {
                if (Countdown.Title.Length > 0) parts.Add(Countdown.Title);
                parts.Add(Countdown.Repeat == CountdownRepeat.None
                    ? Countdown.Target.ToString("d. MMM yyyy, HH:mm", CultureInfo.CurrentCulture)
                    : CountdownRepeatHint);
            }
            else if (Is(WidgetType.Media))
            {
                if (Media.ShowCover) parts.Add("mit Cover");
                if (Media.ShowControls) parts.Add("mit Steuerung");
                if (Media.FixedTextWidth) parts.Add("feste Breite");
            }
            else if (Is(WidgetType.Separator))
            {
                parts.Add(WidgetCatalog.SeparatorStyles.FirstOrDefault(s => Equals(s.Value, Separator.Style))?.Display ?? "");
                parts.Add(Separator.Horizontal ? "waagerecht" : "senkrecht");
            }
            else if (Is(WidgetType.Clipboard))
            {
                parts.Add($"{Clipboard.MaxItems} Einträge");
                if (Clipboard.Pinned.Count > 0) parts.Add($"{Clipboard.Pinned.Count} angeheftet");
                if (Clipboard.PasteOnSelect) parts.Add("fügt direkt ein");
            }
            else if (Is(WidgetType.WorldClock))
            {
                parts.Add(ZoneRows.Count == 0 ? "keine Zeitzone" : string.Join(", ", ZoneRows.Select(z => z.DisplayLabel)));
            }
            else if (Is(WidgetType.Launcher))
            {
                parts.Add(LauncherRows.Count == 1 ? "1 Eintrag" : $"{LauncherRows.Count} Einträge");
                if (Launcher.ShowNames) parts.Add("mit Namen");
            }
            else if (Is(WidgetType.Weather))
            {
                parts.Add(Weather.HasLocation ? Weather.LocationName : "kein Ort gewählt");
                parts.Add(Weather.Unit == TemperatureUnit.Celsius ? "°C" : "°F");
            }
            else if (Is(WidgetType.DeviceBattery))
            {
                var sources = new List<string>();
                if (DeviceBattery.Bluetooth) sources.Add("Bluetooth");
                if (DeviceBattery.Razer) sources.Add("Razer");
                parts.Add(sources.Count == 0 ? "keine Quelle" : string.Join(" + ", sources));
                parts.Add(AllBatteryDevices ? "alle Geräte" : $"{BatteryDevices.Count(d => d.Selected)} Gerät(e)");
            }
            else if (Is(WidgetType.PublicIp))
            {
                parts.Add(WidgetCatalog.IpVersions.FirstOrDefault(v => Equals(v.Value, PublicIp.Show))?.Display ?? "");
                if (PublicIp.Mask) parts.Add("verborgen");
            }
            else if (Is(WidgetType.Fps))
            {
                if (Fps.ShowFrameTime) parts.Add("mit Frametime");
                if (Fps.ShowOnePercentLow) parts.Add("mit 1 % Low");
                if (FpsAccessState != Gaming.FpsAccess.Granted) parts.Add("Messung noch nicht erlaubt");
            }
            else if (Is(WidgetType.Brightness))
            {
                parts.Add($"Nachtmodus {Brightness.NightTemperature} K");
                if (Brightness.NightSchedule) parts.Add($"{Brightness.NightFrom}–{Brightness.NightTo}");
            }
            else if (Is(WidgetType.Draw))
            {
                parts.Add(Draw.ToolbarHorizontal ? "Werkzeugleiste waagerecht" : "Werkzeugleiste senkrecht");
                if (Draw.ClearOnExit) parts.Add("löscht beim Beenden");
            }
            int hotkeys = HotkeyActions.Count(a => a.Hotkey is not null);
            if (hotkeys > 0) parts.Add(hotkeys == 1 ? "1 Tastenkürzel" : $"{hotkeys} Tastenkürzel");
            if (Math.Abs(ScalePercent - 100) > 0.5) parts.Add($"{ScalePercent:0} %");
            if (_entry.Locked) parts.Add("gesperrt");
            if (!Enabled) parts.Add("inaktiv");
            return string.Join("  ·  ", parts.Where(p => p.Length > 0));
        }
    }
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

    /// <summary>Nur Uhren: Einstellungen dieser Uhr (rechts neben der Widget-Liste, wenn die Uhr gewählt ist).</summary>
    public ClockSettings Clock { get; }

    // ---- Mess-Widgets (CPU, GPU, RAM, Temperatur) -----------------------------------------
    public bool IsMetric => _entry.IsMetric;
    public Visibility MetricVisibility => IsMetric ? Visibility.Visible : Visibility.Collapsed;

    private bool Is(WidgetType type) => _entry.Type == type;

    /// <summary>Eigene Farben: alle Widgets außer Uhr (eigene Uhr-Einstellungen) und Bild.</summary>
    public Visibility ColorsVisibility => IsClock || Is(WidgetType.Image) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Symbolfarbe: alle Widgets mit Symbol; beim Upload-/Download-Monitor die Uploadfarbe, bei Mess-Widgets die
    /// Bezeichnung, beim Countdown der Titel, bei der Trennlinie die Linie. Notizen haben nur eine Schriftfarbe.
    /// </summary>
    private bool HasIconColor => !IsClock && !Is(WidgetType.Image) && !Is(WidgetType.Note);

    /// <summary>Schriftfarbe: alle außer Uhr, Bild, Trennlinie und Zeichnen.</summary>
    private bool HasLabelColor => !IsClock && !Is(WidgetType.Image) && !Is(WidgetType.Separator) && !Is(WidgetType.Draw);

    /// <summary>Akzentfarbe: Markierung, Balken, Fortschritt bzw. Download.</summary>
    private bool HasIndicatorColor => IsAudio || IsMetric || Is(WidgetType.Volume) || Is(WidgetType.Timer) || Is(WidgetType.NetworkTraffic) || Is(WidgetType.Draw) || Is(WidgetType.DeviceBattery) || Is(WidgetType.Fps);

    public Visibility IconColorVisibility => HasIconColor ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LabelColorVisibility => HasLabelColor ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IndicatorColorVisibility => HasIndicatorColor ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Ohne eigene Symbolfarbe: Schriftfarbe der Fläche, beim Upload die Standard-Uploadfarbe.</summary>
    private string DefaultIconColor => Is(WidgetType.NetworkTraffic) ? WidgetEntry.DefaultUploadColor : _barForeground;

    public string IconColorTitle => _entry.Type switch
    {
        WidgetType.NetworkTraffic => "Uploadfarbe",
        WidgetType.Countdown => "Titelfarbe",
        WidgetType.Separator => "Linienfarbe",
        WidgetType.Battery => "Symbolfarbe",
        WidgetType.WorldClock => "Farbe der Ortsnamen",
        _ when IsMetric => "Bezeichnungsfarbe",
        _ => "Symbolfarbe",
    };

    public string LabelColorTitle => IsMetric ? "Wertfarbe" : "Schriftfarbe";

    public string IconColorHint => _entry.Type switch
    {
        WidgetType.NetworkTraffic => "Pfeil und Linie im Verlauf.",
        WidgetType.Network => "Bei Verbindung ohne Internetzugriff wird das Symbol orange.",
        WidgetType.GpuTemperature => "Für das Thermometer vor dem Wert.",
        WidgetType.Battery => "Für das Akkusymbol vor dem Wert.",
        WidgetType.Drive => "Für den Laufwerksbuchstaben vor dem Wert. Unabhängig von der Wertfarbe.",
        WidgetType.GpuMemory or WidgetType.GpuClock or WidgetType.GpuFan => "Für die Bezeichnung vor dem Wert. Unabhängig von der Wertfarbe.",
        WidgetType.Media => "Für die Schaltflächen Zurück, Wiedergabe und Weiter.",
        WidgetType.Countdown => "Für den Titel vor der Restzeit.",
        WidgetType.Separator => "Ohne eigene Farbe: Schriftfarbe der Fläche, etwas durchsichtig.",
        WidgetType.WorldClock => "Für die Ortsnamen vor der Uhrzeit.",
        WidgetType.Launcher => "Für Einträge ohne eigenes Symbol (Webadressen, fehlende Dateien).",
        WidgetType.Weather => "Für das Wettersymbol.",
        WidgetType.DeviceBattery => "Für die Gerätesymbole (Maus, Tastatur, Headset …).",
        WidgetType.PublicIp => "Für den Globus und „IPv4“/„IPv6“.",
        WidgetType.Brightness => "Für die Sonne – im Nachtmodus wird sie zum orangefarbenen Mond.",
        WidgetType.Fps => "Für „FPS“ vor dem Wert.",
        WidgetType.Clipboard => "Für das Symbol der Zwischenablage.",
        _ when IsMetric => "Für „CPU“, „GPU“ bzw. „RAM“ vor dem Wert. Unabhängig von der Wertfarbe.",
        _ => "",
    };

    public Visibility IconColorHintVisibility => IconColorHint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Schriftfarbe: Audio = Gerätenamen, sonst Wert bzw. Beschriftung.</summary>
    public string LabelColorHint => _entry.Type switch
    {
        WidgetType.AudioOutputs or WidgetType.AudioInputs => "Für die Gerätenamen (Audiogeräte → Beschriftung).",
        WidgetType.Network => "Für Netzwerkname und Signalstärke.",
        WidgetType.Volume => "Für den Prozentwert.",
        WidgetType.Timer => "Für die Restzeit. Abgelaufene Timer werden rot. Der Name folgt der Schriftfarbe der Fläche.",
        WidgetType.Stopwatch => "Für die Zeit.",
        WidgetType.NetworkTraffic => "Für die Datenraten.",
        WidgetType.Note => "Für den Text der Notiz.",
        WidgetType.Media => "Für Titel und Interpret.",
        WidgetType.Countdown => "Für die Restzeit.",
        WidgetType.Battery => "Für den Ladestand. Bei wenig Akku gelten die Warnfarben.",
        WidgetType.Drive => "Für den Wert. Bei fast vollem Laufwerk gelten die Warnfarben.",
        WidgetType.GpuMemory => "Für den Wert. Bei fast vollem Videospeicher gelten die Warnfarben.",
        WidgetType.Clipboard => "Für die Anzahl der Einträge (wenn eingeschaltet).",
        WidgetType.WorldClock => "Für die Uhrzeiten.",
        WidgetType.Launcher => "Für die Namen neben den Symbolen (wenn eingeschaltet).",
        WidgetType.Weather => "Für Temperatur und Text.",
        WidgetType.DeviceBattery => "Für Ladestand und Namen. Bei wenig Akku gelten die Warnfarben.",
        WidgetType.PublicIp => "Für die Adresse.",
        WidgetType.Brightness => "Für den Prozentwert.",
        WidgetType.Fps => "Für den Wert und die Zusatzangaben. Bei wenig FPS gelten die Warnfarben.",
        WidgetType.GpuClock or WidgetType.GpuFan => "Für den Wert.",
        _ => "Für den Wert, z. B. „42 %“. Bei hoher Last gelten die Warnfarben.",
    };

    public string IndicatorColorTitle => _entry.Type switch
    {
        WidgetType.AudioOutputs or WidgetType.AudioInputs => "Markierungsfarbe",
        WidgetType.Draw => "Farbe beim Zeichnen",
        WidgetType.Volume => "Balkenfarbe",
        WidgetType.Timer => "Fortschrittsfarbe",
        WidgetType.NetworkTraffic => "Downloadfarbe",
        _ => "Balken- und Verlaufsfarbe",
    };

    public string IndicatorColorHint => _entry.Type switch
    {
        WidgetType.AudioOutputs or WidgetType.AudioInputs => "Strich unter dem aktiven Standardgerät.",
        WidgetType.Draw => "Hintergrund des Stifts, solange gezeichnet wird.",
        WidgetType.DeviceBattery => "Balken im Popup. Bei wenig Akku orange bzw. rot.",
        WidgetType.Fps => "Verlauf im Popup.",
        WidgetType.Volume => "Füllbalken neben dem Symbol (wenn eingeschaltet).",
        WidgetType.Timer => "Balken unter der Restzeit, der bis zum Ablauf leerläuft.",
        WidgetType.NetworkTraffic => "Pfeil und gefüllter Verlauf.",
        WidgetType.Battery => "Bei wenig Akku orange und rot, wenn die Warnfarben an sind.",
        WidgetType.Drive => "Bei fast vollem Laufwerk orange und rot, wenn die Warnfarben an sind.",
        WidgetType.GpuMemory => "Bei fast vollem Videospeicher orange und rot, wenn die Warnfarben an sind.",
        WidgetType.GpuClock or WidgetType.GpuFan => "Balken und Verlauf.",
        _ => "Bei hoher Last bzw. Temperatur orange und rot, wenn die Warnfarben an sind.",
    };

    public string ResetColorsHint => _entry.Type switch
    {
        WidgetType.AudioOutputs or WidgetType.AudioInputs =>
            "Ohne eigene Farbe folgt das Widget der Schriftfarbe der Fläche (Tab „Aussehen“); die Markierung wird wieder blau.",
        WidgetType.NetworkTraffic =>
            "Ohne eigene Farbe folgt der Text der Schriftfarbe der Fläche (Tab „Aussehen“); Download wird wieder blau, Upload orange.",
        WidgetType.Network or WidgetType.Stopwatch or WidgetType.Note or WidgetType.Media or WidgetType.Countdown or WidgetType.Separator =>
            "Ohne eigene Farbe folgt das Widget der Schriftfarbe der Fläche (Tab „Aussehen“).",
        _ when IsMetric =>
            "Ohne eigene Farbe folgt das Widget der Schriftfarbe der Fläche (Tab „Aussehen“); Balken und Verlauf werden wieder blau.",
        _ => "Ohne eigene Farbe folgt das Widget der Schriftfarbe der Fläche (Tab „Aussehen“); Akzente werden wieder blau.",
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

    // ---- Bild, Notiz, Medien, Countdown, Trennlinie, Zeichnen-------------------------------------
    public ImageSettings Image { get; }
    public NoteSettings Note { get; }
    public MediaSettings Media { get; }
    public CountdownSettings Countdown { get; }
    public SeparatorSettings Separator { get; }
    public DrawSettings Draw { get; }

    public Visibility ImageVisibility => Is(WidgetType.Image) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility NoteVisibility => Is(WidgetType.Note) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility MediaVisibility => Is(WidgetType.Media) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CountdownVisibility => Is(WidgetType.Countdown) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SeparatorVisibility => Is(WidgetType.Separator) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DrawVisibility => Is(WidgetType.Draw) ? Visibility.Visible : Visibility.Collapsed;

    // ---- Zwischenablage, Weltuhr, Schnellstart, Wetter, Geräte-Akku, IP, Helligkeit --------
    public ClipboardSettings Clipboard { get; }
    public WorldClockSettings WorldClock { get; }
    public LauncherSettings Launcher { get; }
    public WeatherSettings Weather { get; }
    public DeviceBatterySettings DeviceBattery { get; }
    public PublicIpSettings PublicIp { get; }
    public BrightnessSettings Brightness { get; }
    public FpsSettings Fps { get; }

    public Visibility ClipboardVisibility => Is(WidgetType.Clipboard) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WorldClockVisibility => Is(WidgetType.WorldClock) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LauncherVisibility => Is(WidgetType.Launcher) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility WeatherVisibility => Is(WidgetType.Weather) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DeviceBatteryVisibility => Is(WidgetType.DeviceBattery) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PublicIpVisibility => Is(WidgetType.PublicIp) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BrightnessVisibility => Is(WidgetType.Brightness) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FpsVisibility => Is(WidgetType.Fps) ? Visibility.Visible : Visibility.Collapsed;

    // FPS: Berechtigung (Gruppe „Leistungsprotokollbenutzer“)
    private Gaming.FpsAccess? _fpsAccess;
    private string _fpsGrantStatus = "";

    private Gaming.FpsAccess FpsAccessState => _fpsAccess ??= Is(WidgetType.Fps) ? Gaming.FpsPermission.Check() : Gaming.FpsAccess.Granted;

    public ICommand GrantFpsCommand { get; }

    public string FpsAccessText => FpsAccessState switch
    {
        Gaming.FpsAccess.Granted => $"Erlaubt – dein Konto ist in der Gruppe „{Gaming.FpsPermission.GroupName}“.",
        Gaming.FpsAccess.NeedsSignIn => $"Dein Konto ist in der Gruppe „{Gaming.FpsPermission.GroupName}“ – bitte einmal ab- und wieder anmelden, dann wird gemessen.",
        _ => $"Windows gibt die Bildausgabe von Spielen nur an Administratoren und Mitglieder der Gruppe „{Gaming.FpsPermission.GroupName}“ heraus. " +
             "Der Button nimmt dein Konto einmalig auf (Windows fragt nach deiner Erlaubnis), danach einmal ab- und wieder anmelden. Die Gruppe gilt für alle Programme deines Kontos.",
    };

    public Visibility GrantFpsVisibility => FpsAccessState == Gaming.FpsAccess.Missing ? Visibility.Visible : Visibility.Collapsed;

    public string FpsGrantStatus
    {
        get => _fpsGrantStatus;
        private set
        {
            if (SetProperty(ref _fpsGrantStatus, value)) OnPropertyChanged(nameof(FpsGrantStatusVisibility));
        }
    }

    public Visibility FpsGrantStatusVisibility => FpsGrantStatus.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    private async Task GrantFpsAsync()
    {
        FpsGrantStatus = "Warte auf die Windows-Abfrage …";
        FpsGrantStatus = await Gaming.FpsPermission.RequestAsync();
        _fpsAccess = null;
        OnPropertyChanged(nameof(FpsAccessText));
        OnPropertyChanged(nameof(GrantFpsVisibility));
        OnPropertyChanged(nameof(Summary));
    }

    // Zwischenablage: Angeheftetes kann im Popup geändert worden sein, während dieses Fenster offen war.
    private readonly string _pinnedSnapshot;
    private bool _pinnedCleared;
    private ICommand? _clearPinnedCommand;

    public string PinnedText => Clipboard.Pinned.Count switch
    {
        0 => "Keine angehefteten Texte. Anheften geht im Popup über die Stecknadel.",
        1 => "1 angehefteter Text.",
        int n => $"{n} angeheftete Texte.",
    };

    public ICommand ClearPinnedCommand => _clearPinnedCommand ??= new RelayCommand(() =>
    {
        Clipboard.Pinned.Clear();
        _pinnedCleared = true;
        OnPropertyChanged(nameof(PinnedText));
        OnPropertyChanged(nameof(Summary));
    });

    public int ClipboardMaxItems
    {
        get => Clipboard.MaxItems;
        set
        {
            if (value <= 0 || value == Clipboard.MaxItems) return; // Listenaustausch schreibt kurz 0
            Clipboard.MaxItems = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    private ClipboardSettings BuildClipboard(WidgetEntry? live)
    {
        var clipboard = Clipboard.Clone();
        if (!_pinnedCleared && string.Join("\u0001", Clipboard.Pinned) == _pinnedSnapshot && live?.Clipboard is { } liveClipboard)
            clipboard.Pinned = [.. liveClipboard.Pinned];
        return clipboard;
    }

    // Weltuhr: bearbeitbare Liste der Zeitzonen
    public ObservableCollection<WorldClockZoneRow> ZoneRows { get; } = [];
    public ICommand AddZoneCommand { get; }

    private void AddZoneRow()
    {
        // Vorschlag: eine noch nicht gewählte, häufige Zeitzone.
        string[] suggestions = ["GMT Standard Time", "Eastern Standard Time", "Pacific Standard Time", "Tokyo Standard Time", "China Standard Time", "AUS Eastern Standard Time"];
        string id = suggestions.FirstOrDefault(s => ZoneRows.All(z => z.TimeZoneId != s) && WorldClockSettings.FindZone(s) is not null) ?? TimeZoneInfo.Utc.Id;
        ZoneRows.Add(new WorldClockZoneRow(new WorldClockZone { TimeZoneId = id }, RemoveZoneRow));
        OnPropertyChanged(nameof(Summary));
    }

    private void RemoveZoneRow(WorldClockZoneRow row)
    {
        ZoneRows.Remove(row);
        OnPropertyChanged(nameof(Summary));
    }

    private WorldClockSettings BuildWorldClock()
    {
        var clock = WorldClock.Clone();
        clock.Zones = ZoneRows.Select(r => r.ToZone()).ToList();
        return clock;
    }

    // Schnellstart: bearbeitbare Liste der Einträge
    private readonly string _launcherSnapshot;

    public ObservableCollection<LauncherItemRow> LauncherRows { get; } = [];
    public ICommand AddLauncherItemCommand { get; }
    public ICommand AddLauncherFolderCommand { get; }
    public ICommand AddLauncherUrlCommand { get; }

    private void AddLauncherRow(bool folder)
    {
        string? path = null;
        if (folder)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Ordner für den Schnellstart" };
            if (dialog.ShowDialog() == true) path = dialog.FolderName;
        }
        else
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Programm oder Datei für den Schnellstart",
                Filter = "Programme und Verknüpfungen|*.exe;*.lnk;*.url;*.bat;*.cmd|Alle Dateien|*.*",
                DereferenceLinks = false,
            };
            if (dialog.ShowDialog() == true) path = dialog.FileName;
        }
        if (path is null) return;
        LauncherRows.Add(new LauncherItemRow(new LauncherItem { Path = path }, RemoveLauncherRow));
        OnPropertyChanged(nameof(Summary));
    }

    private void RemoveLauncherRow(LauncherItemRow row)
    {
        LauncherRows.Remove(row);
        OnPropertyChanged(nameof(Summary));
    }

    private static string LauncherSignature(IEnumerable<LauncherItem> items) =>
        string.Join("|", items.Select(i => $"{i.Id}:{i.Path}:{i.Arguments}:{i.Name}"));

    /// <summary>Einträge können per Drag &amp; Drop auf das Widget hinzugekommen sein, während dieses Fenster offen war.</summary>
    private LauncherSettings BuildLauncher(WidgetEntry? live)
    {
        var launcher = Launcher.Clone();
        var rows = LauncherRows.Select(r => r.ToItem()).ToList();
        launcher.Items = LauncherSignature(rows) == _launcherSnapshot && live?.Launcher is { } liveLauncher
            ? liveLauncher.Items.Select(i => i.Clone()).ToList()
            : rows;
        return launcher;
    }

    // Wetter: Ort suchen (Open-Meteo-Geocoding)
    private string _locationQuery = "";
    private string _locationStatus = "";

    public ICommand SearchLocationCommand { get; }
    public ObservableCollection<Veyra.Weather.WeatherLocation> LocationResults { get; } = [];

    public string LocationQuery { get => _locationQuery; set => SetProperty(ref _locationQuery, value ?? ""); }

    public string LocationStatus
    {
        get => _locationStatus;
        private set
        {
            if (SetProperty(ref _locationStatus, value)) OnPropertyChanged(nameof(LocationStatusVisibility));
        }
    }

    public Visibility LocationStatusVisibility => LocationStatus.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LocationResultsVisibility => LocationResults.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public string LocationText => Weather.HasLocation
        ? string.Format(CultureInfo.CurrentCulture, "{0}  ({1:0.00}°, {2:0.00}°)", Weather.LocationName, Weather.Latitude, Weather.Longitude)
        : "Noch kein Ort gewählt – unten suchen.";

    private async Task SearchLocationAsync()
    {
        string query = LocationQuery.Trim();
        if (query.Length < 2)
        {
            LocationStatus = "Bitte mindestens zwei Buchstaben eingeben.";
            return;
        }
        LocationStatus = "Suche …";
        LocationResults.Clear();
        OnPropertyChanged(nameof(LocationResultsVisibility));
        var results = await Veyra.Weather.WeatherService.SearchLocationsAsync(query);
        foreach (var result in results) LocationResults.Add(result);
        LocationStatus = results.Count == 0 ? "Kein Ort gefunden (oder keine Verbindung)." : "Ort anklicken, um ihn zu übernehmen.";
        OnPropertyChanged(nameof(LocationResultsVisibility));
    }

    public Veyra.Weather.WeatherLocation? SelectedLocation
    {
        get => null;
        set
        {
            if (value is null) return;
            Weather.LocationName = value.ShortName;
            Weather.Latitude = Math.Round(value.Latitude, 4);
            Weather.Longitude = Math.Round(value.Longitude, 4);
            LocationResults.Clear();
            LocationStatus = "";
            OnPropertyChanged(nameof(LocationText));
            OnPropertyChanged(nameof(LocationResultsVisibility));
            OnPropertyChanged(nameof(Summary));
        }
    }

    public TemperatureUnit WeatherUnit
    {
        get => Weather.Unit;
        set
        {
            if (!Enum.IsDefined(value) || value == Weather.Unit) return;
            Weather.Unit = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public WindSpeedUnit WeatherWindUnit
    {
        get => Weather.WindUnit;
        set
        {
            if (!Enum.IsDefined(value) || value == Weather.WindUnit) return;
            Weather.WindUnit = value;
            OnPropertyChanged();
        }
    }

    public int WeatherInterval
    {
        get => Weather.IntervalMinutes;
        set
        {
            if (value <= 0 || value == Weather.IntervalMinutes) return;
            Weather.IntervalMinutes = value;
            OnPropertyChanged();
        }
    }

    // Geräte-Akku: Auswahl der Geräte (Kennung je Gerät, siehe DeviceBatteryService)
    private bool _allBatteryDevices;

    public ObservableCollection<DeviceChoice> BatteryDevices { get; } = [];

    public bool AllBatteryDevices
    {
        get => _allBatteryDevices;
        set
        {
            if (!SetProperty(ref _allBatteryDevices, value)) return;
            OnPropertyChanged(nameof(BatteryDeviceListEnabled));
            if (value) foreach (var device in BatteryDevices) device.Selected = true;
            OnPropertyChanged(nameof(Summary));
        }
    }

    public bool BatteryDeviceListEnabled => !AllBatteryDevices;

    public Visibility NoBatteryDevicesVisibility => BatteryDevices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Gefundene Geräte eintragen (im Hintergrund ermittelt; gewählte, gerade fehlende Geräte bleiben erhalten).</summary>
    public void SetBatteryDevices(IReadOnlyList<Veyra.Peripherals.BatteryDevice> found)
    {
        var selected = _entry.Devices;
        BatteryDevices.Clear();
        foreach (var device in found)
            BatteryDevices.Add(new DeviceChoice(device.Key, $"{device.Name}  ·  {device.SourceName}", "", connected: true,
                selected: _allBatteryDevices || selected.Contains(device.Key, StringComparer.OrdinalIgnoreCase)));
        foreach (var missing in selected.Where(k => found.All(d => !d.Key.Equals(k, StringComparison.OrdinalIgnoreCase))))
            BatteryDevices.Add(new DeviceChoice(missing, Veyra.Peripherals.BatteryDevice.NameFromKey(missing), "", connected: false, selected: true));
        OnPropertyChanged(nameof(NoBatteryDevicesVisibility));
        OnPropertyChanged(nameof(Summary));
    }

    public IpVersionDisplay PublicIpShow
    {
        get => PublicIp.Show;
        set
        {
            if (!Enum.IsDefined(value) || value == PublicIp.Show) return;
            PublicIp.Show = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public int PublicIpInterval
    {
        get => PublicIp.IntervalMinutes;
        set
        {
            if (value <= 0 || value == PublicIp.IntervalMinutes) return;
            PublicIp.IntervalMinutes = value;
            OnPropertyChanged();
        }
    }

    public int BrightnessStep
    {
        get => Brightness.WheelStep;
        set
        {
            if (value <= 0 || value == Brightness.WheelStep) return;
            Brightness.WheelStep = value;
            OnPropertyChanged();
        }
    }

    public int NightTemperature
    {
        get => Brightness.NightTemperature;
        set
        {
            if (value <= 0 || value == Brightness.NightTemperature) return;
            Brightness.NightTemperature = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public string NightFrom
    {
        get => Brightness.NightFrom;
        set
        {
            if (BrightnessSettings.ParseTime(value) is not { } time) return;
            Brightness.NightFrom = time.ToString(@"hh\:mm");
            OnPropertyChanged(nameof(Summary));
        }
    }

    public string NightTo
    {
        get => Brightness.NightTo;
        set
        {
            if (BrightnessSettings.ParseTime(value) is not { } time) return;
            Brightness.NightTo = time.ToString(@"hh\:mm");
            OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>Ein- und Ausschalten des Nachtmodus passiert im Popup – hier gilt der laufende Stand.</summary>
    private BrightnessSettings BuildBrightness(WidgetEntry? live)
    {
        var brightness = Brightness.Clone();
        if (live?.Brightness is { } liveBrightness) brightness.NightMode = liveBrightness.NightMode;
        return brightness;
    }

    public ICommand BrowseImageCommand { get; }

    public string ImagePath
    {
        get => Image.Path;
        set
        {
            value = value?.Trim().Trim('"') ?? "";
            if (value == Image.Path) return;
            Image.Path = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    private void BrowseImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Bild auswählen",
            Filter = "Bilder|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.ico;*.tif;*.tiff;*.webp|Alle Dateien|*.*",
        };
        if (Image.Path.Length > 0 && System.IO.Path.GetDirectoryName(Image.Path) is { } folder && System.IO.Directory.Exists(folder))
            dialog.InitialDirectory = folder;
        if (dialog.ShowDialog() == true) ImagePath = dialog.FileName;
    }

    /// <summary>Notiztext beim Öffnen dieses Fensters – zum Erkennen, ob er hier oder im Notiz-Popup geändert wurde.</summary>
    private readonly string _noteSnapshot;

    public string NoteText
    {
        get => Note.Text;
        set
        {
            if (value is null || value == Note.Text) return;
            Note.Text = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public TextAlignment NoteAlignment
    {
        get => Note.Alignment;
        set
        {
            if (!Enum.IsDefined(value) || value == Note.Alignment) return;
            Note.Alignment = value;
            OnPropertyChanged();
        }
    }

    /// <summary>"(Standardschrift)" = Schriftart der Fläche.</summary>
    public string NoteFontFamily
    {
        get => Note.FontFamily.Length == 0 ? SettingsViewModel.DefaultFontLabel : Note.FontFamily;
        set
        {
            if (string.IsNullOrEmpty(value)) return; // Listenaustausch schreibt kurz null
            Note.FontFamily = value == SettingsViewModel.DefaultFontLabel ? "" : value;
            OnPropertyChanged();
        }
    }

    private string _noteBackground;

    public bool NoteHasBackground
    {
        get => Note.Background is not null;
        set
        {
            if (value == NoteHasBackground) return;
            Note.Background = value ? _noteBackground : null;
            OnPropertyChanged();
        }
    }

    public string NoteBackground
    {
        get => _noteBackground;
        set
        {
            if (!SetProperty(ref _noteBackground, value ?? "")) return;
            if (NoteHasBackground && ColorUtil.IsValid(value)) Note.Background = value;
        }
    }

    /// <summary>Wurde der Text hier nicht geändert, gilt der Stand der laufenden Konfiguration (evtl. im Popup bearbeitet).</summary>
    private NoteSettings BuildNote(WidgetEntry? live)
    {
        var note = Note.Clone();
        if (Note.Text == _noteSnapshot && live?.Note is { } liveNote) note.Text = liveNote.Text;
        return note;
    }

    public string CountdownTitle
    {
        get => Countdown.Title;
        set
        {
            if (value is null || value == Countdown.Title) return;
            Countdown.Title = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public CountdownStyle CountdownStyle
    {
        get => Countdown.Style;
        set
        {
            if (!Enum.IsDefined(value) || value == Countdown.Style) return;
            Countdown.Style = value;
            OnPropertyChanged();
        }
    }

    public DateTime? CountdownDate
    {
        get => Countdown.Target.Date;
        set
        {
            if (value is not { } date) return;
            Countdown.Target = date.Date + Countdown.Target.TimeOfDay;
            OnPropertyChanged();
            OnCountdownTargetChanged();
        }
    }

    public CountdownRepeat CountdownRepeat
    {
        get => Countdown.Repeat;
        set
        {
            if (!Enum.IsDefined(value) || value == Countdown.Repeat) return;
            Countdown.Repeat = value;
            OnPropertyChanged();
            OnCountdownTargetChanged();
        }
    }

    private void OnCountdownTargetChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(CountdownRepeatHint));
        OnPropertyChanged(nameof(CountdownTargetTitle));
    }

    public string CountdownTargetTitle => Countdown.Repeat == CountdownRepeat.None ? "Ziel" : "Erster Termin";

    /// <summary>Was die Wiederholung aus Datum und Uhrzeit macht, z. B. „Jeden Mittwoch um 09:00“.</summary>
    public string CountdownRepeatHint
    {
        get
        {
            var culture = CultureInfo.CurrentCulture;
            var target = Countdown.Target;
            string time = target.ToString("HH:mm", culture);
            return Countdown.Repeat switch
            {
                CountdownRepeat.Daily => $"Jeden Tag um {time} Uhr",
                CountdownRepeat.Weekly => $"Jeden {target.ToString("dddd", culture)} um {time} Uhr",
                CountdownRepeat.Monthly => target.Day > 28
                    ? $"Jeden Monat am {target.Day}. um {time} Uhr (in kürzeren Monaten am letzten Tag)"
                    : $"Jeden Monat am {target.Day}. um {time} Uhr",
                CountdownRepeat.Yearly => $"Jedes Jahr am {target.ToString("d. MMMM", culture)} um {time} Uhr",
                _ => "Datum und Uhrzeit (z. B. 18:00).",
            };
        }
    }

    private string _countdownTime;
    private bool _countdownTimeInvalid;

    /// <summary>Uhrzeit des Ziels: "18:00", auch "18", "1830" oder "18.30".</summary>
    public string CountdownTime
    {
        get => _countdownTime;
        set
        {
            if (!SetProperty(ref _countdownTime, value ?? "")) return;
            bool valid = TryParseTime(_countdownTime, out var time);
            if (valid)
            {
                Countdown.Target = Countdown.Target.Date + time;
                OnCountdownTargetChanged();
            }
            CountdownTimeInvalid = !valid;
        }
    }

    public bool CountdownTimeInvalid
    {
        get => _countdownTimeInvalid;
        private set
        {
            if (SetProperty(ref _countdownTimeInvalid, value)) OnPropertyChanged(nameof(CountdownTimeHintVisibility));
        }
    }

    public Visibility CountdownTimeHintVisibility => CountdownTimeInvalid ? Visibility.Visible : Visibility.Collapsed;

    private static bool TryParseTime(string text, out TimeSpan time)
    {
        text = text.Trim().Replace('.', ':');
        if (text.Length is 3 or 4 && text.All(char.IsAsciiDigit)) text = text[..^2] + ":" + text[^2..];
        if (text.Length is 1 or 2 && text.All(char.IsAsciiDigit)) text += ":00";
        return TimeSpan.TryParseExact(text, [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out time) && time < TimeSpan.FromDays(1);
    }

    public SeparatorStyle SeparatorStyle
    {
        get => Separator.Style;
        set
        {
            if (!Enum.IsDefined(value) || value == Separator.Style) return;
            Separator.Style = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    // ---- Akku und Laufwerk (Mess-Widgets) --------------------------------------------------
    public Visibility DriveVisibility => Is(WidgetType.Drive) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Stammverzeichnis wie "C:\".</summary>
    public string DriveChoice
    {
        get => Metric.Drive;
        set
        {
            if (string.IsNullOrEmpty(value) || value == Metric.Drive) return; // Listenaustausch schreibt kurz null
            Metric.Drive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>RAM, Laufwerk, Akku: Menge bzw. Restlaufzeit statt Prozent.</summary>
    public Visibility AmountVisibility =>
        Is(WidgetType.Ram) || Is(WidgetType.GpuMemory) || Is(WidgetType.Drive) || Is(WidgetType.Battery) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Takt und Lüfter haben keine Warnfarben – hoher Takt bzw. laufender Lüfter ist unter Last normal.</summary>
    public Visibility WarnColorsVisibility =>
        Is(WidgetType.GpuClock) || Is(WidgetType.GpuFan) ? Visibility.Collapsed : Visibility.Visible;

    public string AmountTitle => _entry.Type switch
    {
        WidgetType.Drive => "Freien Speicher statt Belegung anzeigen",
        WidgetType.Battery => "Restlaufzeit statt Prozent anzeigen",
        _ => "Belegung in GB statt Prozent",
    };

    public string AmountHint => _entry.Type switch
    {
        WidgetType.Drive => "Z. B. „86,4 GB frei“. Balken und Verlauf zeigen weiter die Belegung.",
        WidgetType.Battery => "Nur im Akkubetrieb, sobald Windows eine Schätzung hat – sonst der Ladestand.",
        _ => "Die genauen Werte stehen immer im Tooltip.",
    };

    public string CaptionTitle => _entry.Type switch
    {
        WidgetType.GpuTemperature => "Thermometer vor dem Wert anzeigen",
        WidgetType.Battery => "Akkusymbol vor dem Wert anzeigen",
        WidgetType.Drive => "Laufwerksbuchstaben vor dem Wert anzeigen",
        WidgetType.GpuMemory => "Bezeichnung „VRAM“ vor dem Wert anzeigen",
        WidgetType.GpuClock => "Bezeichnung „Takt“ vor dem Wert anzeigen",
        WidgetType.GpuFan => "Bezeichnung „Lüfter“ vor dem Wert anzeigen",
        _ => "Bezeichnung vor dem Wert anzeigen (CPU, GPU, RAM)",
    };

    public string WarnColorsHint => _entry.Type switch
    {
        WidgetType.GpuTemperature => "Ab 80 °C orange, ab 90 °C rot.",
        WidgetType.Battery => "Im Akkubetrieb ab 20 % orange, ab 10 % rot.",
        WidgetType.Drive => "Ab 90 % Belegung orange, ab 95 % rot.",
        WidgetType.GpuMemory => "Ab 80 % Belegung orange, ab 95 % rot.",
        _ => "Ab 80 % Auslastung orange, ab 95 % rot.",
    };

    public string MetricStyleHint => _entry.Type switch
    {
        WidgetType.Battery => "Balken: aktueller Ladestand. Verlauf: Mini-Diagramm der letzten 60 Sekunden.",
        WidgetType.Drive => "Balken: Belegung des Laufwerks. Die Werte werden alle 30 Sekunden neu gelesen.",
        WidgetType.GpuMemory => "Balken: belegter Videospeicher. Verlauf: Mini-Diagramm der letzten 60 Sekunden.",
        WidgetType.GpuClock => "Balken: Takt im Verhältnis zum Höchsttakt der Karte. Verlauf: die letzten 60 Sekunden.",
        WidgetType.GpuFan => "Balken: Drehzahl im Verhältnis zur Höchstdrehzahl. Verlauf: die letzten 60 Sekunden.",
        _ => "Balken: aktuelle Auslastung. Verlauf: Mini-Diagramm der letzten 60 Sekunden.",
    };

    public AudioFlow VolumeFlow
    {
        get => Volume.Flow;
        set
        {
            if (!Enum.IsDefined(value) || value == Volume.Flow) return;
            Volume.Flow = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MicrophoneVisibility));
            OnPropertyChanged(nameof(OutputVisibility));
            OnPropertyChanged(nameof(Summary));
            // Name in der Liste folgt dem Gerät, solange er nicht nummeriert o. ä. ist.
            var info = WidgetCatalog.For(_entry.Type, Volume);
            if (Title == Info.Name) Title = info.Name;
            Info = info;
        }
    }

    /// <summary>Nur Mikrofon: rote Hervorhebung und Signalton beim Stummschalten.</summary>
    public Visibility MicrophoneVisibility => Volume.Flow == AudioFlow.Input ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Nur Wiedergabe: Lautstärke je App (beim Mikrofon wirkt sie auf das ganze Gerät, daher dort nicht angeboten).</summary>
    public Visibility OutputVisibility => Volume.Flow == AudioFlow.Output ? Visibility.Visible : Visibility.Collapsed;

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

    public Visibility GpuChoiceVisibility => WidgetEntry.IsGpuType(_entry.Type) ? Visibility.Visible : Visibility.Collapsed;

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

    /// <summary>Kurzinfo neu lesen (z. B. nach Änderung des Zeitformats dieser Uhr).</summary>
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

    public string PositionText => $"Position auf der Fläche: {_entry.Position:P0} – per Drag & Drop verschieben";

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
        // Gesperrt wird nur über das Kontextmenü der Fläche – gilt also immer der laufende Stand.
        Locked = live?.Locked ?? _entry.Locked,
        Position = live?.Position ?? _entry.Position,
        FreeX = live is null ? _entry.FreeX : live.FreeX,
        FreeLeft = live is null ? _entry.FreeLeft : live.FreeLeft,
        FreeY = live is null ? _entry.FreeY : live.FreeY,
        FreeOffsetLeft = live is null ? _entry.FreeOffsetLeft : live.FreeOffsetLeft,
        FreeOffsetTop = live is null ? _entry.FreeOffsetTop : live.FreeOffsetTop,
        Devices = Is(WidgetType.DeviceBattery)
            ? AllBatteryDevices ? [] : BatteryDevices.Where(d => d.Selected).Select(d => d.Id).ToList()
            : AllDevices ? [] : Devices.Where(d => d.Selected).Select(d => d.Id).ToList(),
        // Ohne eigene Farbe (null) folgt das Widget der Schriftfarbe der Leiste.
        IconColor = HasIconColor && _iconColorCustom ? IconColor : null,
        LabelColor = HasLabelColor && _labelColorCustom ? LabelColor : null,
        IndicatorColor = HasIndicatorColor && _indicatorColorCustom ? IndicatorColor : null,
        Metric = IsMetric ? Metric.Clone() : null,
        Network = Is(WidgetType.Network) ? Network.Clone() : null,
        Volume = Is(WidgetType.Volume) ? Volume.Clone() : null,
        Timer = Is(WidgetType.Timer) ? BuildTimer(live) : null,
        Stopwatch = Is(WidgetType.Stopwatch) ? Stopwatch.Clone() : null,
        Traffic = Is(WidgetType.NetworkTraffic) ? BuildTraffic() : null,
        Image = Is(WidgetType.Image) ? Image.Clone() : null,
        Note = Is(WidgetType.Note) ? BuildNote(live) : null,
        Media = Is(WidgetType.Media) ? Media.Clone() : null,
        Countdown = Is(WidgetType.Countdown) ? Countdown.Clone() : null,
        Separator = Is(WidgetType.Separator) ? Separator.Clone() : null,
        Draw = Is(WidgetType.Draw) ? Draw.Clone() : null,
        Clipboard = Is(WidgetType.Clipboard) ? BuildClipboard(live) : null,
        WorldClock = Is(WidgetType.WorldClock) ? BuildWorldClock() : null,
        Launcher = Is(WidgetType.Launcher) ? BuildLauncher(live) : null,
        Weather = Is(WidgetType.Weather) ? Weather.Clone() : null,
        DeviceBattery = Is(WidgetType.DeviceBattery) ? DeviceBattery.Clone() : null,
        PublicIp = Is(WidgetType.PublicIp) ? PublicIp.Clone() : null,
        Brightness = Is(WidgetType.Brightness) ? BuildBrightness(live) : null,
        Fps = Is(WidgetType.Fps) ? Fps.Clone() : null,
        Hotkey =IsAudio ? Hotkey : null,
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

/// <summary>Eine Zeitzone in der Liste einer Weltuhr.</summary>
public sealed class WorldClockZoneRow : ObservableObject
{
    private readonly string _id;
    private string _timeZoneId;
    private string _label;

    public WorldClockZoneRow(WorldClockZone zone, Action<WorldClockZoneRow> remove)
    {
        _id = zone.Id;
        _timeZoneId = zone.TimeZoneId;
        _label = zone.Label;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public ICommand RemoveCommand { get; }

    public string TimeZoneId
    {
        get => _timeZoneId;
        set
        {
            if (string.IsNullOrEmpty(value) || !SetProperty(ref _timeZoneId, value)) return; // Listenaustausch schreibt kurz null
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(LabelPlaceholder));
        }
    }

    public string Label
    {
        get => _label;
        set
        {
            if (SetProperty(ref _label, value ?? "")) OnPropertyChanged(nameof(DisplayLabel));
        }
    }

    /// <summary>Vorschlag aus der Zeitzone, solange kein eigener Name eingetragen ist.</summary>
    public string LabelPlaceholder => WorldClockSettings.FindZone(TimeZoneId) is { } zone ? WorldClockSettings.DefaultLabel(zone) : "";

    public string DisplayLabel => Label.Trim().Length > 0 ? Label.Trim() : LabelPlaceholder;

    public WorldClockZone ToZone() => new() { Id = _id, TimeZoneId = TimeZoneId, Label = Label.Trim() };
}

/// <summary>Ein Eintrag in der Liste eines Schnellstarts.</summary>
public sealed class LauncherItemRow : ObservableObject
{
    private readonly string _id;
    private string _path;
    private string _arguments;
    private string _name;

    public LauncherItemRow(LauncherItem item, Action<LauncherItemRow> remove)
    {
        _id = item.Id;
        _path = item.Path;
        _arguments = item.Arguments;
        _name = item.Name;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public ICommand RemoveCommand { get; }

    public string Path
    {
        get => _path;
        set
        {
            if (!SetProperty(ref _path, value?.Trim().Trim('"') ?? "")) return;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(NamePlaceholder));
        }
    }

    public string Arguments { get => _arguments; set => SetProperty(ref _arguments, value ?? ""); }
    public string Name { get => _name; set => SetProperty(ref _name, value ?? ""); }

    public System.Windows.Media.ImageSource? Icon => Widgets.Launcher.LauncherIcons.Get(Path, 16);
    public string NamePlaceholder => Widgets.Launcher.LauncherIcons.DefaultName(Path);

    public LauncherItem ToItem() => new() { Id = _id, Path = Path, Arguments = Arguments.Trim(), Name = Name.Trim() };
}
