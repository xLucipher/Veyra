using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using Veyra.Core;
using static Veyra.Network.NetworkNative;

namespace Veyra.Network;

public enum ConnectionKind
{
    None,
    Ethernet,
    Wifi,
    Other,
}

public enum Connectivity
{
    Unknown,
    None,
    LocalOnly,
    Internet,
    Constrained,
}

/// <summary>Aktuelle Verbindung (die Schnittstelle der Standardroute) mit allem, was das Popup anzeigt.</summary>
public sealed record NetworkStatus
{
    public static readonly NetworkStatus Disconnected = new();

    public ConnectionKind Kind { get; init; }
    public Connectivity Connectivity { get; init; }

    /// <summary>SSID (WLAN) bzw. Name des Netzwerks laut Windows-Netzwerkliste; leer = unbekannt.</summary>
    public string NetworkName { get; init; } = "";

    /// <summary>WLAN-Signalqualität 0–100; null = unbekannt bzw. kein WLAN.</summary>
    public int? Signal { get; init; }

    /// <summary>Windows verweigert WLAN-Details, solange Desktop-Apps keinen Standortzugriff haben (ab Windows 11 24H2).</summary>
    public bool WifiDetailsRestricted { get; init; }

    public string AdapterAlias { get; init; } = "";
    public string AdapterDescription { get; init; } = "";
    public string AdapterId { get; init; } = "";
    public string MacAddress { get; init; } = "";

    /// <summary>Verbindungsgeschwindigkeit laut Adapter in Bit/s.</summary>
    public long LinkSpeed { get; init; }

    /// <summary>Nur WLAN: Empfangs-/Senderate in kbit/s und Standard ("Wi-Fi 6 (802.11ax)").</summary>
    public long WifiRxRate { get; init; }
    public long WifiTxRate { get; init; }
    public string WifiStandard { get; init; } = "";
    public int? WifiChannel { get; init; }
    public bool? WifiSecured { get; init; }

    public IReadOnlyList<string> IPv4 { get; init; } = [];
    public IReadOnlyList<string> IPv6 { get; init; } = [];
    public IReadOnlyList<string> Gateways { get; init; } = [];
    public IReadOnlyList<string> DnsServers { get; init; } = [];

    /// <summary>Netzwerkprofil laut Windows: "Öffentlich", "Privat", "Domäne"; leer = unbekannt.</summary>
    public string Category { get; init; } = "";

    /// <summary>Getaktete Verbindung (Kosten pro Datenmenge).</summary>
    public bool Metered { get; init; }

    public bool IsConnected => Kind != ConnectionKind.None;
}

/// <summary>
/// Überwacht die aktuelle Netzwerkverbindung – gemeinsam für alle Netzwerk-Widgets und rein ereignisbasiert:
/// Adressänderungen (NotifyAddrChange über <see cref="NetworkChange"/>), Konnektivitätswechsel
/// (NotifyNetworkConnectivityHintChange) und WLAN-Ereignisse (WlanRegisterNotification, u. a. Signalstärke).
/// Ohne laufendes Widget (<see cref="Acquire"/>) ist nichts angemeldet. Alle öffentlichen Mitglieder: UI-Thread.
/// </summary>
public sealed class NetworkStatusService : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _refreshDebounce;
    private int _demand;

    private IntPtr _hintHandle;
    private ConnectivityHintCallback? _hintCallback; // Verweis halten, solange Windows ihn aufrufen kann
    private NL_NETWORK_CONNECTIVITY_HINT _hint;
    private bool _hintAvailable = true;

    private IntPtr _wlan;
    private WlanNotificationCallback? _wlanCallback;
    private bool _wlanUnavailable;

    public NetworkStatusService()
    {
        // Ein Verbindungswechsel erzeugt eine Salve von Adress- und WLAN-Ereignissen → zusammenfassen.
        _refreshDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _refreshDebounce.Tick += (_, _) =>
        {
            _refreshDebounce.Stop();
            Refresh();
        };
    }

    /// <summary>Status hat sich geändert (UI-Thread).</summary>
    public event EventHandler? Changed;

    public NetworkStatus Status { get; private set; } = NetworkStatus.Disconnected;

    public void Acquire()
    {
        if (++_demand > 1) return;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        RegisterConnectivityHint();
        RegisterWlan();
        Refresh();
    }

    public void Release()
    {
        if (_demand == 0 || --_demand > 0) return;
        Unregister();
    }

    public void Dispose()
    {
        _demand = 0;
        Unregister();
    }

    /// <summary>Sofort neu einlesen (z. B. beim Öffnen des Popups, damit IP-Adressen und Geschwindigkeit aktuell sind).</summary>
    public void Refresh()
    {
        NetworkStatus status;
        try
        {
            status = Query();
        }
        catch (Exception ex)
        {
            Log.Error("Network", "Netzwerkstatus konnte nicht gelesen werden", ex);
            status = NetworkStatus.Disconnected;
        }
        if (Signature(status) == Signature(Status)) return;
        bool connectionChanged = status.Kind != Status.Kind || status.NetworkName != Status.NetworkName || status.AdapterId != Status.AdapterId;
        Status = status;
        if (connectionChanged)
            Log.Info("Network", status.IsConnected
                ? $"Verbindung: {status.Kind} „{status.NetworkName}“ über {status.AdapterDescription} ({status.Connectivity})"
                : "Keine Netzwerkverbindung");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Unregister()
    {
        _refreshDebounce.Stop();
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        if (_hintHandle != IntPtr.Zero)
        {
            CancelMibChangeNotify2(_hintHandle);
            _hintHandle = IntPtr.Zero;
        }
        _hintCallback = null;
        if (_wlan != IntPtr.Zero)
        {
            WlanRegisterNotification(_wlan, WLAN_NOTIFICATION_SOURCE_NONE, true, null, IntPtr.Zero, IntPtr.Zero, out _);
            WlanCloseHandle(_wlan, IntPtr.Zero);
            _wlan = IntPtr.Zero;
        }
        _wlanCallback = null;
    }

    private void ScheduleRefresh()
    {
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    }

    // Ereignisse kommen auf Threadpool- bzw. Systemthreads an → nur auf den UI-Thread weiterreichen.
    private void OnAddressChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(ScheduleRefresh);
    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => _dispatcher.BeginInvoke(ScheduleRefresh);

    private void RegisterConnectivityHint()
    {
        if (!_hintAvailable) return;
        try
        {
            if (GetNetworkConnectivityHint(out var hint) == 0) _hint = hint;
            _hintCallback = (_, changed) => _dispatcher.BeginInvoke(() =>
            {
                _hint = changed;
                ScheduleRefresh();
            });
            int result = NotifyNetworkConnectivityHintChange(_hintCallback, IntPtr.Zero, false, out _hintHandle);
            if (result != 0) Log.Warning("Network", $"Konnektivitätsbenachrichtigung nicht verfügbar ({result})");
        }
        catch (EntryPointNotFoundException)
        {
            // Erst ab Windows 10 2004 – dann gibt es nur die Adressänderungen.
            _hintAvailable = false;
            _hintCallback = null;
        }
    }

    private void RegisterWlan()
    {
        if (_wlanUnavailable) return;
        try
        {
            int result = WlanOpenHandle(2, IntPtr.Zero, out _, out _wlan);
            if (result != 0)
            {
                // Kein WLAN-Dienst (z. B. Rechner ohne WLAN-Adapter) – kein Fehler.
                _wlanUnavailable = true;
                _wlan = IntPtr.Zero;
                return;
            }
            _wlanCallback = OnWlanNotification;
            result = WlanRegisterNotification(_wlan, WLAN_NOTIFICATION_SOURCE_ACM | WLAN_NOTIFICATION_SOURCE_MSM, true,
                _wlanCallback, IntPtr.Zero, IntPtr.Zero, out _);
            if (result != 0) Log.Warning("Network", $"WLAN-Benachrichtigungen nicht verfügbar ({result})");
        }
        catch (DllNotFoundException)
        {
            _wlanUnavailable = true;
            _wlan = IntPtr.Zero;
        }
    }

    /// <summary>WLAN-Ereignis (Systemthread): Verbindungswechsel → neu einlesen, Signaländerung → nur das Signal übernehmen.</summary>
    private void OnWlanNotification(IntPtr data, IntPtr context)
    {
        if (data == IntPtr.Zero) return;
        uint source = (uint)Marshal.ReadInt32(data);
        int code = Marshal.ReadInt32(data, NotificationCode);

        if (source == WLAN_NOTIFICATION_SOURCE_MSM && code == MsmSignalQualityChange)
        {
            int size = Marshal.ReadInt32(data, NotificationDataSize);
            IntPtr payload = Marshal.ReadIntPtr(data, NotificationData);
            if (size < 4 || payload == IntPtr.Zero) return;
            int quality = Math.Clamp(Marshal.ReadInt32(payload), 0, 100);
            var guid = ReadGuid(data + NotificationGuid);
            _dispatcher.BeginInvoke(() => OnSignalChanged(guid, quality));
            return;
        }

        bool relevant = source == WLAN_NOTIFICATION_SOURCE_ACM
            ? code is AcmConnectionComplete or AcmDisconnected or AcmInterfaceArrival or AcmInterfaceRemoval
            : code is MsmConnected or MsmRoamingEnd or MsmDisconnected;
        if (relevant) _dispatcher.BeginInvoke(ScheduleRefresh);
    }

    private void OnSignalChanged(Guid adapter, int quality)
    {
        if (Status.Kind != ConnectionKind.Wifi || !Guid.TryParse(Status.AdapterId, out var current) || current != adapter) return;
        if (Status.Signal == quality) return;
        Status = Status with { Signal = quality };
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ---- Abfrage ----------------------------------------------------------------------------

    private NetworkStatus Query()
    {
        var adapter = FindPrimaryInterface();
        if (adapter is null) return NetworkStatus.Disconnected;

        var properties = adapter.GetIPProperties();
        var kind = adapter.NetworkInterfaceType switch
        {
            NetworkInterfaceType.Wireless80211 => ConnectionKind.Wifi,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => ConnectionKind.Ethernet,
            _ => ConnectionKind.Other,
        };

        var addresses = properties.UnicastAddresses.Select(a => a.Address).ToList();
        string mac = string.Join(":", adapter.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
        var (nlmName, category) = ReadNetworkListInfo(adapter.Id);

        var status = new NetworkStatus
        {
            Kind = kind,
            Connectivity = ReadConnectivity(),
            NetworkName = nlmName,
            AdapterAlias = adapter.Name,
            AdapterDescription = adapter.Description,
            AdapterId = adapter.Id,
            MacAddress = mac,
            LinkSpeed = Math.Max(0, adapter.Speed),
            IPv4 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToList(),
            // Globale Adressen zuerst, verbindungslokale (fe80::) zuletzt.
            IPv6 = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6)
                .OrderBy(a => a.IsIPv6LinkLocal).Select(a => a.ToString()).ToList(),
            Gateways = properties.GatewayAddresses.Select(g => g.Address).Where(a => !a.Equals(System.Net.IPAddress.Any))
                .Select(a => a.ToString()).Distinct().ToList(),
            DnsServers = properties.DnsAddresses.Select(a => a.ToString()).Distinct().ToList(),
            Category = category,
            Metered = _hintAvailable && _hint.ConnectivityCost is ConnectivityCostFixed or ConnectivityCostVariable,
        };

        return kind == ConnectionKind.Wifi ? AddWifiDetails(status) : status;
    }

    private Connectivity ReadConnectivity()
    {
        if (!_hintAvailable) return Connectivity.Unknown;
        return _hint.ConnectivityLevel switch
        {
            ConnectivityLevelNone => Connectivity.None,
            ConnectivityLevelLocal => Connectivity.LocalOnly,
            ConnectivityLevelInternet => Connectivity.Internet,
            ConnectivityLevelConstrained => Connectivity.Constrained,
            _ => Connectivity.Unknown,
        };
    }

    /// <summary>
    /// Schnittstelle der IPv4-Standardroute (wie die Windows-Taskleiste). Ohne IPv4-Route: die erste aktive
    /// Schnittstelle mit Gateway, sonst die erste aktive physische Schnittstelle.
    /// </summary>
    private static NetworkInterface? FindPrimaryInterface()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .ToList();
        if (candidates.Count == 0) return null;

        if (GetBestInterface(0x08080808, out uint bestIndex) == 0)
        {
            var best = candidates.FirstOrDefault(n => Ipv4Index(n) == bestIndex);
            if (best is not null) return best;
        }
        return candidates.FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Count > 0)
            ?? candidates.FirstOrDefault(n => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211);
    }

    private static int Ipv4Index(NetworkInterface adapter)
    {
        try
        {
            return adapter.GetIPProperties().GetIPv4Properties()?.Index ?? -1;
        }
        catch (NetworkInformationException)
        {
            return -1; // IPv4 auf diesem Adapter deaktiviert
        }
    }

    /// <summary>SSID, Signal, Raten und Standard über Native WiFi.</summary>
    private NetworkStatus AddWifiDetails(NetworkStatus status)
    {
        if (_wlan == IntPtr.Zero || !Guid.TryParse(status.AdapterId, out var guid)) return status;

        int result = WlanQueryInterface(_wlan, ref guid, WlanOpcodeCurrentConnection, IntPtr.Zero, out _, out IntPtr data, IntPtr.Zero);
        if (result == ERROR_ACCESS_DENIED)
        {
            // Windows 11 24H2+: SSID und Signal nur mit Standortzugriff. Der Name aus der Netzwerkliste bleibt (meist die SSID).
            return status with { WifiDetailsRestricted = true, Signal = Status.AdapterId == status.AdapterId ? Status.Signal : null };
        }
        if (result != 0 || data == IntPtr.Zero) return status;
        try
        {
            int ssidLength = Math.Clamp(Marshal.ReadInt32(data, WlanAttrSsidLength), 0, 32);
            var ssidBytes = new byte[ssidLength];
            Marshal.Copy(data + WlanAttrSsid, ssidBytes, 0, ssidLength);
            string ssid = Encoding.UTF8.GetString(ssidBytes);

            status = status with
            {
                NetworkName = ssid.Length > 0 ? ssid : status.NetworkName,
                Signal = Math.Clamp(Marshal.ReadInt32(data, WlanAttrSignalQuality), 0, 100),
                WifiRxRate = (uint)Marshal.ReadInt32(data, WlanAttrRxRate),
                WifiTxRate = (uint)Marshal.ReadInt32(data, WlanAttrTxRate),
                WifiStandard = PhyName(Marshal.ReadInt32(data, WlanAttrPhyType)),
                WifiSecured = Marshal.ReadInt32(data, WlanAttrSecurityEnabled) != 0,
            };
        }
        finally
        {
            WlanFreeMemory(data);
        }

        if (WlanQueryInterface(_wlan, ref guid, WlanOpcodeChannelNumber, IntPtr.Zero, out int size, out IntPtr channel, IntPtr.Zero) == 0 && channel != IntPtr.Zero)
        {
            if (size >= 4) status = status with { WifiChannel = Marshal.ReadInt32(channel) };
            WlanFreeMemory(channel);
        }
        return status;
    }

    /// <summary>DOT11_PHY_TYPE → Bezeichnung.</summary>
    private static string PhyName(int phy) => phy switch
    {
        4 => "802.11a",
        5 => "802.11g",
        6 => "802.11b",
        7 => "Wi-Fi 4 (802.11n)",
        8 => "Wi-Fi 5 (802.11ac)",
        9 => "802.11ad",
        10 => "Wi-Fi 6 (802.11ax)",
        11 => "Wi-Fi 7 (802.11be)",
        _ => "",
    };

    /// <summary>Name und Profil (öffentlich/privat) des Netzwerks, mit dem der Adapter verbunden ist – laut Windows-Netzwerkliste.</summary>
    private static (string Name, string Category) ReadNetworkListInfo(string adapterId)
    {
        if (!Guid.TryParse(adapterId, out var adapterGuid)) return ("", "");
        INetworkListManager? manager = null;
        IEnumNetworkConnections? connections = null;
        try
        {
            manager = (INetworkListManager)new NetworkListManagerComObject();
            if (manager.GetNetworkConnections(out connections) != 0 || connections is null) return ("", "");
            while (connections.Next(1, out var connection, out uint fetched) == 0 && fetched == 1)
            {
                try
                {
                    if (connection.GetAdapterId(out var id) != 0 || id != adapterGuid) continue;
                    if (connection.GetNetwork(out var network) != 0 || network is null) continue;
                    try
                    {
                        network.GetName(out var name);
                        string category = network.GetCategory(out int value) == 0
                            ? value switch { 0 => "Öffentlich", 1 => "Privat", 2 => "Domäne", _ => "" }
                            : "";
                        return (name?.Trim() ?? "", category);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(network);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(connection);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Network", $"Netzwerkliste nicht lesbar: {ex.Message}");
        }
        finally
        {
            if (connections is not null) Marshal.ReleaseComObject(connections);
            if (manager is not null) Marshal.ReleaseComObject(manager);
        }
        return ("", "");
    }

    /// <summary>Vergleich aller angezeigten Werte (die Listen im Record vergleichen sonst nur Verweise).</summary>
    private static string Signature(NetworkStatus s) =>
        string.Join("|", (s with { IPv4 = [], IPv6 = [], Gateways = [], DnsServers = [] }).ToString(),
            string.Join(",", s.IPv4), string.Join(",", s.IPv6), string.Join(",", s.Gateways), string.Join(",", s.DnsServers));

    private static Guid ReadGuid(IntPtr pointer)
    {
        var bytes = new byte[16];
        Marshal.Copy(pointer, bytes, 0, 16);
        return new Guid(bytes);
    }
}
