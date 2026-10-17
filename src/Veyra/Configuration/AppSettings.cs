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

    /// <summary>Alle Leisten. Es gibt immer mindestens eine.</summary>
    public List<BarSettings> Bars { get; set; } = [];

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

        var legacyClock = Clock;
        Clock = null;

        for (int i = 0; i < Bars.Count; i++) Bars[i].Normalize(i, legacyClock);
        // Doppelte IDs (z. B. kopierte Einträge) würden Leisten bzw. Drag & Drop verwechseln – über alle Leisten hinweg.
        foreach (var duplicate in Bars.GroupBy(b => b.Id).SelectMany(g => g.Skip(1)))
            duplicate.Id = WidgetEntry.NewId();
        foreach (var duplicate in Bars.SelectMany(b => b.Widgets).GroupBy(w => w.Id).SelectMany(g => g.Skip(1)))
            duplicate.Id = WidgetEntry.NewId();

        Behavior.Normalize();
        System.Normalize();
        Audio.Normalize();
        Updates.SkippedVersion = Updates.SkippedVersion?.Trim() ?? "";
    }

    public const string MonitorDefault = "Primary";
}

/// <summary>Eine Leiste: eigenes Fenster mit Modus, Größe, Aussehen, Ebene, Monitor und eigenen Widgets.</summary>
public sealed class BarSettings
{
    public string Id { get; set; } = WidgetEntry.NewId();

    /// <summary>Anzeigename in den Einstellungen; leer = "Leiste N".</summary>
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
        Name = string.IsNullOrWhiteSpace(Name) ? $"Leiste {index + 1}" : Name.Trim();
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

    public double? FreeY { get; set; }

    /// <summary>Größe des Widgets als Faktor (1 = normal, 2 = doppelt so groß). Wirkt auf Schrift, Symbole und Abstände.</summary>
    public double Scale { get; set; } = 1.0;

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

    /// <summary>Nur Uhren: Format, Schrift und Farben – jede Uhr hat eigene Werte.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ClockSettings? Clock { get; set; }

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
        IconColor = ColorUtil.IsValid(IconColor) ? IconColor!.Trim() : null;
        LabelColor = ColorUtil.IsValid(LabelColor) ? LabelColor!.Trim() : null;
        IndicatorColor = ColorUtil.IsValid(IndicatorColor) ? IndicatorColor!.Trim() : null;
        if (!Enum.IsDefined(Slot)) Slot = WidgetSlot.Center;
        OuterMargin = double.IsFinite(OuterMargin) ? Math.Clamp(OuterMargin, 0, 500) : 12;
        InnerSpacing = double.IsFinite(InnerSpacing) ? Math.Clamp(InnerSpacing, 0, 200) : 16;
        if (!double.IsFinite(Position) || Position < 0 || Position > 1)
        {
            // Ältere Konfigurationen hatten nur einen Bereich – daraus eine Startposition ableiten.
            Position = Slot switch { WidgetSlot.Left => 0.08, WidgetSlot.Center => 0.65, _ => 0.9 };
        }
        if (FreeX is not { } fx || !double.IsFinite(fx) || FreeY is not { } fy || !double.IsFinite(fy))
        {
            FreeX = null;
            FreeY = null;
        }
        else
        {
            FreeX = Math.Clamp(Math.Round(fx, 4), 0, 1);
            FreeY = Math.Clamp(Math.Round(fy, 4), 0, 1);
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

    /// <summary>Dynamischer Modus: Verschieben und Größenänderung mit der Maus verhindern.</summary>
    public bool LockPosition { get; set; }

    /// <summary>
    /// Widgets (auch die Uhr) frei auf der Leiste platzieren – in beide Richtungen, Überlappen erlaubt.
    /// Aus: Uhr in festem Bereich, andere Widgets nebeneinander ohne Überlappung.
    /// </summary>
    public bool FreeWidgetPlacement { get; set; }

    public string BackgroundColor { get; set; } = "#202127";

    /// <summary>Dynamischer Modus: Hintergrund-Deckkraft 0..1 (0 = komplett durchsichtig).</summary>
    public double BackgroundOpacity { get; set; } = 1.0;

    /// <summary>Dynamischer Modus: Eckenradius.</summary>
    public int CornerRadius { get; set; } = 12;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsDynamic => Mode == BarMode.Floating;

    public string FontFamily { get; set; } = "Segoe UI";
    public string ForegroundColor { get; set; } = "#FFFFFF";
    public string BorderColor { get; set; } = "#2E3038";
    public int BorderThickness { get; set; } = 1;

    internal void Normalize()
    {
        var d = new AppearanceSettings();
        if (!Enum.IsDefined(Mode)) Mode = d.Mode;
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
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>Reserviert den Bereich als Windows-AppBar (maximierte Fenster beginnen unterhalb der Leiste).</summary>
    public bool ReserveScreenSpace { get; set; } = true;

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
