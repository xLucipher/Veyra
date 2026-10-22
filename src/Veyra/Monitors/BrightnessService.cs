using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;
using Veyra.Core;

namespace Veyra.Monitors;

/// <summary>Ein Bildschirm mit regelbarer Helligkeit.</summary>
public sealed record ScreenBrightness(string Id, string Name, int Percent);

/// <summary>
/// Bildschirmhelligkeit für alle Helligkeits-Widgets. Externe Monitore über DDC/CI (dxva2: <c>GetMonitorBrightness</c>,
/// <c>SetMonitorBrightness</c>), der integrierte Bildschirm eines Laptops über <c>IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS</c>.
/// DDC/CI ist langsam (rund 50 ms je Befehl) – deshalb läuft alles auf einem eigenen Thread, und schnelle Änderungen
/// (Mausrad) werden zusammengefasst: Je Bildschirm wird nur der letzte Zielwert gesendet.
/// Monitore ohne DDC/CI (oder mit abgeschaltetem DDC/CI im Monitormenü) erscheinen nicht.
/// </summary>
public sealed class BrightnessService : IDisposable
{
    private readonly Dictionary<object, bool> _users = [];
    private readonly BlockingCollection<Action> _queue = [];
    private readonly ConcurrentDictionary<string, int> _pending = new();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly List<Screen> _screens = []; // nur auf dem Arbeits-Thread
    private Thread? _worker;
    private IReadOnlyList<ScreenBrightness> _current = [];

    private sealed class Screen
    {
        public required string Id;
        public required string Name; // bei gleichen Monitoren durchnummeriert
        public IntPtr Physical;
        public bool Internal;
        public uint Min;
        public uint Max;
        public int Percent;
    }

    /// <summary>Stand aller Bildschirme (UI-Thread).</summary>
    public IReadOnlyList<ScreenBrightness> Screens => _current;

    /// <summary>Bildschirme wurden mindestens einmal gelesen.</summary>
    public bool HasRead { get; private set; }

    public event EventHandler? Changed;

    public void Acquire(object owner)
    {
        bool first = _users.Count == 0;
        _users[owner] = true;
        if (!first) return;
        _worker ??= StartWorker();
        _queue.Add(Enumerate);
    }

    public void Release(object owner)
    {
        if (_users.Remove(owner) && _users.Count == 0 && _worker is not null) _queue.Add(CloseScreens);
    }

    /// <summary>Monitore neu einlesen (z. B. nach Anschließen eines Monitors).</summary>
    public void Rescan()
    {
        if (_worker is not null && _users.Count > 0) _queue.Add(Enumerate);
    }

    public void Set(string id, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        _pending[id] = percent;
        // Sofort anzeigen, auch wenn der Monitor noch etwas braucht.
        _current = _current.Select(s => s.Id == id ? s with { Percent = percent } : s).ToList();
        Changed?.Invoke(this, EventArgs.Empty);
        _queue.Add(Flush);
    }

    /// <summary>Alle Bildschirme um <paramref name="delta"/> Prozentpunkte.</summary>
    public void Step(int delta)
    {
        foreach (var screen in _current) Set(screen.Id, screen.Percent + delta);
    }

    private Thread StartWorker()
    {
        var thread = new Thread(() =>
        {
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    Log.Warning("Brightness", $"Helligkeit: {ex.Message}");
                }
            }
            CloseScreens();
        })
        { IsBackground = true, Name = "Veyra-Helligkeit" };
        thread.Start();
        return thread;
    }

    private void Enumerate()
    {
        CloseScreens();
        var names = DisplayNames.Query().ToDictionary(t => t.GdiName, StringComparer.OrdinalIgnoreCase);
        foreach (var monitor in MonitorService.GetMonitors())
        {
            var target = names.GetValueOrDefault(monitor.DeviceName);
            if (target?.IsInternal == true)
            {
                if (InternalPanel.TryGet(out int level))
                    _screens.Add(new Screen { Id = monitor.DeviceName, Name = target.FriendlyName, Internal = true, Min = 0, Max = 100, Percent = level });
                continue;
            }
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor.Handle, out uint count) || count == 0) continue;
            var physical = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitor.Handle, count, physical)) continue;
            for (int i = 0; i < physical.Length; i++)
            {
                var handle = physical[i].hPhysicalMonitor;
                if (!GetMonitorBrightness(handle, out uint min, out uint current, out uint max) || max <= min)
                {
                    DestroyPhysicalMonitor(handle); // kein DDC/CI
                    continue;
                }
                string name = target?.FriendlyName ?? physical[i].szPhysicalMonitorDescription;
                _screens.Add(new Screen
                {
                    Id = count > 1 ? $"{monitor.DeviceName}#{i}" : monitor.DeviceName,
                    Name = name,
                    Physical = handle,
                    Min = min,
                    Max = max,
                    Percent = (int)Math.Round((current - min) * 100.0 / (max - min)),
                });
            }
        }
        // Gleiche Monitore nebeneinander („27G15N“, „27G15N“) unterscheidbar machen.
        foreach (var group in _screens.GroupBy(s => s.Name).Where(g => g.Count() > 1))
        {
            int number = 1;
            foreach (var screen in group) screen.Name = $"{screen.Name} ({number++})";
        }
        Log.Info("Brightness", _screens.Count == 0
            ? "Kein Bildschirm mit regelbarer Helligkeit (DDC/CI) gefunden"
            : "Helligkeit regelbar: " + string.Join(", ", _screens.Select(s => $"{s.Name} {s.Percent} %")));
        Publish();
    }

    private void Flush()
    {
        foreach (var screen in _screens)
        {
            if (!_pending.TryRemove(screen.Id, out int percent) || percent == screen.Percent) continue;
            bool ok = screen.Internal
                ? InternalPanel.TrySet(percent)
                : SetMonitorBrightness(screen.Physical, (uint)Math.Round(screen.Min + percent / 100.0 * (screen.Max - screen.Min)));
            if (ok) screen.Percent = percent;
            else Log.Debug("Brightness", $"{screen.Name}: Helligkeit {percent} % nicht übernommen");
        }
        Publish();
    }

    private void Publish()
    {
        var list = _screens.Select(s => new ScreenBrightness(s.Id, s.Name, s.Percent)).ToList();
        _dispatcher.BeginInvoke(() =>
        {
            // Noch ausstehende Änderungen nicht mit alten Werten überschreiben.
            _current = list.Select(s => _pending.TryGetValue(s.Id, out int p) ? s with { Percent = p } : s).ToList();
            HasRead = true;
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    private void CloseScreens()
    {
        foreach (var screen in _screens.Where(s => s.Physical != IntPtr.Zero)) DestroyPhysicalMonitor(screen.Physical);
        _screens.Clear();
    }

    public void Dispose()
    {
        _users.Clear();
        _queue.CompleteAdding();
        NightLight.Restore();
    }

    /// <summary>Integrierter Laptop-Bildschirm über den Videotreiber (\\.\LCD); nicht jeder Treiber lässt das zu.</summary>
    private static class InternalPanel
    {
        private const uint IOCTL_VIDEO_QUERY_DISPLAY_BRIGHTNESS = 0x00230498;
        private const uint IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS = 0x0023049C;
        private const byte DISPLAYPOLICY_BOTH = 3;

        public static bool TryGet(out int level)
        {
            level = 0;
            using var handle = Open();
            if (handle.IsInvalid) return false;
            var value = new byte[3];
            if (!DeviceIoControl(handle, IOCTL_VIDEO_QUERY_DISPLAY_BRIGHTNESS, null, 0, value, 3, out _, IntPtr.Zero)) return false;
            bool onBattery = System.Windows.SystemParameters.PowerLineStatus == System.Windows.PowerLineStatus.Offline;
            level = onBattery ? value[2] : value[1];
            return true;
        }

        public static bool TrySet(int level)
        {
            using var handle = Open();
            if (handle.IsInvalid) return false;
            var value = new[] { DISPLAYPOLICY_BOTH, (byte)level, (byte)level };
            return DeviceIoControl(handle, IOCTL_VIDEO_SET_DISPLAY_BRIGHTNESS, value, 3, null, 0, out _, IntPtr.Zero);
        }

        private static SafeFileHandle Open() =>
            CreateFileW(@"\\.\LCD", 0xC0000000 /* GENERIC_READ | GENERIC_WRITE */, 3, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? input, uint inputSize, byte[]? output, uint outputSize,
            out uint returned, IntPtr overlapped);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitor(IntPtr monitor);
}
