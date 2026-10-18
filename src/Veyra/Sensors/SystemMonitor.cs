using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using Veyra.Core;
using static Veyra.Sensors.SensorInterop;

namespace Veyra.Sensors;

public enum SensorKind
{
    Cpu,
    Ram,
    GpuUsage,
    GpuTemperature,
}

/// <summary>Eine Grafikkarte laut DXGI. Die LUID ändert sich bei jedem Neustart, der Name nicht.</summary>
public sealed record GpuInfo(string Name, uint LuidLow, int LuidHigh, ulong DedicatedMemory)
{
    /// <summary>LUID-Teil der Leistungsindikator-Instanznamen, z. B. "luid_0x00000000_0x00011470".</summary>
    public string CounterKey => $"luid_0x{LuidHigh:X8}_0x{LuidLow:X8}";
}

/// <summary>Messwerte einer Grafikkarte; null = nicht verfügbar.</summary>
public sealed record GpuReading(double? Usage, double? Temperature, ulong DedicatedUsed);

/// <summary>
/// Liest CPU, RAM, GPU-Auslastung und GPU-Temperatur einmal pro Sekunde – gemeinsam für alle Widgets.
/// Gemessen wird nur, was gerade ein laufendes Widget angefordert hat (<see cref="Acquire"/>); ohne Anforderung
/// steht der Timer, z. B. während die Leiste bei Vollbild ausgeblendet ist.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<SensorKind, int> _demand = [];

    private long _lastIdle, _lastKernel, _lastUser;
    private IntPtr _gpuQuery, _engineCounter, _memoryCounter;
    private bool _gpuQueryFailed;
    private readonly Dictionary<string, uint> _kmtAdapters = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<GpuInfo>? _gpus;

    public SystemMonitor()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Sample();
        CpuName = ReadCpuName();
    }

    /// <summary>Neue Messwerte liegen vor (UI-Thread).</summary>
    public event EventHandler? Updated;

    public string CpuName { get; }

    /// <summary>CPU-Auslastung 0–100; null bis zur zweiten Messung.</summary>
    public double? CpuUsage { get; private set; }

    public ulong RamTotal { get; private set; }
    public ulong RamUsed { get; private set; }
    public double? RamUsage => RamTotal == 0 ? null : 100.0 * RamUsed / RamTotal;

    /// <summary>Messwerte je Grafikkarte (Schlüssel: <see cref="GpuInfo.CounterKey"/>).</summary>
    public IReadOnlyDictionary<string, GpuReading> Gpu { get; private set; } = new Dictionary<string, GpuReading>();

    /// <summary>Grafikkarten ohne Software-Renderer. Wird einmal ermittelt (Neustart der App nach Hardwarewechsel).</summary>
    public IReadOnlyList<GpuInfo> Gpus => _gpus ??= EnumerateGpus();

    /// <summary>
    /// Grafikkarte zu einem gespeicherten Namen; leer/unbekannt = automatisch die mit dem meisten eigenen
    /// Videospeicher (in Systemen mit Prozessorgrafik und Grafikkarte also die Grafikkarte).
    /// </summary>
    public GpuInfo? ResolveGpu(string? name) =>
        Gpus.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Gpus.OrderByDescending(g => g.DedicatedMemory).FirstOrDefault();

    /// <summary>Messwert anfordern (Widget gestartet). Jeder Aufruf braucht ein passendes <see cref="Release"/>.</summary>
    public void Acquire(SensorKind kind)
    {
        _demand[kind] = _demand.GetValueOrDefault(kind) + 1;
        if (!_timer.IsEnabled)
        {
            Sample();
            _timer.Start();
        }
    }

    public void Release(SensorKind kind)
    {
        int count = _demand.GetValueOrDefault(kind) - 1;
        if (count > 0) _demand[kind] = count;
        else _demand.Remove(kind);

        if (_demand.Count == 0) _timer.Stop();
        if (!Needs(SensorKind.GpuUsage)) CloseGpuQuery();
    }

    public void Dispose()
    {
        _timer.Stop();
        CloseGpuQuery();
        foreach (uint handle in _kmtAdapters.Values)
        {
            var close = new D3DKMT_CLOSEADAPTER { hAdapter = handle };
            D3DKMTCloseAdapter(ref close);
        }
        _kmtAdapters.Clear();
    }

    private bool Needs(SensorKind kind) => _demand.ContainsKey(kind);

    private void Sample()
    {
        try
        {
            if (Needs(SensorKind.Cpu)) SampleCpu();
            if (Needs(SensorKind.Ram)) SampleRam();
            if (Needs(SensorKind.GpuUsage) || Needs(SensorKind.GpuTemperature)) SampleGpus();
        }
        catch (Exception ex)
        {
            Log.Error("Sensors", "Messung fehlgeschlagen", ex);
        }
        Updated?.Invoke(this, EventArgs.Empty);
    }

    // ---- CPU / RAM ------------------------------------------------------------------------

    private void SampleCpu()
    {
        if (!GetSystemTimes(out long idle, out long kernel, out long user)) return;
        long idleDelta = idle - _lastIdle, totalDelta = kernel - _lastKernel + (user - _lastUser); // Kernelzeit enthält die Leerlaufzeit
        bool first = _lastKernel == 0;
        (_lastIdle, _lastKernel, _lastUser) = (idle, kernel, user);
        if (first || totalDelta <= 0) return;
        CpuUsage = Math.Clamp(100.0 * (totalDelta - idleDelta) / totalDelta, 0, 100);
    }

    private void SampleRam()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return;
        RamTotal = status.ullTotalPhys;
        RamUsed = status.ullTotalPhys - status.ullAvailPhys;
    }

    private static string ReadCpuName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Prozessor";
        }
        catch
        {
            return "Prozessor";
        }
    }

    // ---- GPU ------------------------------------------------------------------------------

    private void SampleGpus()
    {
        Dictionary<string, double> usage = [];
        Dictionary<string, ulong> memory = [];

        if (Needs(SensorKind.GpuUsage) && EnsureGpuQuery() && PdhCollectQueryData(_gpuQuery) == 0)
        {
            // Wie der Task-Manager: je Grafikkarte die Auslastung jeder Engine-Art (3D, Copy, VideoDecode …)
            // über alle Prozesse summieren; angezeigt wird die am stärksten ausgelastete Engine-Art.
            var perType = new Dictionary<(string Luid, string Engine), double>();
            foreach (var (instance, value) in ReadCounterArray(_engineCounter))
            {
                if (ParseLuid(instance) is not { } luid) continue;
                int engIndex = instance.IndexOf("engtype_", StringComparison.Ordinal);
                string engine = engIndex >= 0 ? instance[(engIndex + 8)..] : "";
                perType[(luid, engine)] = perType.GetValueOrDefault((luid, engine)) + value;
            }
            foreach (var group in perType.GroupBy(p => p.Key.Luid, StringComparer.OrdinalIgnoreCase))
                usage[group.Key] = Math.Clamp(group.Max(p => p.Value), 0, 100);

            foreach (var (instance, value) in ReadCounterArray(_memoryCounter))
                if (ParseLuid(instance) is { } luid) memory[luid] = memory.GetValueOrDefault(luid) + (ulong)Math.Max(0, value);
        }

        var readings = new Dictionary<string, GpuReading>(StringComparer.OrdinalIgnoreCase);
        foreach (var gpu in Gpus)
        {
            string key = gpu.CounterKey;
            double? temperature = Needs(SensorKind.GpuTemperature) ? ReadTemperature(gpu) : null;
            readings[key] = new GpuReading(
                usage.TryGetValue(key, out var u) ? u : (Needs(SensorKind.GpuUsage) && _gpuQuery != IntPtr.Zero ? 0 : null),
                temperature,
                memory.GetValueOrDefault(key));
        }
        Gpu = readings;
    }

    private bool EnsureGpuQuery()
    {
        if (_gpuQuery != IntPtr.Zero) return true;
        if (_gpuQueryFailed) return false;

        if (PdhOpenQueryW(null, IntPtr.Zero, out _gpuQuery) != 0 ||
            PdhAddEnglishCounterW(_gpuQuery, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _engineCounter) != 0 ||
            PdhAddEnglishCounterW(_gpuQuery, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out _memoryCounter) != 0)
        {
            Log.Warning("Sensors", "GPU-Leistungsindikatoren nicht verfügbar – GPU-Auslastung wird nicht angezeigt");
            CloseGpuQuery();
            _gpuQueryFailed = true;
            return false;
        }
        PdhCollectQueryData(_gpuQuery); // Ratenwerte brauchen zwei Messungen
        return true;
    }

    private void CloseGpuQuery()
    {
        if (_gpuQuery == IntPtr.Zero) return;
        PdhCloseQuery(_gpuQuery);
        _gpuQuery = _engineCounter = _memoryCounter = IntPtr.Zero;
    }

    /// <summary>Alle Instanzen eines Platzhalter-Zählers als (Instanzname, Wert); ungültige Werte werden übersprungen.</summary>
    private static List<(string Instance, double Value)> ReadCounterArray(IntPtr counter)
    {
        var result = new List<(string, double)>();
        uint size = 0;
        int status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out _, IntPtr.Zero);
        if (status != PDH_MORE_DATA || size == 0) return result;

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out uint count, buffer) != 0) return result;
            int itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                if (item.CStatus is not (0 or 1)) continue; // PDH_CSTATUS_VALID_DATA / NEW_DATA
                string? name = Marshal.PtrToStringUni(item.szName);
                if (name is not null && double.IsFinite(item.doubleValue)) result.Add((name, item.doubleValue));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    /// <summary>"pid_12_luid_0x00000000_0x00011470_phys_0_eng_0_engtype_3D" → "luid_0x00000000_0x00011470".</summary>
    private static string? ParseLuid(string instance)
    {
        int start = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        return start >= 0 && instance.Length >= start + 26 ? instance.Substring(start, 26) : null;
    }

    private double? ReadTemperature(GpuInfo gpu)
    {
        if (!_kmtAdapters.TryGetValue(gpu.CounterKey, out uint handle))
        {
            var open = new D3DKMT_OPENADAPTERFROMLUID { LuidLowPart = gpu.LuidLow, LuidHighPart = gpu.LuidHigh };
            if (D3DKMTOpenAdapterFromLuid(ref open) != 0) return null;
            handle = open.hAdapter;
            _kmtAdapters[gpu.CounterKey] = handle;
        }

        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<D3DKMT_ADAPTER_PERFDATA>());
        try
        {
            Marshal.StructureToPtr(new D3DKMT_ADAPTER_PERFDATA(), buffer, false);
            var query = new D3DKMT_QUERYADAPTERINFO
            {
                hAdapter = handle,
                Type = KMTQAITYPE_ADAPTERPERFDATA,
                pPrivateDriverData = buffer,
                PrivateDriverDataSize = (uint)Marshal.SizeOf<D3DKMT_ADAPTER_PERFDATA>(),
            };
            if (D3DKMTQueryAdapterInfo(ref query) != 0) return null;
            var data = Marshal.PtrToStructure<D3DKMT_ADAPTER_PERFDATA>(buffer);
            // 0 = Treiber liefert keinen Wert (z. B. manche Prozessorgrafiken).
            return data.Temperature is > 0 and < 2000 ? data.Temperature / 10.0 : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>Grafikkarten ohne Software-Renderer, z. B. für die Auswahl im Einstellungsfenster.</summary>
    public static List<GpuInfo> EnumerateGpus()
    {
        var result = new List<GpuInfo>();
        try
        {
            var iid = IID_IDXGIFactory1;
            if (CreateDXGIFactory1(ref iid, out IntPtr factoryPtr) != 0) return result;
            var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(factoryPtr);
            Marshal.Release(factoryPtr);
            try
            {
                for (uint i = 0; factory.EnumAdapters1(i, out var adapter) == 0; i++)
                {
                    try
                    {
                        if (adapter.GetDesc1(out var desc) != 0 || (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0) continue;
                        // Dieselbe Karte kann mehrfach auftauchen (eine je Ausgang/Modus) – nach LUID eindeutig halten.
                        if (result.Any(g => g.LuidLow == desc.LuidLowPart && g.LuidHigh == desc.LuidHighPart)) continue;
                        result.Add(new GpuInfo(desc.Description.Trim(), desc.LuidLowPart, desc.LuidHighPart, desc.DedicatedVideoMemory));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
            Log.Info("Sensors", "Grafikkarten: " + string.Join(", ", result.Select(g => $"{g.Name} ({g.DedicatedMemory / 1048576} MB)")));
        }
        catch (Exception ex)
        {
            Log.Error("Sensors", "Grafikkarten konnten nicht ermittelt werden", ex);
        }
        return result;
    }
}
