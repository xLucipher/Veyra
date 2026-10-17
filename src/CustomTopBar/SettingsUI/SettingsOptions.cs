using System.Diagnostics;
using System.Globalization;

namespace CustomTopBar.SettingsUI;

public sealed record Option(string Value, string Display);

public sealed record IntOption(int Value, string Display);

/// <summary>Auswahllisten für das Einstellungsfenster. Formatlisten zeigen eine Live-Vorschau in der gewählten Sprache.</summary>
public static class SettingsOptions
{
    private static readonly (string Format, string Description)[] TimeFormats =
    [
        ("HH:mm", "24 Stunden"),
        ("HH:mm:ss", "24 Stunden mit Sekunden"),
        ("H:mm", "24 Stunden, ohne führende Null"),
        ("H:mm:ss", "24 Stunden, ohne führende Null, mit Sekunden"),
        ("hh:mm tt", "12 Stunden"),
        ("hh:mm:ss tt", "12 Stunden mit Sekunden"),
        ("h:mm tt", "12 Stunden, ohne führende Null"),
        ("h:mm:ss tt", "12 Stunden, ohne führende Null, mit Sekunden"),
    ];

    private static readonly string[] DateFormats =
    [
        "dddd, d. MMMM yyyy",
        "ddd, d. MMMM yyyy",
        "ddd, d. MMM yyyy",
        "d. MMMM yyyy",
        "dddd, dd.MM.yyyy",
        "ddd, dd.MM.yyyy",
        "dd.MM.yyyy",
        "ddd, dd.MM.",
        "dddd",
        "yyyy-MM-dd",
        "D",
        "d",
    ];

    private static readonly string[] WeekFormats = ["KW {0}", "KW{0}", "Woche {0}", "(KW {0})", "W{0}", "Wk {0}"];

    private static readonly string[] Cultures =
        ["de-DE", "de-AT", "de-CH", "en-US", "en-GB", "fr-FR", "it-IT", "es-ES", "nl-NL", "pl-PL", "sv-SE", "da-DK", "cs-CZ"];

    public static IReadOnlyList<Option> FontWeights { get; } =
    [
        new("Normal", "Normal"),
        new("Medium", "Mittel"),
        new("SemiBold", "Halbfett"),
        new("Bold", "Fett"),
    ];

    public static IReadOnlyList<int> HeightPresets { get; } = [28, 32, 36, 40, 44, 48, 52, 56, 64, 72];

    public static IReadOnlyList<double> FontSizePresets { get; } = [12, 14, 16, 18, 20, 22, 24, 26, 28, 32, 36];

    public static IReadOnlyList<double> DateFontSizePresets { get; } = [9, 10, 11, 12, 13, 14, 16];

    public static IReadOnlyList<Option> BuildTimeFormats(CultureInfo culture, string current)
    {
        // Beispielzeit 09:05:42 macht führende Null und Sekunden in der Vorschau sichtbar.
        var sample = DateTime.Today.AddHours(9).AddMinutes(5).AddSeconds(42);
        return WithCurrent(TimeFormats.Select(t => new Option(t.Format, $"{sample.ToString(t.Format, culture)}   ·   {t.Description}")),
            current, f => Preview(sample, f, culture));
    }

    public static IReadOnlyList<Option> BuildDateFormats(CultureInfo culture, string current)
    {
        var today = DateTime.Today;
        return WithCurrent(DateFormats.Select(f => new Option(f, f switch
            {
                "D" => $"{today.ToString(f, culture)}   ·   langes Systemformat",
                "d" => $"{today.ToString(f, culture)}   ·   kurzes Systemformat",
                _ => today.ToString(f, culture),
            })),
            current, f => Preview(today, f, culture));
    }

    public static IReadOnlyList<Option> BuildWeekFormats(CultureInfo culture, string current)
    {
        int week = ISOWeek.GetWeekOfYear(DateTime.Today);
        return WithCurrent(WeekFormats.Select(f => new Option(f, string.Format(culture, f, week))),
            current, f => SafeFormat(culture, f, week));
    }

    public static IReadOnlyList<Option> BuildCultures(string current)
    {
        var list = new List<Option> { new("", $"Systemeinstellung ({CultureInfo.CurrentCulture.NativeName})") };
        list.AddRange(Cultures.Select(c => new Option(c, $"{CultureInfo.GetCultureInfo(c).NativeName}  ({c})")));
        if (!list.Any(o => o.Value.Equals(current, StringComparison.OrdinalIgnoreCase)))
            list.Add(new Option(current, current));
        return list;
    }

    public static IReadOnlyList<IntOption> BuildDelays(int current) =>
        WithCurrent([0, 100, 200, 300, 500, 750, 1000, 1500, 2000], current, ms => ms == 0 ? "Sofort" : $"{ms} ms");

    public static IReadOnlyList<IntOption> BuildSafetyIntervals(int current) =>
        WithCurrent([0, 1000, 2000, 3000, 5000, 10000], current, ms => ms == 0 ? "Aus" : $"alle {ms / 1000.0:0.#} s");

    public static IReadOnlyList<IntOption> BuildBorderThickness(int current) =>
        WithCurrent([0, 1, 2, 3, 4], current, px => px == 0 ? "Keine" : $"{px} px");

    /// <summary>Prozesse mit sichtbarem Hauptfenster – zum bequemen Hinzufügen zu den Vollbild-Ausnahmen.</summary>
    public static IReadOnlyList<string> GetWindowedProcessNames()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero && process.Id != Environment.ProcessId)
                    names.Add(process.ProcessName);
            }
            catch
            {
                // Prozess bereits beendet oder Zugriff verweigert.
            }
            finally
            {
                process.Dispose();
            }
        }
        return names.ToList();
    }

    private static IReadOnlyList<Option> WithCurrent(IEnumerable<Option> options, string current, Func<string, string> preview)
    {
        var list = options.ToList();
        if (!string.IsNullOrEmpty(current) && !list.Any(o => o.Value == current))
            list.Add(new Option(current, $"{preview(current)}   ·   benutzerdefiniert ({current})"));
        return list;
    }

    private static IReadOnlyList<IntOption> WithCurrent(int[] values, int current, Func<int, string> display)
    {
        var all = values.Contains(current) ? values : values.Append(current).Order().ToArray();
        return all.Select(v => new IntOption(v, display(v))).ToList();
    }

    private static string Preview(DateTime value, string format, CultureInfo culture)
    {
        try
        {
            return value.ToString(format, culture);
        }
        catch (FormatException)
        {
            return "?";
        }
    }

    private static string SafeFormat(CultureInfo culture, string format, int week)
    {
        try
        {
            return string.Format(culture, format, week);
        }
        catch (FormatException)
        {
            return "?";
        }
    }
}
