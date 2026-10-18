using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Veyra.Core;
using static Veyra.Network.NetworkNative;

namespace Veyra.Network;

/// <summary>Byte-Zähler einer Schnittstelle seit dem Systemstart (bzw. seit dem Aktivieren des Adapters).</summary>
public readonly record struct TrafficCounters(ulong Received, ulong Sent);

/// <summary>Ein Netzwerkadapter zur Auswahl im Einstellungsfenster.</summary>
public sealed record NetworkAdapterInfo(string Id, string Name, string Description, bool IsUp);

/// <summary>
/// Liest die Byte-Zähler aller Netzwerkschnittstellen (GetIfTable2, ein Systemaufruf je Messung) – gemeinsam für
/// alle Upload-/Download-Widgets. Gemessen wird im kürzesten angeforderten Intervall und nur, solange mindestens
/// ein laufendes Widget angemeldet ist (<see cref="Acquire"/>); die Raten berechnet jedes Widget selbst aus
/// zwei Messungen in seinem eigenen Intervall.
/// </summary>
public sealed class NetworkTrafficMonitor : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly List<int> _intervals = [];
    private Dictionary<Guid, TrafficCounters> _counters = [];
    private TrafficCounters _total;

    public NetworkTrafficMonitor()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += (_, _) => Sample();
    }

    /// <summary>Neue Messung liegt vor (UI-Thread).</summary>
    public event EventHandler? Sampled;

    /// <summary>Zeitpunkt der letzten Messung (<see cref="Stopwatch.GetTimestamp"/>).</summary>
    public long Timestamp { get; private set; }

    /// <param name="intervalMs">Gewünschtes Aktualisierungsintervall des Widgets.</param>
    public void Acquire(int intervalMs)
    {
        _intervals.Add(intervalMs);
        UpdateTimer();
        if (_intervals.Count == 1) Sample();
    }

    public void Release(int intervalMs)
    {
        _intervals.Remove(intervalMs);
        UpdateTimer();
    }

    public void Dispose() => _timer.Stop();

    /// <summary>
    /// Zähler eines Adapters (GUID wie <see cref="NetworkInterface.Id"/>) oder – bei leerer Auswahl – die Summe aller
    /// aktiven physischen Adapter. null = Adapter nicht vorhanden bzw. nicht verbunden.
    /// </summary>
    public TrafficCounters? Get(string? adapterId)
    {
        if (string.IsNullOrEmpty(adapterId)) return _total;
        return Guid.TryParse(adapterId, out var guid) && _counters.TryGetValue(guid, out var counters) ? counters : null;
    }

    /// <summary>
    /// Adapter für die Auswahl im Einstellungsfenster: physische Adapter (auch getrennte) sowie verbundene virtuelle
    /// Adapter mit IP-Adresse (z. B. VPN). Filter-Schnittstellen, WAN-Miniports, Loopback und Tunnel fehlen.
    /// </summary>
    public static List<NetworkAdapterInfo> EnumerateAdapters()
    {
        try
        {
            var flags = ReadInterfaceFlags();
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .Where(n =>
                {
                    if (!Guid.TryParse(n.Id, out var guid) || !flags.TryGetValue(guid, out var f)) return false;
                    if (f.Filter) return false;
                    return f.Hardware || (n.OperationalStatus == OperationalStatus.Up && n.GetIPProperties().UnicastAddresses.Count > 0);
                })
                .OrderByDescending(n => n.OperationalStatus == OperationalStatus.Up)
                .ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(n => new NetworkAdapterInfo(n.Id, n.Name, n.Description, n.OperationalStatus == OperationalStatus.Up))
                .ToList();
        }
        catch (NetworkInformationException ex)
        {
            Log.Warning("Network", $"Netzwerkadapter nicht lesbar: {ex.Message}");
            return [];
        }
    }

    private void UpdateTimer()
    {
        if (_intervals.Count == 0)
        {
            _timer.Stop();
            return;
        }
        var interval = TimeSpan.FromMilliseconds(_intervals.Min());
        if (_timer.Interval != interval) _timer.Interval = interval;
        if (!_timer.IsEnabled) _timer.Start();
    }

    private void Sample()
    {
        try
        {
            if (GetIfTable2(out IntPtr table) != 0 || table == IntPtr.Zero) return;
            try
            {
                int count = Marshal.ReadInt32(table);
                var counters = new Dictionary<Guid, TrafficCounters>(count);
                ulong totalIn = 0, totalOut = 0;
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = table + IfTableRowsOffset + i * IfRowSize;
                    if (Marshal.ReadInt32(row, IfRowOperStatus) != IfOperStatusUp) continue;
                    var guid = ReadGuid(row + IfRowGuid);
                    var value = new TrafficCounters((ulong)Marshal.ReadInt64(row, IfRowInOctets), (ulong)Marshal.ReadInt64(row, IfRowOutOctets));
                    // Filter-Schnittstellen (z. B. WFP-/QoS-Filter) spiegeln den Adapter darunter – nicht doppelt zählen.
                    byte flags = Marshal.ReadByte(row, IfRowFlags);
                    bool hardware = (flags & 0x1) != 0, filter = (flags & 0x2) != 0;
                    if (filter) continue;
                    counters[guid] = value; // je GUID gibt es nur eine Nicht-Filter-Schnittstelle
                    if (hardware && Marshal.ReadInt32(row, IfRowType) != IfTypeLoopback)
                    {
                        totalIn += value.Received;
                        totalOut += value.Sent;
                    }
                }
                _counters = counters;
                _total = new TrafficCounters(totalIn, totalOut);
                Timestamp = Stopwatch.GetTimestamp();
            }
            finally
            {
                FreeMibTable(table);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Network", "Netzwerkzähler konnten nicht gelesen werden", ex);
            return;
        }
        Sampled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Hardware- und Filter-Kennzeichen aller Schnittstellen (nach GUID).</summary>
    private static Dictionary<Guid, (bool Hardware, bool Filter)> ReadInterfaceFlags()
    {
        var result = new Dictionary<Guid, (bool, bool)>();
        if (GetIfTable2(out IntPtr table) != 0 || table == IntPtr.Zero) return result;
        try
        {
            int count = Marshal.ReadInt32(table);
            for (int i = 0; i < count; i++)
            {
                IntPtr row = table + IfTableRowsOffset + i * IfRowSize;
                byte flags = Marshal.ReadByte(row, IfRowFlags);
                result[ReadGuid(row + IfRowGuid)] = ((flags & 0x1) != 0, (flags & 0x2) != 0);
            }
        }
        finally
        {
            FreeMibTable(table);
        }
        return result;
    }

    private static Guid ReadGuid(IntPtr pointer)
    {
        var bytes = new byte[16];
        Marshal.Copy(pointer, bytes, 0, 16);
        return new Guid(bytes);
    }
}
