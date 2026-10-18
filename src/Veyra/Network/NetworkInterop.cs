using System.Runtime.InteropServices;

namespace Veyra.Network;

// IP-Hilfsfunktionen (iphlpapi), Native WiFi (wlanapi) und Netzwerkliste (NLM, COM) – ohne externe Bibliothek.
// Große Strukturen werden über feste Offsets gelesen statt vollständig nachgebaut (Layouts laut Windows SDK, x64).

internal static class NetworkNative
{
    // ---- iphlpapi -------------------------------------------------------------------------

    /// <summary>Interface-Index der Route zu einer Zieladresse (IPv4, Netzwerk-Byte-Reihenfolge). Sendet nichts.</summary>
    [DllImport("iphlpapi.dll")]
    public static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

    [DllImport("iphlpapi.dll")]
    public static extern int GetIfTable2(out IntPtr table);

    [DllImport("iphlpapi.dll")]
    public static extern void FreeMibTable(IntPtr memory);

    [DllImport("iphlpapi.dll")]
    public static extern int GetNetworkConnectivityHint(out NL_NETWORK_CONNECTIVITY_HINT hint);

    [DllImport("iphlpapi.dll")]
    public static extern int NotifyNetworkConnectivityHintChange(ConnectivityHintCallback callback, IntPtr context,
        [MarshalAs(UnmanagedType.U1)] bool initialNotification, out IntPtr notificationHandle);

    [DllImport("iphlpapi.dll")]
    public static extern int CancelMibChangeNotify2(IntPtr notificationHandle);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void ConnectivityHintCallback(IntPtr context, NL_NETWORK_CONNECTIVITY_HINT hint);

    [StructLayout(LayoutKind.Sequential)]
    public struct NL_NETWORK_CONNECTIVITY_HINT
    {
        public int ConnectivityLevel; // NL_NETWORK_CONNECTIVITY_LEVEL_HINT
        public int ConnectivityCost;  // NL_NETWORK_CONNECTIVITY_COST_HINT
        public byte ApproachingDataLimit;
        public byte OverDataLimit;
        public byte Roaming;
    }

    public const int ConnectivityLevelNone = 1;
    public const int ConnectivityLevelLocal = 2;
    public const int ConnectivityLevelInternet = 3;
    public const int ConnectivityLevelConstrained = 4;
    public const int ConnectivityCostFixed = 2;
    public const int ConnectivityCostVariable = 3;

    // MIB_IF_TABLE2: ULONG NumEntries, danach (8-Byte-ausgerichtet) MIB_IF_ROW2[] mit je 1352 Byte.
    public const int IfTableRowsOffset = 8;
    public const int IfRowSize = 1352;
    public const int IfRowGuid = 12;
    public const int IfRowAlias = 28;
    public const int IfRowDescription = 542;
    public const int IfRowType = 1128;
    public const int IfRowFlags = 1152;       // Bitfeld: HardwareInterface (Bit 0), FilterInterface (Bit 1)
    public const int IfRowOperStatus = 1156;
    public const int IfRowInOctets = 1208;
    public const int IfRowOutOctets = 1280;
    public const int IfTypeLoopback = 24;
    public const int IfOperStatusUp = 1;

    // ---- wlanapi --------------------------------------------------------------------------

    [DllImport("wlanapi.dll")]
    public static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [DllImport("wlanapi.dll")]
    public static extern int WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [DllImport("wlanapi.dll")]
    public static extern int WlanEnumInterfaces(IntPtr clientHandle, IntPtr reserved, out IntPtr interfaceList);

    [DllImport("wlanapi.dll")]
    public static extern int WlanQueryInterface(IntPtr clientHandle, ref Guid interfaceGuid, int opCode, IntPtr reserved,
        out int dataSize, out IntPtr data, IntPtr opcodeValueType);

    [DllImport("wlanapi.dll")]
    public static extern void WlanFreeMemory(IntPtr memory);

    [DllImport("wlanapi.dll")]
    public static extern int WlanRegisterNotification(IntPtr clientHandle, uint notifSource,
        [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate, WlanNotificationCallback? callback, IntPtr context,
        IntPtr reserved, out uint prevNotifSource);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void WlanNotificationCallback(IntPtr notificationData, IntPtr context);

    public const int ERROR_ACCESS_DENIED = 5;
    public const uint WLAN_NOTIFICATION_SOURCE_NONE = 0;
    public const uint WLAN_NOTIFICATION_SOURCE_ACM = 0x8;
    public const uint WLAN_NOTIFICATION_SOURCE_MSM = 0x10;
    public const int WlanOpcodeCurrentConnection = 7;
    public const int WlanOpcodeChannelNumber = 8;

    // WLAN_NOTIFICATION_DATA: DWORD Source, DWORD Code, GUID InterfaceGuid, DWORD dwDataSize, PVOID pData (Offset 32)
    public const int NotificationCode = 4;
    public const int NotificationGuid = 8;
    public const int NotificationDataSize = 24;
    public const int NotificationData = 32;

    public const int AcmConnectionComplete = 10;
    public const int AcmInterfaceArrival = 13;
    public const int AcmInterfaceRemoval = 14;
    public const int AcmDisconnected = 21;
    public const int MsmConnected = 4;
    public const int MsmRoamingEnd = 6;
    public const int MsmSignalQualityChange = 8;
    public const int MsmDisconnected = 10;

    // WLAN_INTERFACE_INFO_LIST: DWORD Anzahl, DWORD Index, danach WLAN_INTERFACE_INFO[] mit je 532 Byte (GUID am Anfang).
    public const int WlanListItemsOffset = 8;
    public const int WlanInterfaceInfoSize = 532;

    // WLAN_CONNECTION_ATTRIBUTES
    public const int WlanAttrProfileName = 8;
    public const int WlanAttrSsidLength = 520;
    public const int WlanAttrSsid = 524;
    public const int WlanAttrPhyType = 568;
    public const int WlanAttrSignalQuality = 576;
    public const int WlanAttrRxRate = 580; // kbit/s
    public const int WlanAttrTxRate = 584; // kbit/s
    public const int WlanAttrSecurityEnabled = 588;
}

// ---- Netzwerkliste (Network List Manager) – duale Schnittstellen, VTable-Reihenfolge laut netlistmgr.h ----

[ComImport, Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B")]
internal class NetworkListManagerComObject;

[ComImport, Guid("DCB00000-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface INetworkListManager
{
    [PreserveSig] int GetNetworks(int flags, out IntPtr enumNetworks);
    [PreserveSig] int GetNetwork(Guid networkId, out IntPtr network);
    [PreserveSig] int GetNetworkConnections(out IEnumNetworkConnections enumConnections);
}

[ComImport, Guid("DCB00006-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IEnumNetworkConnections
{
    [PreserveSig] int GetNewEnum(out IntPtr enumVariant);
    [PreserveSig] int Next(uint count, out INetworkConnection connection, out uint fetched);
}

[ComImport, Guid("DCB00005-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface INetworkConnection
{
    [PreserveSig] int GetNetwork(out INetwork network);
    [PreserveSig] int IsConnectedToInternet(out short connected);
    [PreserveSig] int IsConnected(out short connected);
    [PreserveSig] int GetConnectivity(out int connectivity);
    [PreserveSig] int GetConnectionId(out Guid connectionId);
    [PreserveSig] int GetAdapterId(out Guid adapterId);
}

[ComImport, Guid("DCB00002-570F-4A9B-8D69-199FDBA5723B"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface INetwork
{
    [PreserveSig] int GetName([MarshalAs(UnmanagedType.BStr)] out string name);
    [PreserveSig] int SetName([MarshalAs(UnmanagedType.BStr)] string name);
    [PreserveSig] int GetDescription([MarshalAs(UnmanagedType.BStr)] out string description);
    [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.BStr)] string description);
    [PreserveSig] int GetNetworkId(out Guid networkId);
    [PreserveSig] int GetDomainType(out int domainType);
    [PreserveSig] int GetNetworkConnections(out IntPtr enumConnections);
    [PreserveSig] int GetTimeCreatedAndConnected(out uint createdLow, out uint createdHigh, out uint connectedLow, out uint connectedHigh);
    [PreserveSig] int IsConnectedToInternet(out short connected);
    [PreserveSig] int IsConnected(out short connected);
    [PreserveSig] int GetConnectivity(out int connectivity);
    [PreserveSig] int GetCategory(out int category); // 0 öffentlich, 1 privat, 2 Domäne
}
