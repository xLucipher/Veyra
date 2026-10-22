using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Veyra.Widgets.Launcher;

/// <summary>Symbole und Namen für Schnellstart-Einträge (Shell-Symbol der Datei bzw. des Ordners; Webadressen haben keins).</summary>
public static class LauncherIcons
{
    private const uint SHGFI_ICON = 0x100;
    private const uint SHGFI_LARGEICON = 0x0;
    private const uint SHGFI_SMALLICON = 0x1;

    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsWebAddress(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("www.", StringComparison.OrdinalIgnoreCase);

    /// <summary>Symbol in passender Größe; null für Webadressen und nicht vorhandene Dateien.</summary>
    public static ImageSource? Get(string path, double size)
    {
        if (string.IsNullOrWhiteSpace(path) || IsWebAddress(path)) return null;
        bool large = size > 16;
        string key = (large ? "L|" : "S|") + path;
        if (Cache.TryGetValue(key, out var cached)) return cached;

        ImageSource? image = null;
        try
        {
            string expanded = Environment.ExpandEnvironmentVariables(path);
            if (File.Exists(expanded) || Directory.Exists(expanded))
            {
                var info = new SHFILEINFO();
                if (SHGetFileInfoW(expanded, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON)) != IntPtr.Zero &&
                    info.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        source.Freeze();
                        image = source;
                    }
                    finally
                    {
                        DestroyIcon(info.hIcon);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or COMException)
        {
            image = null;
        }
        Cache[key] = image;
        return image;
    }

    /// <summary>Anzeigename ohne eigenen Namen: Ordnername, Dateiname ohne Endung bzw. Host der Webadresse.</summary>
    public static string DefaultName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (IsWebAddress(path))
        {
            string url = path.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + path : path;
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.Replace("www.", "", StringComparison.OrdinalIgnoreCase) : path;
        }
        string trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length <= 3 && trimmed.EndsWith(':')) return trimmed; // Laufwerk "D:"
        try
        {
            return Directory.Exists(trimmed) ? Path.GetFileName(trimmed) : Path.GetFileNameWithoutExtension(trimmed);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
