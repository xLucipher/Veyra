using System.Runtime.InteropServices;
using Veyra.Core;

namespace Veyra.Monitors;

/// <summary>
/// Nachtmodus über die Gamma-Tabelle jedes Monitors (<c>SetDeviceGammaRamp</c>): Blau und Grün werden nach der
/// Farbtemperatur abgesenkt. Windows lehnt zu starke Änderungen ab – dann wird die Temperatur schrittweise angehoben,
/// bis die Tabelle angenommen wird. Die ursprüngliche Tabelle wird beim ersten Setzen gemerkt und beim Ausschalten,
/// Beenden und im Absturzpfad zurückgeschrieben. HDR-Monitore ignorieren die Tabelle.
/// </summary>
public static class NightLight
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, ushort[]> Originals = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<object, int> Requests = [];

    /// <summary>
    /// Ein Widget wünscht den Nachtmodus mit <paramref name="kelvin"/> bzw. keinen (null). Mehrere Widgets: Es gilt der
    /// wärmste Wunsch; wünscht keines mehr den Nachtmodus, wird die ursprüngliche Tabelle zurückgeschrieben.
    /// </summary>
    public static void Request(object owner, int? kelvin)
    {
        lock (Sync)
        {
            if (kelvin is { } k) Requests[owner] = k;
            else Requests.Remove(owner);
            if (Requests.Count == 0) Restore();
            else if (ActiveTemperature != Requests.Values.Min()) Apply(Requests.Values.Min());
        }
    }

    /// <summary>
    /// Windows setzt die Tabelle bei Anzeigeänderungen, nach dem Sperrbildschirm oder UAC-Abfragen gelegentlich zurück
    /// → regelmäßig bzw. nach solchen Ereignissen erneut setzen.
    /// </summary>
    public static void Reapply()
    {
        lock (Sync)
        {
            if (ActiveTemperature is { } kelvin) Apply(kelvin);
        }
    }

    /// <summary>Zuletzt gesetzte Temperatur; null = aus.</summary>
    public static int? ActiveTemperature { get; private set; }

    /// <summary>Tatsächlich erreichte Temperatur (kann wärmer gewünscht gewesen sein, als Windows zulässt).</summary>
    public static int? EffectiveTemperature { get; private set; }

    public static void Apply(int kelvin)
    {
        lock (Sync)
        {
            int? reached = null;
            foreach (var monitor in MonitorService.GetMonitors())
            {
                for (int k = kelvin; k <= 6500; k += 200)
                {
                    if (!SetRamp(monitor.DeviceName, Ramp(k))) continue;
                    reached = reached is null ? k : Math.Max(reached.Value, k);
                    break;
                }
            }
            if (ActiveTemperature != kelvin || EffectiveTemperature != reached)
                Log.Info("NightLight", reached is { } r ? $"Nachtmodus {kelvin} K{(r != kelvin ? $" (Windows lässt höchstens {r} K zu)" : "")}" : "Nachtmodus: Gamma-Tabelle abgelehnt");
            ActiveTemperature = kelvin;
            EffectiveTemperature = reached;
        }
    }

    /// <summary>Ursprüngliche Tabellen zurückschreiben. Auch aus dem Absturz-Handler (beliebiger Thread) aufrufbar.</summary>
    public static void Restore()
    {
        lock (Sync)
        {
            if (ActiveTemperature is null) return;
            foreach (var monitor in MonitorService.GetMonitors())
                SetRamp(monitor.DeviceName, Originals.TryGetValue(monitor.DeviceName, out var original) ? original : Ramp(6500));
            ActiveTemperature = null;
            EffectiveTemperature = null;
            Log.Info("NightLight", "Nachtmodus aus");
        }
    }

    private static bool SetRamp(string deviceName, ushort[] ramp)
    {
        var hdc = CreateDCW("DISPLAY", deviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;
        try
        {
            if (!Originals.ContainsKey(deviceName))
            {
                var original = new ushort[768];
                // Eine schon verfärbte Tabelle (z. B. nach einem Absturz) nicht als „ursprünglich“ merken.
                Originals[deviceName] = GetDeviceGammaRamp(hdc, original) && IsNeutral(original) ? original : Ramp(6500);
            }
            return SetDeviceGammaRamp(hdc, ramp);
        }
        finally
        {
            DeleteDC(hdc);
        }
    }

    /// <summary>Kanäle annähernd gleich und bis fast ganz oben – also keine Farbverschiebung.</summary>
    private static bool IsNeutral(ushort[] ramp)
    {
        for (int i = 0; i < 256; i += 15)
        {
            int r = ramp[i], g = ramp[256 + i], b = ramp[512 + i];
            if (Math.Abs(r - g) > 2500 || Math.Abs(r - b) > 2500) return false;
        }
        return ramp[255] > 60000 && ramp[511] > 60000 && ramp[767] > 60000;
    }

    /// <summary>Gamma-Tabelle (je 256 Werte Rot, Grün, Blau) für eine Farbtemperatur; 6500 K = unverändert.</summary>
    private static ushort[] Ramp(int kelvin)
    {
        var (r, g, b) = Multipliers(kelvin);
        var ramp = new ushort[768];
        for (int i = 0; i < 256; i++)
        {
            double value = i * 257.0;
            ramp[i] = (ushort)Math.Min(65535, value * r);
            ramp[256 + i] = (ushort)Math.Min(65535, value * g);
            ramp[512 + i] = (ushort)Math.Min(65535, value * b);
        }
        return ramp;
    }

    /// <summary>Annäherung der Schwarzkörperfarbe (Tanner Helland), bezogen auf 6500 K = (1, 1, 1).</summary>
    private static (double R, double G, double B) Multipliers(int kelvin)
    {
        static (double, double, double) Rgb(double k)
        {
            double t = k / 100.0;
            double red = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
            double green = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
            double blue = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            return (Math.Clamp(red, 0, 255), Math.Clamp(green, 0, 255), Math.Clamp(blue, 0, 255));
        }
        var (r, g, b) = Rgb(kelvin);
        var (r0, g0, b0) = Rgb(6500);
        return (Math.Min(1, r / r0), Math.Min(1, g / g0), Math.Min(1, b / b0));
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDCW(string driver, string device, string? output, IntPtr initData);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDeviceGammaRamp(IntPtr hdc, [Out] ushort[] ramp);
}
