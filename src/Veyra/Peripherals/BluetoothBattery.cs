using System.Runtime.InteropServices;

namespace Veyra.Peripherals;

/// <summary>
/// Akkustand von Bluetooth-Geräten, so wie Windows ihn unter Einstellungen → Bluetooth zeigt: Eigenschaft
/// <c>DEVPKEY_Bluetooth_Battery</c> am Geräteknoten (SetupAPI). Ein Gerät besteht aus mehreren Knoten mit derselben
/// Container-ID; der Name kommt vom Knoten des Geräts selbst (BTHLE\DEV_… bzw. BTHENUM\DEV_…). Getrennte Geräte
/// (<c>DN_DEVICE_DISCONNECTED</c>) werden ausgelassen, weil Windows ihren letzten Wert stehen lässt.
/// </summary>
internal static class BluetoothBattery
{
    private const uint DIGCF_PRESENT = 0x2;
    private const uint DIGCF_ALLCLASSES = 0x4;
    private const uint DEVPROP_TYPE_BYTE = 0x3;
    private const uint DEVPROP_TYPE_GUID = 0xD;
    private const uint DEVPROP_TYPE_STRING = 0x12;
    private const uint DN_DEVICE_DISCONNECTED = 0x02000000;

    private static DEVPROPKEY BatteryKey = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    private static DEVPROPKEY ContainerKey = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    private static DEVPROPKEY FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    private static DEVPROPKEY NameKey = new(new Guid("B725F130-47EF-101A-A5F1-02608C9EEBAC"), 10);

    public static List<BatteryDevice> Read()
    {
        var batteries = new Dictionary<Guid, (int Percent, bool Connected, string Name)>();
        var names = new Dictionary<Guid, string>();

        var set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
        if (set == new IntPtr(-1)) return [];
        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                string instance = InstanceId(set, ref data);
                bool deviceNode = instance.StartsWith(@"BTHLE\DEV_", StringComparison.OrdinalIgnoreCase) ||
                                  instance.StartsWith(@"BTHENUM\DEV_", StringComparison.OrdinalIgnoreCase);
                byte? battery = ByteProperty(set, ref data, ref BatteryKey);
                if (battery is null && !deviceNode) continue;

                var container = GuidProperty(set, ref data, ref ContainerKey) ?? Guid.Empty;
                string name = StringProperty(set, ref data, ref FriendlyNameKey) ?? StringProperty(set, ref data, ref NameKey) ?? "";
                if (deviceNode && name.Length > 0) names[container] = name;
                if (battery is not { } percent) continue;

                bool connected = CM_Get_DevNode_Status(out uint status, out _, data.DevInst, 0) == 0 && (status & DN_DEVICE_DISCONNECTED) == 0;
                // Mehrere Knoten eines Geräts melden denselben Wert – ein verbundener gewinnt.
                if (!batteries.TryGetValue(container, out var known) || (connected && !known.Connected))
                    batteries[container] = (Math.Clamp((int)percent, 0, 100), connected, name);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return batteries
            .Where(b => b.Value.Connected)
            .Select(b =>
            {
                string name = names.GetValueOrDefault(b.Key) ?? (b.Value.Name.Length > 0 ? b.Value.Name : "Bluetooth-Gerät");
                return new BatteryDevice("bt:" + name, name, BatterySource.Bluetooth, b.Value.Percent, null);
            })
            .DistinctBy(d => d.Key)
            .ToList();
    }

    private static unsafe string InstanceId(IntPtr set, ref SP_DEVINFO_DATA data)
    {
        char* buffer = stackalloc char[512];
        return SetupDiGetDeviceInstanceIdW(set, ref data, buffer, 512, out _) ? new string(buffer) : "";
    }

    private static unsafe byte? ByteProperty(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key)
    {
        byte value;
        return SetupDiGetDevicePropertyW(set, ref data, ref key, out uint type, &value, 1, out _, 0) && type == DEVPROP_TYPE_BYTE ? value : null;
    }

    private static unsafe Guid? GuidProperty(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key)
    {
        Guid value;
        return SetupDiGetDevicePropertyW(set, ref data, ref key, out uint type, (byte*)&value, (uint)sizeof(Guid), out _, 0) && type == DEVPROP_TYPE_GUID
            ? value
            : null;
    }

    private static unsafe string? StringProperty(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key)
    {
        char* buffer = stackalloc char[256];
        if (!SetupDiGetDevicePropertyW(set, ref data, ref key, out uint type, (byte*)buffer, 512, out _, 0) || type != DEVPROP_TYPE_STRING) return null;
        string text = new(buffer);
        return text.Trim().Length > 0 ? text.Trim() : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY(Guid fmtid, uint pid)
    {
        public Guid fmtid = fmtid;
        public uint pid = pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type,
        byte* buffer, uint bufferSize, out uint requiredSize, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern unsafe bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SP_DEVINFO_DATA data, char* buffer, uint bufferSize, out uint requiredSize);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
}
