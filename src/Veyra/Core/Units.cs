using System.Globalization;
using System.Windows.Media;

namespace Veyra.Core;

/// <summary>Einheiten für Datenraten und gemeinsame Signalfarben der Widgets.</summary>
public static class Units
{
    private static readonly string[] ByteUnits = ["B/s", "KB/s", "MB/s", "GB/s", "TB/s"];
    private static readonly string[] BitUnits = ["bit/s", "kbit/s", "Mbit/s", "Gbit/s", "Tbit/s"];

    public static readonly Brush WarnBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF6, 0x74, 0x00)));
    public static readonly Brush CriticalBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xDA, 0x44, 0x53)));

    /// <summary>
    /// Rate mit passender Einheit und drei gültigen Stellen: "512 KB/s", "12,3 MB/s", "1,05 GB/s".
    /// Bytes in 1024er-Schritten (wie der Explorer), Bits in 1000er-Schritten (wie Netzwerkgeräte).
    /// </summary>
    public static string FormatRate(double bytesPerSecond, bool bits)
    {
        double value = Math.Max(0, bits ? bytesPerSecond * 8 : bytesPerSecond);
        double step = bits ? 1000 : 1024;
        var units = bits ? BitUnits : ByteUnits;
        int unit = 0;
        while (value >= 999.5 && unit < units.Length - 1)
        {
            value /= step;
            unit++;
        }
        string format = unit == 0 || value >= 99.95 ? "0" : value >= 9.995 ? "0.0" : "0.00";
        return value.ToString(format, CultureInfo.CurrentCulture) + " " + units[unit];
    }

    /// <summary>Verbindungsgeschwindigkeit in Bit/s: "1 Gbit/s", "866,7 Mbit/s".</summary>
    public static string FormatLinkSpeed(double bitsPerSecond)
    {
        if (bitsPerSecond <= 0) return "–";
        string[] units = ["bit/s", "kbit/s", "Mbit/s", "Gbit/s", "Tbit/s"];
        int unit = 0;
        while (bitsPerSecond >= 1000 && unit < units.Length - 1)
        {
            bitsPerSecond /= 1000;
            unit++;
        }
        return bitsPerSecond.ToString(bitsPerSecond % 1 == 0 ? "0" : "0.#", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    /// <summary>Datenmenge: "1,2 GB".</summary>
    public static string FormatBytes(double bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        int unit = 0;
        while (bytes >= 1024 && unit < units.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        return bytes.ToString(unit == 0 ? "0" : "0.0", CultureInfo.CurrentCulture) + " " + units[unit];
    }

    public static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
