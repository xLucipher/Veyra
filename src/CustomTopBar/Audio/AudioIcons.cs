using System.Windows.Media;

namespace CustomTopBar.Audio;

public enum IconCategory
{
    Output,
    Input,
    General,
}

/// <summary>
/// Ein Gerätesymbol: entweder ein Zeichen aus "Segoe Fluent Icons" (<see cref="Glyph"/>) oder eine eigene
/// Vektorgrafik (<see cref="PathData"/>) auf einem 24×24-Raster im gleichen Linienstil.
/// </summary>
public sealed record AudioIcon(string Key, string Name, IconCategory Category, string? Glyph = null, string? PathData = null);

/// <summary>Symbolkatalog für Audiogeräte – keine Bilddateien, alles skaliert verlustfrei mit der DPI.</summary>
public static class AudioIcons
{
    public const string FontFamily = "Segoe Fluent Icons, Segoe MDL2 Assets";
    public const string AutoKey = "Auto";

    // Hilfen für Kreise im Pfad-Format (zwei Halbbögen, geschlossen).
    private static string Circle(double cx, double cy, double r) =>
        FormattableString.Invariant($"M{cx - r},{cy} A{r},{r} 0 1 0 {cx + r},{cy} A{r},{r} 0 1 0 {cx - r},{cy} Z ");

    public static readonly IReadOnlyList<AudioIcon> All =
    [
        // ---- Wiedergabe -----------------------------------------------------------------
        new("Speakers", "Lautsprecher", IconCategory.Output, Glyph: ""),
        new("SpeakerBox", "Regallautsprecher", IconCategory.Output, PathData:
            "M7,2 H17 A2,2 0 0 1 19,4 V20 A2,2 0 0 1 17,22 H7 A2,2 0 0 1 5,20 V4 A2,2 0 0 1 7,2 Z " +
            Circle(12, 15, 3.5) + Circle(12, 7, 1.5)),
        new("StereoSpeakers", "Stereo-Lautsprecher", IconCategory.Output, PathData:
            "M2.5,4 H8.5 A1.5,1.5 0 0 1 10,5.5 V18.5 A1.5,1.5 0 0 1 8.5,20 H2.5 A1.5,1.5 0 0 1 1,18.5 V5.5 A1.5,1.5 0 0 1 2.5,4 Z " +
            Circle(5.5, 14, 2.5) + Circle(5.5, 8, 1) +
            "M15.5,4 H21.5 A1.5,1.5 0 0 1 23,5.5 V18.5 A1.5,1.5 0 0 1 21.5,20 H15.5 A1.5,1.5 0 0 1 14,18.5 V5.5 A1.5,1.5 0 0 1 15.5,4 Z " +
            Circle(18.5, 14, 2.5) + Circle(18.5, 8, 1)),
        new("Soundbar", "Soundbar", IconCategory.Output, PathData:
            "M3,9 H21 A2,2 0 0 1 23,11 V13 A2,2 0 0 1 21,15 H3 A2,2 0 0 1 1,13 V11 A2,2 0 0 1 3,9 Z " +
            Circle(5, 12, 0.9) + Circle(9.5, 12, 0.9) + Circle(14.5, 12, 0.9) + Circle(19, 12, 0.9) +
            "M8,5.5 Q12,3.5 16,5.5 M8,18.5 Q12,20.5 16,18.5"),
        new("SoundbarSub", "Soundbar mit Subwoofer", IconCategory.Output, PathData:
            "M3,3 H21 A1.5,1.5 0 0 1 22.5,4.5 V6.5 A1.5,1.5 0 0 1 21,8 H3 A1.5,1.5 0 0 1 1.5,6.5 V4.5 A1.5,1.5 0 0 1 3,3 Z " +
            Circle(6, 5.5, 0.8) + Circle(12, 5.5, 0.8) + Circle(18, 5.5, 0.8) +
            "M15.5,11 H20.5 A1.5,1.5 0 0 1 22,12.5 V20.5 A1.5,1.5 0 0 1 20.5,22 H15.5 A1.5,1.5 0 0 1 14,20.5 V12.5 A1.5,1.5 0 0 1 15.5,11 Z " +
            Circle(18, 16.5, 2.5)),
        new("Headphones", "Kopfhörer", IconCategory.Output, Glyph: ""),
        new("StudioHeadphones", "Over-Ear-Kopfhörer", IconCategory.Output, PathData:
            "M4,15 V12 A8,8 0 0 1 20,12 V15 " +
            "M3,14 H6 A1,1 0 0 1 7,15 V20 A1,1 0 0 1 6,21 H4 A2,2 0 0 1 2,19 V15 A1,1 0 0 1 3,14 Z " +
            "M18,14 H21 A1,1 0 0 1 22,15 V19 A2,2 0 0 1 20,21 H18 A1,1 0 0 1 17,20 V15 A1,1 0 0 1 18,14 Z"),
        new("Earbuds", "In-Ear / Earbuds", IconCategory.Output, PathData:
            Circle(7, 7, 3.5) + "M6,10.3 V19.5 A1.25,1.25 0 0 0 8.5,19.5 V10.5 " +
            Circle(17, 7, 3.5) + "M15.5,10.5 V19.5 A1.25,1.25 0 0 0 18,19.5 V10.3"),
        new("Monitor", "Monitor / TV", IconCategory.Output, Glyph: ""),

        // ---- Mikrofone ------------------------------------------------------------------
        new("Microphone", "Mikrofon", IconCategory.Input, Glyph: ""),
        new("StudioMic", "Studiomikrofon", IconCategory.Input, PathData:
            "M12,2 A3,3 0 0 1 15,5 V11 A3,3 0 0 1 9,11 V5 A3,3 0 0 1 12,2 Z M9,6.5 H15 M9,9 H15 " +
            "M6,9 V11 A6,6 0 0 0 18,11 V9 M12,17 V21 M8,21 H16"),
        new("PodcastMic", "Podcast-Mikrofon (Schwenkarm)", IconCategory.Input, PathData:
            "M8,2 H16 A2,2 0 0 1 18,4 V11 A2,2 0 0 1 16,13 H8 A2,2 0 0 1 6,11 V4 A2,2 0 0 1 8,2 Z M6,5.5 H18 M6,8.5 H18 " +
            "M12,13 V16 L19,20 V22 M16,22 H22"),
        new("DeskMic", "USB-Tischmikrofon", IconCategory.Input, PathData:
            Circle(12, 8, 5) + "M7.5,6 H16.5 M7.5,10 H16.5 M12,13 V18.5 " +
            "M7,18.5 H17 A1.5,1.5 0 0 1 17,21.5 H7 A1.5,1.5 0 0 1 7,18.5 Z"),
        new("HandheldMic", "Handmikrofon", IconCategory.Input, PathData:
            Circle(8, 8, 4.5) + "M5,5 L11,11 M11.2,11.8 L19.5,21 A1.5,1.5 0 0 0 21.6,19 L12.4,10.6"),
        new("Lavalier", "Ansteckmikrofon", IconCategory.Input, PathData:
            "M12,2 A2,2 0 0 1 14,4 V8 H10 V4 A2,2 0 0 1 12,2 Z M9,8 H15 V11 H9 Z M12,11 C12,17 18,15 18,22"),
        new("Webcam", "Webcam", IconCategory.Input, PathData:
            Circle(12, 10, 7) + Circle(12, 10, 2.5) + "M12,17 V20 M7,21 H17"),
        new("AudioInterface", "Audio-Interface / XLR", IconCategory.Input, PathData:
            "M3,7 H21 A2,2 0 0 1 23,9 V16 A2,2 0 0 1 21,18 H3 A2,2 0 0 1 1,16 V9 A2,2 0 0 1 3,7 Z " +
            Circle(6.5, 12.5, 2.5) + Circle(13, 12.5, 2.5) + "M18.5,10 V15 M20.5,11.5 V15"),

        // ---- Allgemein ------------------------------------------------------------------
        new("Headset", "Headset", IconCategory.General, Glyph: ""),
        new("Bluetooth", "Bluetooth", IconCategory.General, Glyph: ""),
        new("Virtual", "Virtuelles Gerät / Kabel", IconCategory.General, PathData:
            "M9,2 V6 M15,2 V6 M6.5,6 H17.5 V10 A5.5,5.5 0 0 1 6.5,10 Z M12,15.5 V18 A3,3 0 0 1 9,21 H6"),
        new("Waveform", "Stream / Mix", IconCategory.General, PathData:
            "M2,12 H4 M6,9 V15 M10,5 V19 M14,8 V16 M18,10 V14 M20,12 H22"),
        new("Volume", "Lautstärke", IconCategory.General, Glyph: ""),
        new("Music", "Musik", IconCategory.General, Glyph: ""),
        new("Game", "Spiele", IconCategory.General, Glyph: ""),
        new("Phone", "Telefon", IconCategory.General, Glyph: ""),
        new("Laptop", "Laptop", IconCategory.General, Glyph: ""),
    ];

    private static readonly Dictionary<string, Geometry> GeometryCache = [];

    public static AudioIcon Get(string? key) =>
        All.FirstOrDefault(i => i.Key == key) ?? All[0];

    public static IEnumerable<AudioIcon> InCategory(IconCategory category) => All.Where(i => i.Category == category);

    public static Geometry? GetGeometry(AudioIcon icon)
    {
        if (icon.PathData is null) return null;
        if (!GeometryCache.TryGetValue(icon.Key, out var geometry))
        {
            geometry = Geometry.Parse(icon.PathData);
            geometry.Freeze();
            GeometryCache[icon.Key] = geometry;
        }
        return geometry;
    }

    /// <summary>Gewähltes Symbol oder – bei "Auto" – das automatisch passende.</summary>
    public static string Resolve(string? key, AudioFlow flow, AudioFormFactor formFactor, string name) =>
        !string.IsNullOrEmpty(key) && key != AutoKey && All.Any(i => i.Key == key) ? key : Auto(flow, formFactor, name);

    /// <summary>
    /// Automatische Wahl: zuerst nach typischen Namensbestandteilen (funktioniert für die meisten Geräte
    /// herstellerübergreifend), dann nach dem von Windows gemeldeten Formfaktor.
    /// </summary>
    public static string Auto(AudioFlow flow, AudioFormFactor formFactor, string name)
    {
        string n = name.ToLowerInvariant();
        bool Has(params string[] parts) => parts.Any(n.Contains);
        bool input = flow == AudioFlow.Input;

        if (Has("virtual", "vb-audio", "cable", "voicemeeter", "voicemod", "nvidia broadcast", "steam streaming")) return "Virtual";
        if (Has("soundbar", "sound bar")) return "Soundbar";
        if (Has("webcam", "camera", "kamera", "brio", "c920", "c922", "facecam", "kiyo")) return input ? "Webcam" : "Speakers";
        if (input && Has("xlr", "interface", "focusrite", "scarlett", "goxlr", "umc", "audient", "motu", "steinberg")) return "AudioInterface";
        if (input && Has("yeti", "quadcast", "wave:", "seiren", "snowball", "fifine", "podmic", "usb")) return "DeskMic";
        if (Has("headset", "hands-free", "freisprech")) return "Headset";
        if (Has("airpods", "buds", "earbuds", "in-ear")) return input ? "Headset" : "Earbuds";
        if (Has("kopfhörer", "headphone")) return input ? "Headset" : "StudioHeadphones";
        if (Has("bluetooth")) return "Bluetooth";

        return formFactor switch
        {
            AudioFormFactor.Headphones => input ? "Headset" : "Headphones",
            AudioFormFactor.Headset or AudioFormFactor.Handset => "Headset",
            AudioFormFactor.DigitalAudioDisplayDevice => "Monitor",
            _ => input ? "Microphone" : "Speakers",
        };
    }
}
