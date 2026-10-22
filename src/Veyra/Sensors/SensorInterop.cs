using System.Runtime.InteropServices;

namespace Veyra.Sensors;

/// <summary>
/// Windows-Schnittstellen für Systemwerte – alle ohne Zusatzsoftware und ohne Adminrechte:
/// GetSystemTimes (CPU), GlobalMemoryStatusEx (RAM), Leistungsindikatoren/PDH (GPU-Auslastung, wie der Task-Manager),
/// DXGI (Grafikkarten: Name, LUID, Videospeicher) und D3DKMT (GPU-Temperatur, -Takt und -Lüfter, wie der Task-Manager).
/// </summary>
internal static class SensorInterop
{
    // ---- CPU / RAM ------------------------------------------------------------------------
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    // ---- Akku / Laufwerke -----------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;        // 0 = Akku, 1 = Netz, 255 = unbekannt
        public byte BatteryFlag;         // 128 = kein Akku, 8 = lädt, 255 = unbekannt
        public byte BatteryLifePercent;  // 0–100, 255 = unbekannt
        public byte SystemStatusFlag;    // 1 = Energiesparen aktiv
        public int BatteryLifeTime;      // Sekunden, -1 = unbekannt
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetDiskFreeSpaceEx(string directory, out ulong freeForCaller, out ulong total, out ulong totalFree);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern uint GetDriveType(string root);

    public const uint DRIVE_REMOVABLE = 2, DRIVE_FIXED = 3;

    [DllImport("kernel32.dll")]
    public static extern uint SetErrorMode(uint mode);

    public const uint SEM_FAILCRITICALERRORS = 0x0001;

    // ---- PDH (Leistungsindikatoren) -------------------------------------------------------
    public const uint PDH_FMT_DOUBLE = 0x00000200;
    public const uint PDH_FMT_NOCAP100 = 0x00008000;
    public const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    [StructLayout(LayoutKind.Sequential)]
    public struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr szName;
        public uint CStatus;
        public double doubleValue; // Union-Feld; durch die Ausrichtung liegt es wie in C bei Offset 16
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern int PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    public static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    public static extern int PdhRemoveCounter(IntPtr counter);

    [DllImport("pdh.dll")]
    public static extern int PdhCloseQuery(IntPtr query);

    // ---- DXGI (Grafikkarten auflisten) ----------------------------------------------------
    [DllImport("dxgi.dll")]
    public static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    public static readonly Guid IID_IDXGIFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Flags;
    }

    // Nur die benötigten Methoden; die Reihenfolge der Vtable-Einträge muss exakt stimmen.
    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIFactory1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumAdapters();
        void MakeWindowAssociation();
        void GetWindowAssociation();
        void CreateSwapChain();
        void CreateSoftwareAdapter();

        [PreserveSig]
        int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDXGIAdapter1
    {
        void SetPrivateData();
        void SetPrivateDataInterface();
        void GetPrivateData();
        void GetParent();
        void EnumOutputs();
        void GetDesc();
        void CheckInterfaceSupport();

        [PreserveSig]
        int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }

    // ---- D3DKMT (GPU-Temperatur, -Takt, -Lüfter) ------------------------------------------
    public const int KMTQAITYPE_NODEPERFDATA = 61;
    public const int KMTQAITYPE_ADAPTERPERFDATA = 62;
    public const int KMTQAITYPE_ADAPTERPERFDATA_CAPS = 63;

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_OPENADAPTERFROMLUID
    {
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint hAdapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_CLOSEADAPTER
    {
        public uint hAdapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_QUERYADAPTERINFO
    {
        public uint hAdapter;
        public int Type;
        public IntPtr pPrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;        // Promille
        public uint Temperature;  // Zehntel °C
        public byte PowerStateOverride;
    }

    /// <summary>Feste Werte der Karte, z. B. die höchste Lüfterdrehzahl (0 = unbekannt).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_ADAPTER_PERFDATACAPS
    {
        public uint PhysicalAdapterIndex;
        public ulong MaxMemoryBandwidth;
        public ulong MaxPCIEBandwidth;
        public uint MaxFanRPM;
        public uint TemperatureMax;
        public uint TemperatureWarning;
    }

    /// <summary>Takt einer Engine (Node 0 = 3D) in Hz, wie im Task-Manager.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct D3DKMT_NODE_PERFDATA
    {
        public uint NodeOrdinal;
        public uint PhysicalAdapterIndex;
        public ulong Frequency;
        public ulong MaxFrequency;
        public ulong MaxFrequencyOC;
        public uint Voltage;
        public uint VoltageMax;
        public uint VoltageMaxOC;
        public ulong MaxTransitionLatency;
    }

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTOpenAdapterFromLuid(ref D3DKMT_OPENADAPTERFROMLUID open);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER close);

    [DllImport("gdi32.dll")]
    public static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO info);
}
