using System.Globalization;
using System.Text.Json.Serialization;
using Veyra.Core;

namespace Veyra.Configuration;

public enum BarMode
{
    /// <summary>Fixiert: volle Bildschirmbreite, oben angedockt.</summary>
    FullWidth,

    /// <summary>Dynamisch: frei bewegliches Fenster mit eigener Größe, Transparenz und Ecken.</summary>
    Floating,
}

/// <summary>Fensterebene im dynamischen Modus.</summary>
public enum WindowLayer
{
    /// <summary>Immer über allen Fenstern.</summary>
    Topmost,

    /// <summary>Wie ein normales Fenster – andere Fenster dürfen es verdecken.</summary>
    Normal,

    /// <summary>Immer hinter allen Fenstern, wie ein Desktop-Widget.</summary>
    Desktop,
}

/// <summary>Wurzelobjekt von %AppData%\Veyra\settings.json.</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 4;

    /// <summary>Alle Leisten des aktiven Profils. Es gibt immer mindestens eine.</summary>
    public List<BarSettings> Bars { get; set; } = [];

    /// <summary>
    /// Layout-Profile (eigene Ansichten). Das aktive Profil speichert keine eigenen Leisten – seine Leisten sind
    /// <see cref="Bars"/>. Beim Wechsel werden die Leisten getauscht (<see cref="ProfileOperations"/>).
    /// </summary>
    public List<LayoutProfile> Profiles { get; set; } = [];

    public string ActiveProfileId { get; set; } = "";

    /// <summary>Globales Tastenkürzel für die Profilauswahl (Overlay); null/leer = keins.</summary>
    public string? ProfileHotkey { get; set; } = DefaultProfileHotkey;

    public const string DefaultProfileHotkey = "Ctrl+Alt+P";

    [JsonIgnore]
    public LayoutProfile ActiveProfile => Profiles.First(p => p.Id == ActiveProfileId);

    public BehaviorSettings Behavior { get; set; } = new();
    public SystemSettings System { get; set; } = new();

    public AudioSettings Audio { get; set; } = new();

    public UpdateSettings Updates { get; set; } = new();

    /// <summary>Nur zum Einlesen älterer Dateien mit genau einer Leiste: wird in <see cref="Bars"/> übernommen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AppearanceSettings? Appearance { get; set; }

    /// <inheritdoc cref="Appearance"/>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<WidgetEntry>? Widgets { get; set; }

    /// <summary>Nur zum Einlesen älterer Dateien mit einer gemeinsamen Uhr-Einstellung: jede Uhr erhält davon eine eigene Kopie.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClockSettings? Clock { get; set; }

    public AppSettings Clone() => SettingsService.Deserialize(SettingsService.Serialize(this)) ?? new AppSettings();

    public BarSettings? FindBar(string? id) => Bars.FirstOrDefault(b => b.Id == id);

    /// <summary>Ersetzt fehlende/ungültige Werte durch Standardwerte und begrenzt Zahlen auf sinnvolle Bereiche.</summary>
    public void Normalize()
    {
        Behavior ??= new BehaviorSettings();
        System ??= new SystemSettings();
        Audio ??= new AudioSettings();
        Updates ??= new UpdateSettings();

        Bars = Bars?.Where(b => b is not null).ToList() ?? [];
        if (Bars.Count == 0)
        {
            // Ältere Datei (eine Leiste) oder neue Installation → erste Leiste aus den bisherigen Werten.
            Bars.Add(new BarSettings
            {
                Appearance = Appearance ?? new AppearanceSettings(),
                Widgets = Widgets ?? WidgetEntry.Defaults(),
                MonitorSelection = System.MonitorSelection ?? MonitorDefault,
            });
        }
        Appearance = null;
        Widgets = null;
        System.MonitorSelection = null;
        Version = 4;

        // Ältere Dateien: Vordergrund und Reservierung galten für alle Leisten – jede Leiste übernimmt den Wert.
        foreach (var bar in Bars.Concat(Profiles?.Where(p => p?.Bars is not null).SelectMany(p => p.Bars) ?? []).Where(b => b?.Appearance is not null))
        {
            bar.Appearance.AlwaysOnTop ??= Behavior.AlwaysOnTop ?? true;
            bar.Appearance.ReserveScreenSpace ??= Behavior.ReserveScreenSpace ?? true;
        }
        Behavior.AlwaysOnTop = null;
        Behavior.ReserveScreenSpace = null;

        var legacyClock = Clock;
        Clock = null;

        for (int i = 0; i < Bars.Count; i++) Bars[i].Normalize(i, legacyClock);
        // Doppelte IDs (z. B. kopierte Einträge) würden Leisten bzw. Drag & Drop verwechseln – über alle Leisten hinweg.
        foreach (var duplicate in Bars.GroupBy(b => b.Id).SelectMany(g => g.Skip(1)))
            duplicate.Id = WidgetEntry.NewId();
        foreach (var duplicate in Bars.SelectMany(b => b.Widgets).GroupBy(w => w.Id).SelectMany(g => g.Skip(1)))
            duplicate.Id = WidgetEntry.NewId();

        NormalizeProfiles();

        Behavior.Normalize();
        System.Normalize();
        Audio.Normalize();
        Updates.SkippedVersion = Updates.SkippedVersion?.Trim() ?? "";
    }

    private void NormalizeProfiles()
    {
        Profiles = Profiles?.Where(p => p is not null).ToList() ?? [];
        // Ältere Datei ohne Profile → die bisherigen Leisten werden zum Profil "Standard".
        if (Profiles.Count == 0) Profiles.Add(new LayoutProfile { Id = ActiveProfileId, Name = "Standard" });

        foreach (var duplicate in Profiles.GroupBy(p => p.Id).SelectMany(g => g.Skip(1)))
            duplicate.Id = WidgetEntry.NewId();
        for (int i = 0; i < Profiles.Count; i++)
        {
            var profile = Profiles[i];
            if (string.IsNullOrWhiteSpace(profile.Id)) profile.Id = WidgetEntry.NewId();
            profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? $"Profil {i + 1}" : profile.Name.Trim();
            profile.Bars = profile.Bars?.Where(b => b is not null).ToList() ?? [];
        }

        if (Profiles.All(p => p.Id != ActiveProfileId)) ActiveProfileId = Profiles[0].Id;

        foreach (var profile in Profiles)
        {
            if (profile.Id == ActiveProfileId)
            {
                profile.Bars = []; // die Flächen des aktiven Profils stehen in Bars
                continue;
            }
            if (profile.Bars.Count == 0) profile.Bars.Add(new BarSettings());
            for (int i = 0; i < profile.Bars.Count; i++) profile.Bars[i].Normalize(i);
        }

        ProfileHotkey = Hotkey.TryParse(ProfileHotkey, out var hotkey) ? hotkey.ToString() : null;
    }

    public const string MonitorDefault = "Primary";
}

/// <summary>Ein Layout-Profil: eigene Ansicht aus Leisten samt Widgets, umschaltbar per Overlay oder Einstellungen.</summary>
public sealed class LayoutProfile
{
    public string Id { get; set; } = WidgetEntry.NewId();

    public string Name { get; set; } = "";

    /// <summary>Leisten dieses Profils; beim aktiven Profil leer (dann gilt <see cref="AppSettings.Bars"/>).</summary>
    public List<BarSettings> Bars { get; set; } = [];
}

/// <summary>Eine Fläche (im Code „Bar“): eigenes Fenster mit Modus, Größe, Aussehen, Ebene, Monitor und eigenen Widgets – fixiert als Leiste oder frei.</summary>
public sealed class BarSettings
{
    public string Id { get; set; } = WidgetEntry.NewId();

    /// <summary>Anzeigename in den Einstellungen; leer = "Fläche N".</summary>
    public string Name { get; set; } = "";

    /// <summary>"Primary" oder ein Gerätename wie "\\.\DISPLAY2".</summary>
    public string MonitorSelection { get; set; } = AppSettings.MonitorDefault;

    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>Widgets auf dieser Leiste. Reihenfolge in der Liste = Reihenfolge innerhalb eines Bereichs.</summary>
    public List<WidgetEntry> Widgets { get; set; } = WidgetEntry.Defaults();

    /// <param name="clockTemplate">Ausgangswerte für Uhren ohne eigene Einstellungen (ältere Dateien); null = Standardwerte.</param>
    internal void Normalize(int index, ClockSettings? clockTemplate = null)
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = WidgetEntry.NewId();
        Name = string.IsNullOrWhiteSpace(Name) ? $"Fläche {index + 1}" : Name.Trim();
        // Bis 1.3.3 hießen Flächen „Leisten“ – automatisch vergebene Namen mit umbenennen, eigene nicht.
        if (Name.StartsWith("Leiste ", StringComparison.Ordinal) && int.TryParse(Name[7..], out int number))
            Name = $"Fläche {number}";
        MonitorSelection = string.IsNullOrWhiteSpace(MonitorSelection) ? AppSettings.MonitorDefault : MonitorSelection.Trim();
        Appearance ??= new AppearanceSettings();
        Appearance.Normalize();
        Widgets = Widgets?.Where(w => w is not null && Enum.IsDefined(w.Type)).ToList() ?? WidgetEntry.Defaults();
        foreach (var widget in Widgets) widget.Normalize(clockTemplate);
    }
}

/// <summary>Update-Prüfung über GitHub Releases.</summary>
public sealed class UpdateSettings
{
    /// <summary>Beim Start (verzögert) und danach regelmäßig nach Updates suchen.</summary>
    public bool AutoCheck { get; set; } = true;

    /// <summary>Mit "Nein" übersprungene Version – erst eine neuere Version wird wieder angeboten.</summary>
    public string SkippedVersion { get; set; } = "";

    /// <summary>Mit "Später" verschoben: vor diesem Zeitpunkt (UTC) wird nicht erneut gefragt.</summary>
    public DateTime? RemindAfterUtc { get; set; }

    public DateTime? LastCheckUtc { get; set; }
}

public enum WidgetType
{
    Clock,

    /// <summary>Ein Button je aktivem Wiedergabegerät – Klick setzt es als Standard.</summary>
    AudioOutputs,

    /// <summary>Ein Button je aktivem Mikrofon – Klick setzt es als Standard.</summary>
    AudioInputs,

    /// <summary>Prozessor-Auslastung in Prozent.</summary>
    Cpu,

    /// <summary>Grafikkarten-Auslastung in Prozent (wie im Task-Manager).</summary>
    Gpu,

    /// <summary>Belegter Arbeitsspeicher.</summary>
    Ram,

    /// <summary>Temperatur der Grafikkarte (wie im Task-Manager).</summary>
    GpuTemperature,

    /// <summary>Aktuelle Netzwerkverbindung (LAN/WLAN) mit Status, Netzwerkname und Signalstärke.</summary>
    Network,

    /// <summary>Systemlautstärke: Mausrad, Regler, Stummschalten.</summary>
    Volume,

    /// <summary>Beliebig viele Countdown-Timer mit Benachrichtigung.</summary>
    Timer,

    /// <summary>Stoppuhr mit Rundenzeiten.</summary>
    Stopwatch,

    /// <summary>Upload- und Downloadgeschwindigkeit.</summary>
    NetworkTraffic,

    /// <summary>Ein Bild aus einer Datei (PNG, JPG, GIF, BMP, ICO).</summary>
    Image,

    /// <summary>Freier Text als Notiz, direkt auf der Fläche bearbeitbar.</summary>
    Note,

    /// <summary>Gerade gespielte Medien (Titel, Interpret, Cover) mit Wiedergabesteuerung.</summary>
    Media,

    /// <summary>Akku-Ladestand.</summary>
    Battery,

    /// <summary>Belegter Speicher eines Laufwerks.</summary>
    Drive,

    /// <summary>Verbleibende Zeit bis zu einem Datum.</summary>
    Countdown,

    /// <summary>Trennlinie oder leerer Abstand – reine Gestaltung.</summary>
    Separator,

    /// <summary>Belegter Videospeicher der Grafikkarte.</summary>
    GpuMemory,

    /// <summary>Aktueller Takt der Grafikkarte in MHz.</summary>
    GpuClock,

    /// <summary>Lüfterdrehzahl der Grafikkarte.</summary>
    GpuFan,

    /// <summary>Freihand auf den Bildschirm zeichnen – das Gezeichnete liegt immer über allem.</summary>
    Draw,

    /// <summary>Verlauf der Zwischenablage: zuletzt kopierte Texte, Bilder und Dateien, Texte auch angeheftet.</summary>
    Clipboard,

    /// <summary>Uhrzeit in anderen Zeitzonen.</summary>
    WorldClock,

    /// <summary>Schnellstart: Programme, Ordner, Dateien und Webadressen als Symbole.</summary>
    Launcher,

    /// <summary>Aktuelles Wetter und Vorhersage (Open-Meteo, ohne Konto).</summary>
    Weather,

    /// <summary>Akkustand von Bluetooth-Geräten und kabellosen Razer-Geräten.</summary>
    DeviceBattery,

    /// <summary>Öffentliche IPv4- und IPv6-Adresse.</summary>
    PublicIp,

    /// <summary>Bildschirmhelligkeit (DDC/CI) und Nachtmodus (warmes Licht).</summary>
    Brightness,

    /// <summary>Bilder pro Sekunde des Spiels im Vordergrund (ETW, Gruppe „Leistungsprotokollbenutzer“).</summary>
    Fps,
}

/// <summary>Eine per Tastenkürzel auslösbare Aktion eines Widgets.</summary>
/// <param name="Repeat">Gedrückt halten wiederholt die Aktion (z. B. Lauter/Leiser).</param>
public sealed record HotkeyAction(string Id, string Name, string Description, bool Repeat = false);

/// <summary>Tastenkürzel-Aktionen je Widget-Typ (gespeichert in <see cref="WidgetEntry.Hotkeys"/>).</summary>
public static class WidgetHotkeys
{
    public const string VolumeUp = "VolumeUp";
    public const string VolumeDown = "VolumeDown";
    public const string Mute = "Mute";
    public const string StartPause = "StartPause";
    public const string Reset = "Reset";
    public const string Lap = "Lap";
    public const string PlayPause = "PlayPause";
    public const string NextTrack = "NextTrack";
    public const string PreviousTrack = "PreviousTrack";
    public const string ToggleDraw = "ToggleDraw";
    public const string ClearDrawing = "ClearDrawing";
    public const string ShowClipboard = "ShowClipboard";
    public const string BrightnessUp = "BrightnessUp";
    public const string BrightnessDown = "BrightnessDown";
    public const string ToggleNightMode = "ToggleNightMode";

    private static readonly IReadOnlyList<HotkeyAction> Volume =
    [
        new(VolumeUp, "Lauter", "Um die Schrittweite des Mausrads; gedrückt halten wiederholt.", Repeat: true),
        new(VolumeDown, "Leiser", "Um die Schrittweite des Mausrads; gedrückt halten wiederholt.", Repeat: true),
        new(Mute, "Stumm ein/aus", "Schaltet das Standardgerät stumm bzw. wieder laut."),
    ];

    private static readonly IReadOnlyList<HotkeyAction> Timer =
    [
        new(StartPause, "Starten / Pausieren", "Abgelaufene Timer: Alarm beenden. Sonst den nächsten aktiven Timer pausieren bzw. fortsetzen – oder den ersten starten."),
        new(Reset, "Zurücksetzen", "Setzt alle laufenden, pausierten und abgelaufenen Timer dieses Widgets zurück."),
    ];

    private static readonly IReadOnlyList<HotkeyAction> Stopwatch =
    [
        new(StartPause, "Starten / Pausieren", ""),
        new(Lap, "Runde", "Nur während die Stoppuhr läuft und Rundenzeiten eingeschaltet sind."),
        new(Reset, "Zurücksetzen", ""),
    ];

    private static readonly IReadOnlyList<HotkeyAction> Media =
    [
        new(PlayPause, "Wiedergabe / Pause", "Für die Wiedergabe, die das Widget gerade zeigt."),
        new(NextTrack, "Nächster Titel", ""),
        new(PreviousTrack, "Vorheriger Titel", ""),
    ];

    private static readonly IReadOnlyList<HotkeyAction> Draw =
    [
        new(ToggleDraw, "Zeichnen ein/aus", "Startet das Zeichnen auf dem Monitor unter der Maus bzw. beendet es. Das Gezeichnete bleibt stehen."),
        new(ClearDrawing, "Alles löschen", "Entfernt alles Gezeichnete auf allen Monitoren und beendet das Zeichnen."),
    ];

    private static readonly IReadOnlyList<HotkeyAction> Clipboard =
    [
        new(ShowClipboard, "Verlauf öffnen", "Öffnet den Verlauf am Widget – Pfeiltasten und Enter wählen, Esc schließt. Nur bei sichtbarer Fläche."),
    ];

    private static readonly IReadOnlyList<HotkeyAction> Brightness =
    [
        new(BrightnessUp, "Heller", "Alle Monitore um die Schrittweite des Mausrads; gedrückt halten wiederholt.", Repeat: true),
        new(BrightnessDown, "Dunkler", "Alle Monitore um die Schrittweite des Mausrads; gedrückt halten wiederholt.", Repeat: true),
        new(ToggleNightMode, "Nachtmodus ein/aus", "Warmes Licht auf allen Monitoren."),
    ];

    public static IReadOnlyList<HotkeyAction> For(WidgetType type) => type switch
    {
        WidgetType.Volume => Volume,
        WidgetType.Timer => Timer,
        WidgetType.Stopwatch => Stopwatch,
        WidgetType.Media => Media,
        WidgetType.Draw => Draw,
        WidgetType.Clipboard => Clipboard,
        WidgetType.Brightness => Brightness,
        _ => [],
    };

    public static HotkeyAction? Find(WidgetType type, string action) => For(type).FirstOrDefault(a => a.Id == action);
}

/// <summary>Einstellungen des Netzwerk-Widgets. Farben stehen wie bei den anderen Widgets im <see cref="WidgetEntry"/>.</summary>
public sealed class NetworkSettings
{
    /// <summary>Netzwerkname (SSID bzw. Name des LAN-Netzwerks) neben dem Symbol.</summary>
    public bool ShowName { get; set; } = true;

    /// <summary>Nur WLAN: Signalstärke in Prozent neben dem Symbol.</summary>
    public bool ShowSignal { get; set; }

    /// <summary>Im Popup eine Schaltfläche zu den Windows-Netzwerkeinstellungen.</summary>
    public bool ShowSettingsButton { get; set; } = true;

    public NetworkSettings Clone() => (NetworkSettings)MemberwiseClone();
}

/// <summary>Einstellungen des Lautstärke-Widgets.</summary>
public sealed class VolumeSettings
{
    /// <summary>Wiedergabe- oder Aufnahme-Standardgerät.</summary>
    public Veyra.Audio.AudioFlow Flow { get; set; } = Veyra.Audio.AudioFlow.Output;

    public bool ShowPercent { get; set; } = true;

    /// <summary>Kleiner Füllbalken neben dem Symbol.</summary>
    public bool ShowBar { get; set; }

    /// <summary>Lautstärkeänderung je Mausrad-Raste in Prozentpunkten.</summary>
    public int WheelStep { get; set; } = 2;

    /// <summary>Rechtsklick schaltet stumm (sonst öffnet er das Kontextmenü der Leiste).</summary>
    public bool RightClickMutes { get; set; } = true;

    /// <summary>Im Regler-Popup die Lautstärke je App (wie im Windows-Lautstärkemixer).</summary>
    public bool ShowApps { get; set; } = true;

    /// <summary>Nur Mikrofon: stummgeschaltet deutlich rot hervorheben.</summary>
    public bool HighlightMuted { get; set; } = true;

    /// <summary>Beim Tastenkürzel kurz eine Anzeige oben auf dem Bildschirm einblenden – auch über Spielen.</summary>
    public bool ShowOverlay { get; set; } = true;

    /// <summary>Nur Mikrofon: kurzer Signalton beim Stummschalten (absteigend) und Einschalten (aufsteigend).</summary>
    public bool MuteSound { get; set; } = true;

    public VolumeSettings Clone() => (VolumeSettings)MemberwiseClone();

    internal void Normalize()
    {
        if (!Enum.IsDefined(Flow)) Flow = Veyra.Audio.AudioFlow.Output;
        WheelStep = Math.Clamp(WheelStep, 1, 25);
    }
}

/// <summary>Ein Timer: Name und Dauer. Der Ablauf (läuft, pausiert) ist Laufzeitzustand und wird nicht gespeichert.</summary>
public sealed class TimerPreset
{
    public string Id { get; set; } = WidgetEntry.NewId();
    public string Name { get; set; } = "";

    /// <summary>Dauer in Sekunden (1 s bis 99 h).</summary>
    public int DurationSeconds { get; set; } = 300;

    public const int MaxDurationSeconds = 99 * 3600 + 59 * 60 + 59;

    public TimerPreset Clone() => (TimerPreset)MemberwiseClone();
}

/// <summary>Signalton nach Ablauf eines Timers (Windows-Systemklänge, sie folgen dem Soundschema).</summary>
public enum TimerSound
{
    None,
    Alarm,
    Notification,
    Reminder,
}

/// <summary>Einstellungen des Timer-Widgets.</summary>
public sealed class TimerSettings
{
    public List<TimerPreset> Timers { get; set; } = [new() { Name = "Timer 1", DurationSeconds = 300 }];

    /// <summary>Alle laufenden bzw. pausierten Timer auf der Leiste anzeigen; aus = nur den, der als nächster abläuft.</summary>
    public bool ShowAllActive { get; set; }

    /// <summary>Namen der Timer neben der Restzeit.</summary>
    public bool ShowName { get; set; }

    /// <summary>Dünner Fortschrittsbalken unter der Restzeit.</summary>
    public bool ShowProgress { get; set; } = true;

    /// <summary>Windows-Benachrichtigung nach Ablauf.</summary>
    public bool Notify { get; set; } = true;

    public TimerSound Sound { get; set; } = TimerSound.Alarm;

    public TimerSettings Clone()
    {
        var copy = (TimerSettings)MemberwiseClone();
        copy.Timers = Timers.Select(t => t.Clone()).ToList();
        return copy;
    }

    internal void Normalize()
    {
        Timers = (Timers ?? []).Where(t => t is not null).ToList();
        for (int i = 0; i < Timers.Count; i++)
        {
            var timer = Timers[i];
            if (string.IsNullOrWhiteSpace(timer.Id)) timer.Id = WidgetEntry.NewId();
            timer.Name = string.IsNullOrWhiteSpace(timer.Name) ? $"Timer {i + 1}" : timer.Name.Trim();
            timer.DurationSeconds = Math.Clamp(timer.DurationSeconds, 1, TimerPreset.MaxDurationSeconds);
        }
        foreach (var duplicate in Timers.GroupBy(t => t.Id).SelectMany(g => g.Skip(1)))
            duplicate.Id = WidgetEntry.NewId();
        if (!Enum.IsDefined(Sound)) Sound = TimerSound.Alarm;
    }
}

/// <summary>Einstellungen der Stoppuhr.</summary>
public sealed class StopwatchSettings
{
    /// <summary>Millisekunden anzeigen (dann wird die Anzeige während des Laufs etwa 20-mal je Sekunde aktualisiert).</summary>
    public bool ShowMilliseconds { get; set; }

    /// <summary>Stunden auch unter einer Stunde anzeigen ("0:05:12" statt "05:12").</summary>
    public bool AlwaysShowHours { get; set; }

    /// <summary>Rundenzeiten im Popup erfassen.</summary>
    public bool EnableLaps { get; set; } = true;

    public StopwatchSettings Clone() => (StopwatchSettings)MemberwiseClone();
}

/// <summary>Welche Richtungen der Upload-/Download-Monitor zeigt.</summary>
public enum TrafficDirection
{
    Both,
    Download,
    Upload,
}

/// <summary>Einstellungen des Upload-/Download-Monitors.</summary>
public sealed class TrafficSettings
{
    public TrafficDirection Direction { get; set; } = TrafficDirection.Both;

    /// <summary>Download und Upload untereinander statt nebeneinander.</summary>
    public bool Stacked { get; set; }

    /// <summary>Verlaufsgrafik der letzten Messungen.</summary>
    public bool ShowGraph { get; set; }

    /// <summary>Bit/s (Mbit/s …) statt Byte/s (MB/s …).</summary>
    public bool UseBits { get; set; }

    /// <summary>Adapter-GUID wie in <see cref="System.Net.NetworkInformation.NetworkInterface.Id"/>; leer = alle aktiven Adapter.</summary>
    public string Adapter { get; set; } = "";

    /// <summary>Nur zur Lesbarkeit der JSON-Datei und für die Anzeige, falls der Adapter fehlt.</summary>
    public string AdapterName { get; set; } = "";

    /// <summary>Aktualisierungsintervall in Millisekunden.</summary>
    public int IntervalMs { get; set; } = 1000;

    public TrafficSettings Clone() => (TrafficSettings)MemberwiseClone();

    internal void Normalize()
    {
        if (!Enum.IsDefined(Direction)) Direction = TrafficDirection.Both;
        Adapter = Adapter?.Trim() ?? "";
        AdapterName = Adapter.Length == 0 ? "" : AdapterName?.Trim() ?? "";
        IntervalMs = Math.Clamp(IntervalMs, 250, 10000);
    }
}

/// <summary>Einstellungen des Bild-Widgets.</summary>
public sealed class ImageSettings
{
    /// <summary>Vollständiger Pfad der Bilddatei; leer = noch kein Bild gewählt.</summary>
    public string Path { get; set; } = "";

    /// <summary>Höhe in px; die Breite folgt dem Seitenverhältnis, solange <see cref="Width"/> 0 ist.</summary>
    public double Height { get; set; } = 32;

    /// <summary>Breite in px; 0 = automatisch aus dem Seitenverhältnis.</summary>
    public double Width { get; set; }

    /// <summary>Deckkraft in Prozent.</summary>
    public double Opacity { get; set; } = 100;

    /// <summary>Abgerundete Ecken in px (0 = eckig).</summary>
    public double CornerRadius { get; set; }

    /// <summary>Optional: Datei, Ordner, Programm oder Webadresse, die ein Klick auf das Bild öffnet; leer = nichts.</summary>
    public string ClickTarget { get; set; } = "";

    public ImageSettings Clone() => (ImageSettings)MemberwiseClone();

    internal void Normalize()
    {
        Path = Path?.Trim().Trim('"') ?? "";
        ClickTarget = ClickTarget?.Trim() ?? "";
        Height = double.IsFinite(Height) ? Math.Clamp(Math.Round(Height), 8, 1000) : 32;
        Width = double.IsFinite(Width) ? Math.Clamp(Math.Round(Width), 0, 2000) : 0;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Math.Round(Opacity), 5, 100) : 100;
        CornerRadius = double.IsFinite(CornerRadius) ? Math.Clamp(Math.Round(CornerRadius), 0, 500) : 0;
    }
}

/// <summary>Einstellungen des Notiz-Widgets. Die Schriftfarbe steht wie bei den anderen Widgets in <see cref="WidgetEntry.LabelColor"/>.</summary>
public sealed class NoteSettings
{
    public string Text { get; set; } = "";

    public double FontSize { get; set; } = 13;

    /// <summary>Leer = Schriftart der Fläche.</summary>
    public string FontFamily { get; set; } = "";

    public bool Bold { get; set; }

    /// <summary>Breite, ab der umgebrochen wird (px); 0 = kein Umbruch, nur Zeilenumbrüche im Text.</summary>
    public double MaxWidth { get; set; } = 260;

    public System.Windows.TextAlignment Alignment { get; set; } = System.Windows.TextAlignment.Left;

    /// <summary>Hintergrund wie ein Notizzettel; null = durchsichtig.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Background { get; set; }

    /// <summary>Klick auf die Notiz öffnet ein Popup zum Bearbeiten.</summary>
    public bool EditOnClick { get; set; } = true;

    public const string DefaultBackground = "#33FFFFFF";

    public NoteSettings Clone() => (NoteSettings)MemberwiseClone();

    internal void Normalize()
    {
        Text = (Text ?? "").Replace("\r\n", "\n");
        if (Text.Length > 4000) Text = Text[..4000];
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(Math.Round(FontSize, 1), 6, 200) : 13;
        FontFamily = FontFamily?.Trim() ?? "";
        MaxWidth = double.IsFinite(MaxWidth) ? Math.Clamp(Math.Round(MaxWidth), 0, 3000) : 260;
        if (!Enum.IsDefined(Alignment) || Alignment == System.Windows.TextAlignment.Justify) Alignment = System.Windows.TextAlignment.Left;
        Background = ColorUtil.IsValid(Background) ? Background!.Trim() : null;
    }
}

/// <summary>Einstellungen des Medien-Widgets.</summary>
public sealed class MediaSettings
{
    /// <summary>Albumcover bzw. Vorschaubild links neben dem Titel.</summary>
    public bool ShowCover { get; set; } = true;

    /// <summary>Interpret unter dem Titel.</summary>
    public bool ShowArtist { get; set; } = true;

    /// <summary>Schaltflächen Zurück, Wiedergabe/Pause, Weiter.</summary>
    public bool ShowControls { get; set; } = true;

    /// <summary>Breite des Textes in px (bei <see cref="FixedTextWidth"/> genau, sonst höchstens); längere Titel werden mit … gekürzt.</summary>
    public double MaxTextWidth { get; set; } = 200;

    /// <summary>
    /// Text immer genau <see cref="MaxTextWidth"/> breit, Cover-Platz auch ohne Cover belegt: Die Schaltflächen stehen
    /// dann bei jedem Titel an derselben Stelle (mehrere Titel überspringen, ohne der Maus hinterherzufahren).
    /// </summary>
    public bool FixedTextWidth { get; set; } = true;

    /// <summary>Zu lange Titel und Interpreten laufen durch (wandernder Text) statt mit … zu enden.</summary>
    public bool ScrollLongText { get; set; } = true;

    /// <summary>Ohne laufende Wiedergabe ganz ausblenden statt „Keine Wiedergabe“ zu zeigen.</summary>
    public bool HideWhenIdle { get; set; }

    public MediaSettings Clone() => (MediaSettings)MemberwiseClone();

    internal void Normalize() =>
        MaxTextWidth = double.IsFinite(MaxTextWidth) ? Math.Clamp(Math.Round(MaxTextWidth), 60, 1000) : 200;
}

/// <summary>Anzeigeform eines Countdowns.</summary>
public enum CountdownStyle
{
    /// <summary>"12 Tage" – unter einem Tag "5 Std." bzw. "40 Min.".</summary>
    Days,

    /// <summary>"12 T 4 Std."</summary>
    DaysHours,

    /// <summary>"12 T 04:31:10" mit Sekunden.</summary>
    Clock,
}

/// <summary>Wiederholung eines Countdowns, z. B. für den täglichen oder wöchentlichen Reset in Spielen.</summary>
public enum CountdownRepeat
{
    None,
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>Einstellungen des Countdowns.</summary>
public sealed class CountdownSettings
{
    public string Title { get; set; } = "Countdown";

    /// <summary>Zieldatum und -uhrzeit (Ortszeit); bei Wiederholung der erste Termin.</summary>
    public DateTime Target { get; set; } = DateTime.Today.AddDays(7);

    /// <summary>
    /// Wiederholung ab <see cref="Target"/>: täglich zur selben Uhrzeit, wöchentlich am selben Wochentag, monatlich am
    /// selben Tag (in kürzeren Monaten am letzten), jährlich am selben Datum.
    /// </summary>
    public CountdownRepeat Repeat { get; set; } = CountdownRepeat.None;

    /// <summary>
    /// Nächster Termin nach <paramref name="after"/>; ohne Wiederholung immer <see cref="Target"/> (auch wenn vorbei).
    /// Vor dem ersten Termin gilt dieser.
    /// </summary>
    public DateTime NextOccurrence(DateTime after)
    {
        if (Repeat == CountdownRepeat.None || after < Target) return Target;
        var time = Target.TimeOfDay;
        DateTime next;
        switch (Repeat)
        {
            case CountdownRepeat.Daily:
                next = after.Date + time;
                if (next <= after) next = next.AddDays(1);
                break;
            case CountdownRepeat.Weekly:
                next = after.Date.AddDays(((int)Target.DayOfWeek - (int)after.DayOfWeek + 7) % 7) + time;
                if (next <= after) next = next.AddDays(7);
                break;
            case CountdownRepeat.Monthly:
                next = DayInMonth(after.Year, after.Month, Target.Day) + time;
                if (next <= after)
                {
                    var month = new DateTime(after.Year, after.Month, 1).AddMonths(1);
                    next = DayInMonth(month.Year, month.Month, Target.Day) + time;
                }
                break;
            default:
                next = DayInMonth(after.Year, Target.Month, Target.Day) + time;
                if (next <= after) next = DayInMonth(after.Year + 1, Target.Month, Target.Day) + time;
                break;
        }
        return next;
    }

    /// <summary>Tag im Monat, in kürzeren Monaten der letzte (31. → 30. bzw. 28./29. Februar).</summary>
    private static DateTime DayInMonth(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    public CountdownStyle Style { get; set; } = CountdownStyle.Days;

    /// <summary>Titel vor der Restzeit.</summary>
    public bool ShowTitle { get; set; } = true;

    /// <summary>Anzeige nach Erreichen des Ziels.</summary>
    public string DoneText { get; set; } = "Geschafft!";

    public CountdownSettings Clone() => (CountdownSettings)MemberwiseClone();

    internal void Normalize()
    {
        Title = Title?.Trim() ?? "";
        DoneText = DoneText?.Trim() ?? "";
        if (!Enum.IsDefined(Style)) Style = CountdownStyle.Days;
        if (!Enum.IsDefined(Repeat)) Repeat = CountdownRepeat.None;
        Target = DateTime.SpecifyKind(Target, DateTimeKind.Unspecified);
        if (Target.Year < 1900 || Target.Year > 9000) Target = DateTime.Today.AddDays(7);
    }
}

public enum SeparatorStyle
{
    /// <summary>Durchgehende Linie.</summary>
    Line,

    /// <summary>Ein Punkt.</summary>
    Dot,

    /// <summary>Nur Abstand, nichts sichtbar.</summary>
    Space,
}

/// <summary>Einstellungen des Zeichnen-Widgets. Werkzeug, Farbe und Stärke wählt man in der Werkzeugleiste.</summary>
public sealed class DrawSettings
{
    /// <summary>Werkzeugleiste waagerecht (zwei Zeilen) statt senkrecht (zwei Spalten). Auch in der Werkzeugleiste umschaltbar.</summary>
    public bool ToolbarHorizontal { get; set; }

    /// <summary>Beim Beenden des Zeichnens alles löschen, statt das Gezeichnete stehen zu lassen.</summary>
    public bool ClearOnExit { get; set; }

    /// <summary>Neben dem Stift einen Papierkorb auf der Fläche zeigen, solange etwas gezeichnet ist.</summary>
    public bool ShowClearButton { get; set; } = true;

    public DrawSettings Clone() => (DrawSettings)MemberwiseClone();
}

/// <summary>
/// Einstellungen des Zwischenablage-Verlaufs. Der Verlauf selbst liegt nur im Arbeitsspeicher; gespeichert werden
/// nur die angehefteten Texte.
/// </summary>
public sealed class ClipboardSettings
{
    /// <summary>So viele Einträge behält der Verlauf (ohne angeheftete).</summary>
    public int MaxItems { get; set; } = 25;

    /// <summary>Angeheftete Texte – bleiben auch nach einem Neustart erhalten.</summary>
    public List<string> Pinned { get; set; } = [];

    /// <summary>Nach der Auswahl direkt in das vorher aktive Fenster einfügen (Strg + V); aus = nur kopieren.</summary>
    public bool PasteOnSelect { get; set; } = true;

    /// <summary>Kopierte Bilder in den Verlauf aufnehmen (höchstens <see cref="MaxImages"/>, nur im Arbeitsspeicher).</summary>
    public bool KeepImages { get; set; } = true;

    /// <summary>Anzahl der Einträge neben dem Symbol.</summary>
    public bool ShowCount { get; set; }

    public const int MaxImages = 5;
    public const int MaxPinned = 50;
    public const int MaxTextLength = 100_000;

    public ClipboardSettings Clone()
    {
        var copy = (ClipboardSettings)MemberwiseClone();
        copy.Pinned = [.. Pinned];
        return copy;
    }

    internal void Normalize()
    {
        MaxItems = Math.Clamp(MaxItems, 5, 100);
        Pinned = (Pinned ?? []).Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p.Length > MaxTextLength ? p[..MaxTextLength] : p)
            .Distinct(StringComparer.Ordinal).Take(MaxPinned).ToList();
    }
}

/// <summary>Eine Zeitzone der Weltuhr.</summary>
public sealed class WorldClockZone
{
    public string Id { get; set; } = WidgetEntry.NewId();

    /// <summary>Windows-Zeitzonen-ID, z. B. "Eastern Standard Time".</summary>
    public string TimeZoneId { get; set; } = "";

    /// <summary>Anzeigename, z. B. "New York"; leer = Name der Zeitzone.</summary>
    public string Label { get; set; } = "";

    public WorldClockZone Clone() => (WorldClockZone)MemberwiseClone();
}

/// <summary>Einstellungen der Weltuhr.</summary>
public sealed class WorldClockSettings
{
    public List<WorldClockZone> Zones { get; set; } =
    [
        new() { TimeZoneId = "Eastern Standard Time", Label = "New York" },
        new() { TimeZoneId = "Tokyo Standard Time", Label = "Tokio" },
    ];

    /// <summary>.NET-Zeitformat, z. B. "HH:mm" oder "h:mm tt".</summary>
    public string TimeFormat { get; set; } = "HH:mm";

    /// <summary>Namen der Orte vor der Uhrzeit.</summary>
    public bool ShowLabels { get; set; } = true;

    /// <summary>Alle Zeitzonen auf der Fläche; aus = nur die erste (alle stehen im Popup).</summary>
    public bool ShowAllInBar { get; set; } = true;

    /// <summary>„+1“ bzw. „−1“, wenn dort schon ein anderer Tag ist.</summary>
    public bool ShowDayOffset { get; set; } = true;

    public WorldClockSettings Clone()
    {
        var copy = (WorldClockSettings)MemberwiseClone();
        copy.Zones = Zones.Select(z => z.Clone()).ToList();
        return copy;
    }

    internal void Normalize()
    {
        Zones = (Zones ?? []).Where(z => z is not null && FindZone(z.TimeZoneId) is not null).Take(12).ToList();
        foreach (var zone in Zones)
        {
            if (string.IsNullOrWhiteSpace(zone.Id)) zone.Id = WidgetEntry.NewId();
            zone.TimeZoneId = zone.TimeZoneId.Trim();
            zone.Label = zone.Label?.Trim() ?? "";
        }
        foreach (var duplicate in Zones.GroupBy(z => z.Id).SelectMany(g => g.Skip(1)))
            duplicate.Id = WidgetEntry.NewId();
        try
        {
            if (string.IsNullOrWhiteSpace(TimeFormat)) throw new FormatException();
            _ = DateTime.Now.ToString(TimeFormat, CultureInfo.InvariantCulture);
        }
        catch (FormatException)
        {
            TimeFormat = "HH:mm";
        }
    }

    /// <summary>Kurzname aus dem Anzeigenamen der Zeitzone: "(UTC+09:00) Osaka, Sapporo, Tokio" → "Osaka".</summary>
    public static string DefaultLabel(TimeZoneInfo zone)
    {
        if (zone.Id == TimeZoneInfo.Utc.Id) return "UTC";
        string name = zone.DisplayName;
        int close = name.IndexOf(") ", StringComparison.Ordinal);
        if (name.StartsWith("(UTC", StringComparison.Ordinal) && close > 0) name = name[(close + 2)..];
        int comma = name.IndexOf(',');
        return (comma > 0 ? name[..comma] : name).Trim();
    }

    /// <summary>Zeitzone zur Windows-ID; null, wenn es sie auf diesem System nicht gibt.</summary>
    public static TimeZoneInfo? FindZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }
}

/// <summary>Ein Eintrag im Schnellstart.</summary>
public sealed class LauncherItem
{
    public string Id { get; set; } = WidgetEntry.NewId();

    /// <summary>Programm, Datei, Ordner oder Webadresse.</summary>
    public string Path { get; set; } = "";

    /// <summary>Nur Programme: Startparameter.</summary>
    public string Arguments { get; set; } = "";

    /// <summary>Anzeigename (Tooltip bzw. Beschriftung); leer = Dateiname.</summary>
    public string Name { get; set; } = "";

    public LauncherItem Clone() => (LauncherItem)MemberwiseClone();
}

/// <summary>Einstellungen des Schnellstarts.</summary>
public sealed class LauncherSettings
{
    public List<LauncherItem> Items { get; set; } = [];

    /// <summary>Namen neben den Symbolen.</summary>
    public bool ShowNames { get; set; }

    /// <summary>Symbolgröße in px.</summary>
    public double IconSize { get; set; } = 20;

    public LauncherSettings Clone()
    {
        var copy = (LauncherSettings)MemberwiseClone();
        copy.Items = Items.Select(i => i.Clone()).ToList();
        return copy;
    }

    internal void Normalize()
    {
        Items = (Items ?? []).Where(i => i is not null && !string.IsNullOrWhiteSpace(i.Path)).Take(40).ToList();
        foreach (var item in Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id)) item.Id = WidgetEntry.NewId();
            item.Path = item.Path.Trim().Trim('"');
            item.Arguments = item.Arguments?.Trim() ?? "";
            item.Name = item.Name?.Trim() ?? "";
        }
        foreach (var duplicate in Items.GroupBy(i => i.Id).SelectMany(g => g.Skip(1)))
            duplicate.Id = WidgetEntry.NewId();
        IconSize = double.IsFinite(IconSize) ? Math.Clamp(Math.Round(IconSize), 12, 64) : 20;
    }
}

public enum TemperatureUnit
{
    Celsius,
    Fahrenheit,
}

public enum WindSpeedUnit
{
    KilometersPerHour,
    MetersPerSecond,
    MilesPerHour,
}

/// <summary>Einstellungen des Wetter-Widgets. Die Daten kommen von Open-Meteo (ohne Konto, ohne Schlüssel).</summary>
public sealed class WeatherSettings
{
    /// <summary>Anzeigename des Orts, z. B. "Berlin, Deutschland".</summary>
    public string LocationName { get; set; } = "";

    /// <summary>Geografische Breite; null = noch kein Ort gewählt.</summary>
    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public TemperatureUnit Unit { get; set; } = TemperatureUnit.Celsius;

    public WindSpeedUnit WindUnit { get; set; } = WindSpeedUnit.KilometersPerHour;

    /// <summary>Abstand der Abfragen in Minuten.</summary>
    public int IntervalMinutes { get; set; } = 30;

    /// <summary>Wetterlage als Text neben der Temperatur, z. B. „Leicht bewölkt“.</summary>
    public bool ShowCondition { get; set; }

    /// <summary>Tageshöchst- und -tiefstwert neben der Temperatur.</summary>
    public bool ShowHighLow { get; set; }

    /// <summary>Ortsname neben der Temperatur.</summary>
    public bool ShowLocation { get; set; }
    [JsonIgnore]
    public bool HasLocation => Latitude is not null && Longitude is not null;

    public WeatherSettings Clone() => (WeatherSettings)MemberwiseClone();

    internal void Normalize()
    {
        LocationName = LocationName?.Trim() ?? "";
        if (Latitude is not { } lat || Longitude is not { } lon || !double.IsFinite(lat) || !double.IsFinite(lon) ||
            Math.Abs(lat) > 90 || Math.Abs(lon) > 180)
        {
            Latitude = null;
            Longitude = null;
        }
        if (!Enum.IsDefined(Unit)) Unit = TemperatureUnit.Celsius;
        if (!Enum.IsDefined(WindUnit)) WindUnit = WindSpeedUnit.KilometersPerHour;
        IntervalMinutes = Math.Clamp(IntervalMinutes, 10, 240);
    }
}

/// <summary>Einstellungen des Geräte-Akku-Widgets. Welche Geräte es zeigt, steht in <see cref="WidgetEntry.Devices"/>.</summary>
public sealed class DeviceBatterySettings
{
    /// <summary>Gerätenamen neben dem Ladestand.</summary>
    public bool ShowNames { get; set; }

    /// <summary>Bluetooth-Geräte, deren Akkustand Windows kennt (wie unter Einstellungen → Bluetooth).</summary>
    public bool Bluetooth { get; set; } = true;

    /// <summary>Kabellose Razer-Mäuse und -Tastaturen über Dongle oder Kabel (direkt am Gerät abgefragt).</summary>
    public bool Razer { get; set; } = true;

    /// <summary>Ab diesem Ladestand (Prozent) orange; ab der Hälfte davon rot.</summary>
    public int WarnLevel { get; set; } = 20;

    /// <summary>Windows-Benachrichtigung, sobald ein Gerät unter <see cref="WarnLevel"/> fällt.</summary>
    public bool NotifyLow { get; set; } = true;

    public DeviceBatterySettings Clone() => (DeviceBatterySettings)MemberwiseClone();

    internal void Normalize() => WarnLevel = Math.Clamp(WarnLevel, 5, 50);
}

public enum IpVersionDisplay
{
    IPv4,
    IPv6,
    Both,
}

/// <summary>Einstellungen des Widgets für die öffentliche IP-Adresse.</summary>
public sealed class PublicIpSettings
{
    public IpVersionDisplay Show { get; set; } = IpVersionDisplay.IPv4;

    /// <summary>„IPv4“/„IPv6“ vor der Adresse.</summary>
    public bool ShowLabel { get; set; } = true;

    /// <summary>Adresse auf der Fläche verbergen (z. B. beim Streamen) – sichtbar erst im Popup.</summary>
    public bool Mask { get; set; }

    /// <summary>Abstand der Abfragen in Minuten; nach einer Netzwerkänderung wird sofort neu abgefragt.</summary>
    public int IntervalMinutes { get; set; } = 15;

    public PublicIpSettings Clone() => (PublicIpSettings)MemberwiseClone();

    internal void Normalize()
    {
        if (!Enum.IsDefined(Show)) Show = IpVersionDisplay.IPv4;
        IntervalMinutes = Math.Clamp(IntervalMinutes, 1, 1440);
    }
}

/// <summary>Einstellungen des Helligkeit-/Nachtmodus-Widgets.</summary>
public sealed class BrightnessSettings
{
    /// <summary>Helligkeitsänderung je Mausrad-Raste bzw. Tastendruck in Prozentpunkten.</summary>
    public int WheelStep { get; set; } = 5;

    public bool ShowPercent { get; set; } = true;

    /// <summary>Farbtemperatur des Nachtmodus in Kelvin (6500 = neutral, kleiner = wärmer).</summary>
    public int NightTemperature { get; set; } = 3400;

    /// <summary>Nachtmodus ist eingeschaltet – wird beim Start wiederhergestellt.</summary>
    public bool NightMode { get; set; }

    /// <summary>Nachtmodus automatisch zwischen <see cref="NightFrom"/> und <see cref="NightTo"/>.</summary>
    public bool NightSchedule { get; set; }

    /// <summary>"HH:mm"</summary>
    public string NightFrom { get; set; } = "21:00";

    public string NightTo { get; set; } = "07:00";

    public const int MinTemperature = 2700;
    public const int NeutralTemperature = 6500;

    public BrightnessSettings Clone() => (BrightnessSettings)MemberwiseClone();

    internal void Normalize()
    {
        WheelStep = Math.Clamp(WheelStep, 1, 25);
        NightTemperature = Math.Clamp(NightTemperature, MinTemperature, NeutralTemperature);
        NightFrom = ParseTime(NightFrom) is { } from ? from.ToString(@"hh\:mm") : "21:00";
        NightTo = ParseTime(NightTo) is { } to ? to.ToString(@"hh\:mm") : "07:00";
    }

    public static TimeSpan? ParseTime(string? text) =>
        TimeSpan.TryParseExact(text?.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var time) && time < TimeSpan.FromDays(1)
            ? time
            : null;

    /// <summary>Liegt <paramref name="now"/> im Zeitplan (auch über Mitternacht, z. B. 21:00–07:00)?</summary>
    public bool IsNightTime(DateTime now)
    {
        if (ParseTime(NightFrom) is not { } from || ParseTime(NightTo) is not { } to || from == to) return false;
        var t = now.TimeOfDay;
        return from < to ? t >= from && t < to : t >= from || t < to;
    }
}

/// <summary>Einstellungen der Trennlinie. Die Farbe steht in <see cref="WidgetEntry.IconColor"/>.</summary>
public sealed class SeparatorSettings
{
    public SeparatorStyle Style { get; set; } = SeparatorStyle.Line;

    /// <summary>Senkrecht (zwischen Widgets nebeneinander) oder waagerecht (zwischen Widgets untereinander).</summary>
    public bool Horizontal { get; set; }

    /// <summary>Länge der Linie in px.</summary>
    public double Length { get; set; } = 20;

    /// <summary>Linienstärke bzw. Punktgröße in px.</summary>
    public double Thickness { get; set; } = 1;

    /// <summary>Freiraum auf jeder Seite in px.</summary>
    public double Padding { get; set; } = 6;

    public SeparatorSettings Clone() => (SeparatorSettings)MemberwiseClone();

    internal void Normalize()
    {
        if (!Enum.IsDefined(Style)) Style = SeparatorStyle.Line;
        Length = double.IsFinite(Length) ? Math.Clamp(Math.Round(Length), 1, 2000) : 20;
        Thickness = double.IsFinite(Thickness) ? Math.Clamp(Math.Round(Thickness, 1), 0.5, 50) : 1;
        Padding = double.IsFinite(Padding) ? Math.Clamp(Math.Round(Padding), 0, 500) : 6;
    }
}

/// <summary>Darstellung eines Mess-Widgets (CPU, GPU, RAM, Temperatur).</summary>
public enum MetricStyle
{
    /// <summary>Nur der Wert.</summary>
    Value,

    /// <summary>Wert und kleiner Füllbalken.</summary>
    Bar,

    /// <summary>Wert und Verlauf der letzten Minute.</summary>
    Graph,
}

/// <summary>Einstellungen eines Mess-Widgets. Farben stehen wie bei Audio-Widgets im <see cref="WidgetEntry"/>.</summary>
public sealed class MetricSettings
{
    public MetricStyle Style { get; set; } = MetricStyle.Bar;

    /// <summary>Kurzbezeichnung ("CPU", "GPU", "RAM") vor dem Wert.</summary>
    public bool ShowCaption { get; set; } = true;

    /// <summary>Ab 80 % (Temperatur 80 °C) orange, ab 95 % (90 °C) rot.</summary>
    public bool WarnColors { get; set; } = true;

    /// <summary>RAM und Videospeicher: belegte Gigabyte statt Prozent. Laufwerk: freie Gigabyte. Akku: Restlaufzeit.</summary>
    public bool ShowAmount { get; set; }

    /// <summary>Nur Laufwerk: Stammverzeichnis wie "C:\".</summary>
    public string Drive { get; set; } = "C:\\";

    /// <summary>Nur GPU-Widgets: Name der Grafikkarte; leer = automatisch (die mit dem meisten Videospeicher).</summary>
    public string Gpu { get; set; } = "";

    public MetricSettings Clone() => (MetricSettings)MemberwiseClone();

    internal void Normalize()
    {
        if (!Enum.IsDefined(Style)) Style = MetricStyle.Bar;
        Gpu = Gpu?.Trim() ?? "";
        Drive = NormalizeDrive(Drive);
    }

    /// <summary>"c", "C:", "c:\" → "C:\"; Ungültiges → "C:\".</summary>
    public static string NormalizeDrive(string? drive)
    {
        var letter = drive?.Trim().FirstOrDefault() ?? 'C';
        return char.IsAsciiLetter(letter) ? $"{char.ToUpperInvariant(letter)}:\\" : "C:\\";
    }
}

public enum WidgetSlot
{
    Left,
    Center,
    Right,
}

/// <summary>
/// Ein Widget auf der Leiste. Es gibt zwei Arten:
///  • feste Widgets (Uhr): sitzen in einem Bereich Links/Mitte/Rechts mit Randabstand und Mindestabstand;
///  • freie Widgets (alle anderen): werden per Drag &amp; Drop platziert (<see cref="Position"/>).
/// Freie Widgets weichen festen Widgets und einander aus – sie verschieben nie ein festes Widget.
/// </summary>
public sealed class WidgetEntry
{
    /// <summary>Eindeutige ID der Instanz – dasselbe Widget kann mehrfach vorkommen.</summary>
    public string Id { get; set; } = NewId();

    public WidgetType Type { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Nur feste Widgets: Bereich auf der Leiste.</summary>
    public WidgetSlot Slot { get; set; } = WidgetSlot.Center;

    /// <summary>Nur feste Widgets: Abstand zum Bildschirmrand in px (Links/Rechts; in der Mitte ohne Wirkung).</summary>
    public double OuterMargin { get; set; } = 12;

    /// <summary>Nur feste Widgets: Mindestabstand zu anderen Widgets in px.</summary>
    public double InnerSpacing { get; set; } = 16;

    /// <summary>Nur freie Widgets: Position der Widget-Mitte als Anteil der Leistenbreite (0 = links, 1 = rechts).</summary>
    public double Position { get; set; } = -1;

    /// <summary>
    /// Freie Platzierung (<see cref="AppearanceSettings.FreeWidgetPlacement"/>): Widget-Mitte als Anteil von Breite
    /// und Höhe der Leiste (0..1). null = noch nicht frei platziert → Startpunkt ist die normale Anordnung.
    /// </summary>
    public double? FreeX { get; set; }

    /// <summary>
    /// Freie Platzierung: linke Kante als Anteil der Leistenbreite (0..1). Löst <see cref="FreeX"/> ab, damit ein
    /// breiter werdendes Widget nach rechts wächst. Alte Werte (Mitte) werden beim ersten Anzeigen umgerechnet.
    /// </summary>
    public double? FreeLeft { get; set; }

    public double? FreeY { get; set; }

    /// <summary>
    /// Freie Platzierung: Abstand der linken Kante zum linken Rand der Fläche in px (geräteunabhängig). Löst die
    /// Anteile <see cref="FreeLeft"/>/<see cref="FreeY"/> ab, damit Widgets beim Ändern der Flächengröße stehen bleiben.
    /// null = noch nicht übernommen (dann gelten die Anteile bzw. die normale Anordnung).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FreeOffsetLeft { get; set; }

    /// <summary>Freie Platzierung: Abstand der Oberkante zum oberen Rand der Fläche in px.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FreeOffsetTop { get; set; }

    /// <summary>Größe des Widgets als Faktor (1 = normal, 2 = doppelt so groß). Wirkt auf Schrift, Symbole und Abstände.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>
    /// Nur dieses Widget gegen Verschieben sperren (Kontextmenü der Fläche) – unabhängig von „Position sperren“ der
    /// ganzen Fläche. Klicks auf das Widget funktionieren weiter.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Locked { get; set; }

    /// <summary>Nur Audio-Widgets: angezeigte Geräte (Endpunkt-IDs). Leer = alle aktiven Geräte.</summary>
    public List<string> Devices { get; set; } = [];

    /// <summary>Nur Audio-Widgets: Farbe der Symbole. null = Schriftfarbe der Leiste.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconColor { get; set; }

    /// <summary>Nur Audio-Widgets: Farbe der Gerätenamen. null = Schriftfarbe der Leiste.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LabelColor { get; set; }

    /// <summary>Nur Audio-Widgets: Farbe des Strichs unter dem Standardgerät. null = <see cref="DefaultIndicatorColor"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IndicatorColor { get; set; }

    public const string DefaultIndicatorColor = "#3DAEE9";

    /// <summary>Upload-/Download-Monitor: Uploadfarbe, solange <see cref="IconColor"/> nicht gesetzt ist.</summary>
    public const string DefaultUploadColor = "#E8A23B";

    /// <summary>
    /// Nur Audio-Widgets: globales Tastenkürzel, z. B. "Ctrl+Shift+I". Jeder Druck macht das nächste Gerät
    /// dieses Widgets (von links nach rechts) zum Standard. null = kein Tastenkürzel.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hotkey { get; set; }

    /// <summary>
    /// Lautstärke, Timer, Stoppuhr: globale Tastenkürzel je Aktion (<see cref="WidgetHotkeys"/>), z. B.
    /// { "VolumeUp": "Ctrl+Alt+Up" }. null = keine.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Hotkeys { get; set; }

    /// <summary>Nur Uhren: Format, Schrift und Farben – jede Uhr hat eigene Werte.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClockSettings? Clock { get; set; }

    /// <summary>Nur Mess-Widgets (CPU, GPU, RAM, Temperatur): Darstellung und Grafikkarte.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MetricSettings? Metric { get; set; }

    /// <summary>Nur Netzwerk-Widgets.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NetworkSettings? Network { get; set; }

    /// <summary>Nur Lautstärke-Widgets.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VolumeSettings? Volume { get; set; }

    /// <summary>Nur Timer-Widgets: die Timer dieses Widgets und das Verhalten nach Ablauf.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TimerSettings? Timer { get; set; }

    /// <summary>Nur Stoppuhren.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StopwatchSettings? Stopwatch { get; set; }

    /// <summary>Nur Upload-/Download-Monitore.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TrafficSettings? Traffic { get; set; }

    /// <summary>Nur Bilder.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImageSettings? Image { get; set; }

    /// <summary>Nur Notizen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NoteSettings? Note { get; set; }

    /// <summary>Nur Medien-Widgets.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MediaSettings? Media { get; set; }

    /// <summary>Nur Countdowns.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CountdownSettings? Countdown { get; set; }

    /// <summary>Nur Trennlinien.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SeparatorSettings? Separator { get; set; }

    /// <summary>Nur Zeichnen-Widgets.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DrawSettings? Draw { get; set; }

    /// <summary>Nur Zwischenablage-Verlauf.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClipboardSettings? Clipboard { get; set; }

    /// <summary>Nur Weltuhren.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorldClockSettings? WorldClock { get; set; }

    /// <summary>Nur Schnellstart.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LauncherSettings? Launcher { get; set; }

    /// <summary>Nur Wetter.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WeatherSettings? Weather { get; set; }

    /// <summary>Nur Geräte-Akku.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeviceBatterySettings? DeviceBattery { get; set; }

    /// <summary>Nur öffentliche IP-Adresse.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PublicIpSettings? PublicIp { get; set; }

    /// <summary>Nur Helligkeit und Nachtmodus.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BrightnessSettings? Brightness { get; set; }

    /// <summary>Nur FPS-Widgets.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FpsSettings? Fps { get; set; }

    public static bool IsMetricType(WidgetType type) =>
        type is WidgetType.Cpu or WidgetType.Gpu or WidgetType.Ram or WidgetType.GpuTemperature
            or WidgetType.Battery or WidgetType.Drive or WidgetType.GpuMemory or WidgetType.GpuClock or WidgetType.GpuFan;

    /// <summary>Mess-Widgets, die eine Grafikkarte auswerten (mit Auswahl der Karte).</summary>
    public static bool IsGpuType(WidgetType type) =>
        type is WidgetType.Gpu or WidgetType.GpuTemperature or WidgetType.GpuMemory or WidgetType.GpuClock or WidgetType.GpuFan;

    [JsonIgnore]
    public bool IsMetric => IsMetricType(Type);

    public const double MinScale = 0.25;
    public const double MaxScale = 8;

    public static bool IsFixedType(WidgetType type) => type == WidgetType.Clock;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFixed => IsFixedType(Type);

    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    public static List<WidgetEntry> Defaults() =>
    [
        new() { Type = WidgetType.Clock, Slot = WidgetSlot.Center },
        new() { Type = WidgetType.AudioOutputs, Position = 0.84 },
        new() { Type = WidgetType.AudioInputs, Position = 0.93 },
    ];

    internal void Normalize(ClockSettings? clockTemplate = null)
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = NewId();
        if (Type == WidgetType.Clock)
        {
            Clock ??= clockTemplate?.Clone() ?? new ClockSettings();
            Clock.Normalize();
        }
        else
        {
            Clock = null;
        }
        if (IsMetric)
        {
            Metric ??= new MetricSettings();
            Metric.Normalize();
        }
        else
        {
            Metric = null;
        }
        Network = Type == WidgetType.Network ? Network ?? new NetworkSettings() : null;
        Volume = Type == WidgetType.Volume ? Volume ?? new VolumeSettings() : null;
        Volume?.Normalize();
        Timer = Type == WidgetType.Timer ? Timer ?? new TimerSettings() : null;
        Timer?.Normalize();
        Stopwatch = Type == WidgetType.Stopwatch ? Stopwatch ?? new StopwatchSettings() : null;
        Traffic = Type == WidgetType.NetworkTraffic ? Traffic ?? new TrafficSettings() : null;
        Traffic?.Normalize();
        Image = Type == WidgetType.Image ? Image ?? new ImageSettings() : null;
        Image?.Normalize();
        Note = Type == WidgetType.Note ? Note ?? new NoteSettings() : null;
        Note?.Normalize();
        Media = Type == WidgetType.Media ? Media ?? new MediaSettings() : null;
        Media?.Normalize();
        Countdown = Type == WidgetType.Countdown ? Countdown ?? new CountdownSettings() : null;
        Countdown?.Normalize();
        Separator = Type == WidgetType.Separator ? Separator ?? new SeparatorSettings() : null;
        Separator?.Normalize();
        Draw = Type == WidgetType.Draw ? Draw ?? new DrawSettings() : null;
        Clipboard = Type == WidgetType.Clipboard ? Clipboard ?? new ClipboardSettings() : null;
        Clipboard?.Normalize();
        WorldClock = Type == WidgetType.WorldClock ? WorldClock ?? new WorldClockSettings() : null;
        WorldClock?.Normalize();
        Launcher = Type == WidgetType.Launcher ? Launcher ?? new LauncherSettings() : null;
        Launcher?.Normalize();
        Weather = Type == WidgetType.Weather ? Weather ?? new WeatherSettings() : null;
        Weather?.Normalize();
        DeviceBattery = Type == WidgetType.DeviceBattery ? DeviceBattery ?? new DeviceBatterySettings() : null;
        DeviceBattery?.Normalize();
        PublicIp = Type == WidgetType.PublicIp ? PublicIp ?? new PublicIpSettings() : null;
        PublicIp?.Normalize();
        Brightness = Type == WidgetType.Brightness ? Brightness ?? new BrightnessSettings() : null;
        Brightness?.Normalize();
        Fps = Type == WidgetType.Fps ? Fps ?? new FpsSettings() : null;
        Fps?.Normalize();
        IconColor =ColorUtil.IsValid(IconColor) ? IconColor!.Trim() : null;
        LabelColor = ColorUtil.IsValid(LabelColor) ? LabelColor!.Trim() : null;
        IndicatorColor = ColorUtil.IsValid(IndicatorColor) ? IndicatorColor!.Trim() : null;
        Hotkey = Type is WidgetType.AudioOutputs or WidgetType.AudioInputs && Core.Hotkey.TryParse(Hotkey, out var hotkey)
            ? hotkey.ToString()
            : null;
        // Nur bekannte Aktionen des Typs mit gültigem Kürzel behalten.
        var actions = (Hotkeys ?? [])
            .Where(h => WidgetHotkeys.Find(Type, h.Key) is not null && Core.Hotkey.TryParse(h.Value, out _))
            .ToDictionary(h => h.Key, h => { Core.Hotkey.TryParse(h.Value, out var parsed); return parsed.ToString(); });
        Hotkeys = actions.Count > 0 ? actions : null;
        if (!Enum.IsDefined(Slot)) Slot = WidgetSlot.Center;
        OuterMargin = double.IsFinite(OuterMargin) ? Math.Clamp(OuterMargin, 0, 500) : 12;
        InnerSpacing = double.IsFinite(InnerSpacing) ? Math.Clamp(InnerSpacing, 0, 200) : 16;
        if (!double.IsFinite(Position) || Position < 0 || Position > 1)
        {
            // Ältere Konfigurationen hatten nur einen Bereich – daraus eine Startposition ableiten.
            Position = Slot switch { WidgetSlot.Left => 0.08, WidgetSlot.Center => 0.65, _ => 0.9 };
        }
        static double? Fraction(double? value) =>
            value is { } v && double.IsFinite(v) ? Math.Clamp(Math.Round(v, 4), 0, 1) : null;
        FreeX = Fraction(FreeX);
        FreeLeft = Fraction(FreeLeft);
        FreeY = Fraction(FreeY);
        if (FreeY is null || (FreeX is null && FreeLeft is null))
        {
            FreeX = null;
            FreeLeft = null;
            FreeY = null;
        }
        static double? Offset(double? value) =>
            value is { } v && double.IsFinite(v) ? Math.Clamp(Math.Round(v, 1), 0, 20000) : null;
        FreeOffsetLeft = Offset(FreeOffsetLeft);
        FreeOffsetTop = Offset(FreeOffsetTop);
        if (FreeOffsetLeft is null || FreeOffsetTop is null)
        {
            FreeOffsetLeft = null;
            FreeOffsetTop = null;
        }
        Scale = double.IsFinite(Scale) && Scale > 0 ? Math.Clamp(Math.Round(Scale, 3), MinScale, MaxScale) : 1.0;
        Devices = (Devices ?? []).Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public enum AudioLabelMode
{
    IconOnly,
    IconAndName,
    NameOnly,
}

public sealed class AudioSettings
{
    public AudioLabelMode LabelMode { get; set; } = AudioLabelMode.IconOnly;

    /// <summary>Symbol und Kurzname je Gerät (über die stabile Endpunkt-ID). Welche Geräte ein Widget zeigt, steht im Widget.</summary>
    public List<AudioDeviceSettings> Devices { get; set; } = [];

    public AudioDeviceSettings? Find(string id) =>
        Devices.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

    internal void Normalize()
    {
        if (!Enum.IsDefined(LabelMode)) LabelMode = AudioLabelMode.IconOnly;
        Devices = (Devices ?? [])
            .Where(d => d is not null && !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();
        foreach (var device in Devices)
        {
            device.Icon = string.IsNullOrWhiteSpace(device.Icon) ? "Auto" : device.Icon.Trim();
            device.Label = device.Label?.Trim() ?? "";
        }
    }
}

public sealed class AudioDeviceSettings
{
    public string Id { get; set; } = "";

    /// <summary>Nur zur Lesbarkeit der JSON-Datei.</summary>
    public string Name { get; set; } = "";

    /// <summary>"Auto" (nach Gerätetyp) oder ein Symbolname, z. B. "Headphones", "Speakers", "Monitor".</summary>
    public string Icon { get; set; } = "Auto";

    /// <summary>Eigene Kurzbezeichnung; leer = Gerätename.</summary>
    public string Label { get; set; } = "";
}

public sealed class AppearanceSettings
{
    public BarMode Mode { get; set; } = BarMode.FullWidth;

    /// <summary>Fixierter Modus: immer über allen Fenstern (dynamisch gilt <see cref="Layer"/>). null nur in älteren Dateien.</summary>
    public bool? AlwaysOnTop { get; set; }

    /// <summary>Fixierter Modus: Bereich als Windows-AppBar reservieren (maximierte Fenster beginnen darunter). null nur in älteren Dateien.</summary>
    public bool? ReserveScreenSpace { get; set; }

    /// <summary>Fixierter Modus: Höhe in geräteunabhängigen Pixeln (bei 150 % Skalierung → 1,5 × physisch).</summary>
    public int Height { get; set; } = 48;

    /// <summary>Dynamischer Modus: Breite in geräteunabhängigen Pixeln.</summary>
    public int Width { get; set; } = 1200;

    /// <summary>Dynamischer Modus: Höhe in geräteunabhängigen Pixeln.</summary>
    public int FloatingHeight { get; set; } = 48;

    /// <summary>Dynamischer Modus: linke Kante relativ zum Monitor (px); null = horizontal zentriert.</summary>
    public double? FloatingLeft { get; set; }

    /// <summary>Dynamischer Modus: obere Kante relativ zum Monitor (px); null = <see cref="MarginTop"/>.</summary>
    public double? FloatingTop { get; set; }

    /// <summary>Dynamischer Modus: Abstand zum oberen Rand, solange keine eigene Position gesetzt ist.</summary>
    public int MarginTop { get; set; } = 8;

    /// <summary>Dynamischer Modus: Vordergrund, normal oder hinter allen Fenstern.</summary>
    public WindowLayer Layer { get; set; } = WindowLayer.Topmost;

    /// <summary>Widgets nicht mehr verschiebbar (jeder Modus); dynamisch auch Verschieben und Größenänderung der Leiste.</summary>
    public bool LockPosition { get; set; }

    /// <summary>
    /// Widgets (auch die Uhr) frei auf der Leiste platzieren – in beide Richtungen, Überlappen erlaubt.
    /// Aus: Uhr in festem Bereich, andere Widgets nebeneinander ohne Überlappung.
    /// </summary>
    public bool FreeWidgetPlacement { get; set; }

    public string BackgroundColor { get; set; } = "#202127";

    /// <summary>Hintergrund-Deckkraft 0..1 (0 = komplett durchsichtig), in beiden Modi.</summary>
    public double BackgroundOpacity { get; set; } = 1.0;

    /// <summary>Dynamischer Modus: Eckenradius.</summary>
    public int CornerRadius { get; set; } = 12;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDynamic => Mode == BarMode.Floating;

    /// <summary>
    /// Fenster braucht Transparenz (Layered-Window): immer im dynamischen Modus, fixiert nur bei Deckkraft unter 100 %.
    /// Eine voll deckende fixierte Leiste bleibt ein normales Fenster (schneller).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool NeedsTransparency => IsDynamic || BackgroundOpacity < 1.0;

    public string FontFamily { get; set; } = "Segoe UI";
    public string ForegroundColor { get; set; } = "#FFFFFF";
    public string BorderColor { get; set; } = "#2E3038";
    public int BorderThickness { get; set; } = 1;

    internal void Normalize()
    {
        var d = new AppearanceSettings();
        if (!Enum.IsDefined(Mode)) Mode = d.Mode;
        AlwaysOnTop ??= true;
        ReserveScreenSpace ??= true;
        if (!Enum.IsDefined(Layer)) Layer = d.Layer;
        Height = Math.Clamp(Height, 16, 200);
        Width = Math.Clamp(Width, 40, 10000);
        FloatingHeight = Math.Clamp(FloatingHeight, 16, 4000);
        FloatingLeft = FloatingLeft is { } left && double.IsFinite(left) ? Math.Clamp(Math.Round(left, 1), -10000, 20000) : null;
        FloatingTop = FloatingTop is { } top && double.IsFinite(top) ? Math.Clamp(Math.Round(top, 1), -10000, 20000) : null;
        BackgroundOpacity = double.IsFinite(BackgroundOpacity) ? Math.Clamp(BackgroundOpacity, 0.0, 1.0) : 1.0;
        CornerRadius = Math.Clamp(CornerRadius, 0, 2000);
        MarginTop = Math.Clamp(MarginTop, 0, 500);
        BorderThickness = Math.Clamp(BorderThickness, 0, 8);
        FontFamily = string.IsNullOrWhiteSpace(FontFamily) ? d.FontFamily : FontFamily.Trim();
        BackgroundColor = ValidColorOr(BackgroundColor, d.BackgroundColor);
        ForegroundColor = ValidColorOr(ForegroundColor, d.ForegroundColor);
        BorderColor = ValidColorOr(BorderColor, d.BorderColor);
    }

    internal static string ValidColorOr(string? value, string fallback) =>
        ColorUtil.IsValid(value) ? value!.Trim() : fallback;
}

public sealed class BehaviorSettings
{
    /// <summary>Nur zum Einlesen älterer Dateien: gilt seitdem je Leiste (<see cref="AppearanceSettings.AlwaysOnTop"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AlwaysOnTop { get; set; }

    /// <summary>Nur zum Einlesen älterer Dateien: gilt seitdem je Leiste (<see cref="AppearanceSettings.ReserveScreenSpace"/>).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ReserveScreenSpace { get; set; }

    /// <summary>Automatisches Ein-/Ausblenden am oberen Rand (geplant, noch nicht implementiert).</summary>
    public bool AutoHide { get; set; }

    public int AutoHideDelayMs { get; set; } = 400;

    /// <summary>Bei exklusivem Vollbild (klassische Spiele, D3D-Exclusive) ausblenden.</summary>
    public bool HideOnFullscreen { get; set; } = true;

    /// <summary>Bei randlosem Vollbild (Borderless, Browser-F11, Videoplayer) ausblenden.</summary>
    public bool HideOnBorderlessFullscreen { get; set; } = true;

    /// <summary>Wie lange "kein Vollbild" stabil sein muss, bevor die Leiste wieder erscheint (verhindert Flackern).</summary>
    public int FullscreenShowDelayMs { get; set; } = 300;

    /// <summary>Intervall der günstigen Sicherheitsprüfung (fängt z. B. abgestürzte Vollbild-Apps ab). 0 = aus.</summary>
    public int FullscreenSafetyCheckIntervalMs { get; set; } = 2000;

    /// <summary>Prozessnamen (ohne .exe), die nie als Vollbild gelten.</summary>
    public List<string> FullscreenExcludedProcesses { get; set; } = [];

    /// <summary>Prozessnamen (ohne .exe), die als Vollbild gelten, sobald sie den Arbeitsbereich ausfüllen.</summary>
    public List<string> FullscreenForcedProcesses { get; set; } = [];

    internal void Normalize()
    {
        AutoHideDelayMs = Math.Clamp(AutoHideDelayMs, 0, 10000);
        FullscreenShowDelayMs = Math.Clamp(FullscreenShowDelayMs, 0, 10000);
        FullscreenSafetyCheckIntervalMs = FullscreenSafetyCheckIntervalMs <= 0 ? 0 : Math.Clamp(FullscreenSafetyCheckIntervalMs, 500, 60000);
        FullscreenExcludedProcesses = NormalizeProcessList(FullscreenExcludedProcesses);
        FullscreenForcedProcesses = NormalizeProcessList(FullscreenForcedProcesses);
    }

    internal static List<string> NormalizeProcessList(IEnumerable<string?>? names) =>
        (names ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n)
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

public sealed class ClockSettings
{
    /// <summary>
    /// Nur informativ: wird beim Laden/Speichern aus <see cref="TimeFormat"/> abgeleitet.
    /// Das Zeitformat ist allein maßgeblich – "HH:mm" zeigt keine Sekunden, "HH:mm:ss" schon.
    /// </summary>
    public bool ShowSeconds { get; set; } = true;

    /// <summary>.NET-Zeitformat, z. B. "HH:mm:ss" (24 h mit Sekunden) oder "HH:mm".</summary>
    public string TimeFormat { get; set; } = "HH:mm:ss";

    public bool ShowDate { get; set; }
    public string DateFormat { get; set; } = "dddd, d. MMMM yyyy";
    public bool ShowWeekNumber { get; set; }

    /// <summary>{0} wird durch die ISO-8601-Kalenderwoche ersetzt.</summary>
    public string WeekNumberFormat { get; set; } = "KW {0}";

    public double FontSize { get; set; } = 20;

    /// <summary>Leer = Schriftart aus Appearance.FontFamily.</summary>
    public string FontFamily { get; set; } = "";

    /// <summary>Normal, Medium, SemiBold oder Bold.</summary>
    public string FontWeight { get; set; } = "SemiBold";

    public string FontColor { get; set; } = "#FFFFFF";
    public double DateFontSize { get; set; } = 11;
    public string DateFontColor { get; set; } = "#B4B7C2";

    /// <summary>Kultur für Wochentags-/Monatsnamen, z. B. "de-DE". Leer = Systemeinstellung.</summary>
    public string Culture { get; set; } = "";

    public ClockSettings Clone() => (ClockSettings)MemberwiseClone();

    /// <summary>
    /// Kultur für die Uhr. Kulturen ohne AM/PM-Bezeichner (z. B. de-DE unter Windows) erhalten "AM"/"PM",
    /// damit 12-Stunden-Formate eindeutig bleiben.
    /// </summary>
    public static CultureInfo ResolveCulture(string? name)
    {
        var culture = string.IsNullOrEmpty(name) ? CultureInfo.CurrentCulture : CultureInfo.GetCultureInfo(name);
        var dtf = culture.DateTimeFormat;
        if (!string.IsNullOrEmpty(dtf.AMDesignator) && !string.IsNullOrEmpty(dtf.PMDesignator)) return culture;

        var copy = (CultureInfo)culture.Clone();
        if (string.IsNullOrEmpty(copy.DateTimeFormat.AMDesignator)) copy.DateTimeFormat.AMDesignator = "AM";
        if (string.IsNullOrEmpty(copy.DateTimeFormat.PMDesignator)) copy.DateTimeFormat.PMDesignator = "PM";
        return CultureInfo.ReadOnly(copy);
    }

    /// <summary>True, wenn das Format Sekunden ausgibt (auch einbuchstabige Standardformate wie "T").</summary>
    public static bool HasSeconds(string? format) =>
        !string.IsNullOrEmpty(format) &&
        (format.Length == 1 ? "TGFUsuoORr".Contains(format[0]) : format.Contains('s'));

    internal void Normalize()
    {
        var d = new ClockSettings();
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(FontSize, 6, 200) : d.FontSize;
        DateFontSize = double.IsFinite(DateFontSize) ? Math.Clamp(DateFontSize, 6, 100) : d.DateFontSize;
        FontFamily = FontFamily?.Trim() ?? "";
        FontWeight = FontWeight is "Normal" or "Medium" or "SemiBold" or "Bold" ? FontWeight : d.FontWeight;
        FontColor = AppearanceSettings.ValidColorOr(FontColor, d.FontColor);
        DateFontColor = AppearanceSettings.ValidColorOr(DateFontColor, d.DateFontColor);

        Culture = Culture?.Trim() ?? "";
        if (Culture.Length > 0)
        {
            try
            {
                CultureInfo.GetCultureInfo(Culture);
            }
            catch (CultureNotFoundException)
            {
                Culture = "";
            }
        }

        if (!IsValidDateFormat(TimeFormat)) TimeFormat = d.TimeFormat;
        ShowSeconds = HasSeconds(TimeFormat);
        if (!IsValidDateFormat(DateFormat)) DateFormat = d.DateFormat;
        if (!IsValidWeekFormat(WeekNumberFormat)) WeekNumberFormat = d.WeekNumberFormat;
    }

    private static bool IsValidDateFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return false;
        try
        {
            _ = DateTime.Now.ToString(format, CultureInfo.InvariantCulture);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsValidWeekFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return false;
        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, format, 42);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class SystemSettings
{
    public bool StartWithWindows { get; set; }

    /// <summary>Nur noch zum Einlesen älterer Dateien (vor mehreren Leisten): wird nach <see cref="BarSettings.MonitorSelection"/> übernommen.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MonitorSelection { get; set; }

    /// <summary>Info-/Diagnose-Logging. Fehler und Warnungen werden immer protokolliert.</summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>WPF-Software-Rendering: keine GPU-Nutzung durch die Leiste (empfohlen beim Spielen). Neustart nötig.</summary>
    public bool UseSoftwareRendering { get; set; } = true;

    internal void Normalize()
    {
    }
}

/// <summary>Einstellungen des FPS-Widgets.</summary>
public sealed class FpsSettings
{
    /// <summary>Zeit je Bild in Millisekunden neben den FPS.</summary>
    public bool ShowFrameTime { get; set; }

    /// <summary>1-%-Low (FPS der langsamsten Bilder der letzten 10 Sekunden) neben den FPS.</summary>
    public bool ShowOnePercentLow { get; set; }

    /// <summary>Name des gemessenen Programms.</summary>
    public bool ShowProcess { get; set; }

    /// <summary>Unter diesen FPS rot, bis zum Doppelten orange; 0 = keine Warnfarben.</summary>
    public int WarnBelow { get; set; } = 30;

    public FpsSettings Clone() => (FpsSettings)MemberwiseClone();

    internal void Normalize() => WarnBelow = Math.Clamp(WarnBelow, 0, 500);
}
