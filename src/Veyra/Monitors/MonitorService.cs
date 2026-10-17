using System.Runtime.InteropServices;
using Veyra.Interop;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Monitors;

/// <summary>
/// Monitor-Abfragen. Bewusst zustandslos: Monitor-Handles werden bei Konfigurationsänderungen
/// ungültig, daher wird bei jeder Änderung (WM_DISPLAYCHANGE, DPI, Entsperren) neu abgefragt.
/// Monitore werden stabil über den Gerätenamen identifiziert.
/// </summary>
public static class MonitorService
{
    public const string PrimarySelection = "Primary";

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var handles = new List<IntPtr>();
        MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref RECT bounds, IntPtr data) =>
        {
            handles.Add(hMonitor);
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        return handles
            .Select(Query)
            .OfType<MonitorInfo>()
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Bounds.Left)
            .ThenBy(m => m.Bounds.Top)
            .ToList();
    }

    /// <summary>Löst die Einstellung MonitorSelection auf; fällt bei unbekanntem/entferntem Monitor auf den primären zurück.</summary>
    public static MonitorInfo? Resolve(string? selection)
    {
        var monitors = GetMonitors();
        if (!string.IsNullOrWhiteSpace(selection) &&
            !selection.Equals(PrimarySelection, StringComparison.OrdinalIgnoreCase))
        {
            var match = monitors.FirstOrDefault(m => m.DeviceName.Equals(selection, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
    }

    public static MonitorInfo? Query(IntPtr hMonitor)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(hMonitor, ref info)) return null;

        uint dpi = 96;
        if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 && dpiX > 0)
            dpi = dpiX;

        int refreshRate = 0;
        var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (EnumDisplaySettings(info.szDevice, ENUM_CURRENT_SETTINGS, ref mode))
            refreshRate = (int)mode.dmDisplayFrequency;

        return new MonitorInfo(hMonitor, info.szDevice, info.rcMonitor, info.rcWork,
            (info.dwFlags & MONITORINFOF_PRIMARY) != 0, dpi, refreshRate);
    }
}
