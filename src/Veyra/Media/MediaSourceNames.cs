using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Veyra.Media;

/// <summary>
/// Lesbarer Name einer Wiedergabequelle aus der App-ID der Windows-Mediensteuerung: „Spotify“, „Medienwiedergabe“ …
/// Browser melden nur sich selbst (z. B. „Chrome“), nicht die Seite. Deshalb wird das Browserfenster gesucht, dessen
/// Titel den gespielten Titel enthält, und darin ein bekannter Dienst erkannt („YouTube Music · Chrome“). Das klappt,
/// solange der Tab im Fenster vorne ist; sonst bleibt es beim Browsernamen. Läuft auf dem Medien-Hintergrund-Thread.
/// </summary>
internal static class MediaSourceNames
{
    /// <summary>Bekannte Dienste, längere Namen zuerst („YouTube Music“ vor „YouTube“).</summary>
    private static readonly string[] Services =
    [
        "YouTube Music", "YouTube", "Spotify", "Deezer", "SoundCloud", "Plex", "Tidal", "Apple Music", "Amazon Music",
        "Qobuz", "Bandcamp", "Mixcloud", "Twitch", "Netflix", "Disney+", "Prime Video", "Crunchyroll", "Jellyfin", "Emby",
        "Audible", "Napster", "Pandora", "Last.fm", "TuneIn", "radio.de", "Joyn", "ARD Mediathek", "ZDF", "Kick", "Vimeo",
    ];

    /// <summary>App-ID-Bestandteil → (Anzeigename, Prozessname des Browsers).</summary>
    private static readonly (string Key, string Name, string Process)[] Browsers =
    [
        ("MSEdge", "Edge", "msedge"),
        ("Chrome", "Chrome", "chrome"),
        ("Brave", "Brave", "brave"),
        ("Opera", "Opera", "opera"),
        ("Vivaldi", "Vivaldi", "vivaldi"),
        ("Firefox", "Firefox", "firefox"),
        ("308046B0AF4A39CB", "Firefox", "firefox"), // App-ID von Firefox (Hash des Installationspfads)
    ];

    /// <summary>Bekannte Store-Apps, deren App-ID wenig aussagt.</summary>
    private static readonly Dictionary<string, string> KnownApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ZuneMusic"] = "Medienwiedergabe",
        ["ZuneVideo"] = "Filme & TV",
        ["SpotifyMusic"] = "Spotify",
        ["AppleMusicWin"] = "Apple Music",
        ["AmazonMusic"] = "Amazon Music",
        ["DeezerMusic"] = "Deezer",
    };

    private static readonly Dictionary<string, (string Name, long Ticks)> Cache = [];

    /// <summary>Anzeigename der Quelle; Ergebnisse für Browser werden einige Sekunden zwischengespeichert.</summary>
    public static string Describe(string appId, string title)
    {
        var browser = FindBrowser(appId);
        if (browser is null) return AppName(appId);

        string key = appId + "\n" + title;
        long now = Environment.TickCount64;
        if (Cache.TryGetValue(key, out var cached) && now - cached.Ticks < 5000) return cached.Name;

        // Manche Seiten melden statt eines Titels nur ihren Namen (z. B. Plex) – dann ist der Titel der Dienst.
        string? service = Services.FirstOrDefault(s => string.Equals(s, title.Trim(), StringComparison.OrdinalIgnoreCase))
                          ?? (title.Length > 0 ? FindService(browser.Value.Process, title) : null);
        // Installierte Web-App (z. B. YouTube Music als App): Dienst ohne Browsernamen.
        bool webApp = appId.Contains("_crx_", StringComparison.OrdinalIgnoreCase);
        string name = service is null ? browser.Value.Name : webApp ? service : $"{service} · {browser.Value.Name}";
        if (Cache.Count > 50) Cache.Clear();
        Cache[key] = (name, now);
        return name;
    }

    private static (string Name, string Process)? FindBrowser(string appId)
    {
        foreach (var (k, name, process) in Browsers)
            if (appId.Contains(k, StringComparison.OrdinalIgnoreCase)) return (name, process);
        return null;
    }

    /// <summary>Lesbarer Name aus der App-ID: "Spotify.exe" → "Spotify", "Mozilla.Firefox_n80bbvh6b1yt2!App" → "Firefox".</summary>
    public static string AppName(string appId)
    {
        if (appId.Length == 0) return "Unbekannte App";
        if (FindBrowser(appId) is { } browser) return browser.Name;
        string name = appId;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
        int bang = name.IndexOf('!');
        if (bang > 0) name = name[..bang];
        int underscore = name.IndexOf('_');
        if (underscore > 0) name = name[..underscore];
        int dot = name.LastIndexOf('.');
        if (dot >= 0 && dot < name.Length - 1) name = name[(dot + 1)..];
        return KnownApps.TryGetValue(name, out var known) ? known : name;
    }

    /// <summary>Sichtbares Fenster des Browsers, dessen Titel den gespielten Titel enthält → bekannter Dienst darin.</summary>
    private static string? FindService(string process, string title)
    {
        string? found = null;
        var names = new Dictionary<uint, string?>();
        var buffer = new StringBuilder(512);
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            buffer.Clear();
            if (GetWindowText(hwnd, buffer, buffer.Capacity) <= 0) return true;
            string windowTitle = buffer.ToString();
            if (!windowTitle.Contains(title, StringComparison.CurrentCultureIgnoreCase)) return true;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (!names.TryGetValue(pid, out var processName))
            {
                try
                {
                    using var p = Process.GetProcessById((int)pid);
                    processName = p.ProcessName;
                }
                catch (Exception)
                {
                    processName = null;
                }
                names[pid] = processName;
            }
            if (!string.Equals(processName, process, StringComparison.OrdinalIgnoreCase)) return true;

            // Titel ohne den Liedtitel selbst durchsuchen – sonst würde ein Lied namens „Spotify“ zum Dienst.
            string rest = windowTitle.Replace(title, " ", StringComparison.CurrentCultureIgnoreCase);
            found = Services.FirstOrDefault(s => rest.Contains(s, StringComparison.OrdinalIgnoreCase));
            return found is null; // weitersuchen, falls ein anderes Fenster passt
        }, IntPtr.Zero);
        return found;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
