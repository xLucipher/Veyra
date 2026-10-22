using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Veyra.Core;

namespace Veyra.Peripherals;

/// <summary>
/// Akkustand kabelloser Geräte mit eigenem Funk-Empfänger (oder per Kabel) direkt am Gerät – ohne Hersteller-Software.
/// Die unterstützten Geräte (VID 1532) haben eine Steuerschnittstelle mit 90-Byte-Feature-Reports (+ Report-ID). Abgefragt werden nur die Lese-Befehle
/// „Akkustand“ (Klasse 0x07, Befehl 0x80) und „Lädt“ (0x07/0x84). Die Transaktions-ID unterscheidet sich je
/// Gerätegeneration; die passende wird ausprobiert und gemerkt. Geöffnet wird ohne Lese-/Schreibrecht – so erlaubt
/// Windows Feature-Reports auch an Maus- und Tastatur-Schnittstellen, die das System selbst belegt.
/// Headsets sprechen ein anderes Protokoll (siehe <see cref="ReadHeadset"/>).
/// </summary>
internal static class ReceiverHid
{
    private const int ReportLength = 91; // Report-ID + 90 Byte
    private const byte StatusSuccess = 0x02;
    private const byte StatusBusy = 0x01;
    private const byte StatusNotSupported = 0x05;
    private const byte CommandClassPower = 0x07;
    private const byte CommandBattery = 0x80;
    private const byte CommandCharging = 0x84;
    private static readonly byte[] TransactionIds = [0x1F, 0x3F, 0xFF, 0x9F];

    /// <summary>Gemerkte Transaktions-ID je Schnittstelle.</summary>
    private static readonly Dictionary<string, byte> KnownTransaction = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Schnittstellen ohne Antwort (kein Akku, schlafendes Gerät) erst später wieder versuchen.</summary>
    private static readonly Dictionary<string, DateTime> RetryAfter = new(StringComparer.OrdinalIgnoreCase);

    private static readonly TimeSpan UnsupportedRetry = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan OfflineRetry = TimeSpan.FromMinutes(2);

    public static List<BatteryDevice> Read()
    {
        var found = new Dictionary<string, BatteryDevice>(StringComparer.OrdinalIgnoreCase);
        var probed = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Produkt-ID schon beantwortet
        var now = DateTime.Now;
        var paths = HidPaths().Where(p => p.Contains("vid_1532", StringComparison.OrdinalIgnoreCase)).ToList();

        // Geräteart je Produkt aus seinen HID-Schnittstellen: Maus vor Tastatur (Mäuse melden oft zusätzlich eine
        // Tastatur-Schnittstelle für Makrotasten).
        var kinds = new Dictionary<string, BatteryKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            using var probe = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (probe.IsInvalid) continue;
            var (page, usage, _) = Caps(probe);
            var kind = page != 1 ? BatteryKind.Unknown : usage switch { 2 => BatteryKind.Mouse, 6 => BatteryKind.Keyboard, 4 or 5 => BatteryKind.Controller, _ => BatteryKind.Unknown };
            string id = ProductId(path);
            if (kind != BatteryKind.Unknown && (!kinds.TryGetValue(id, out var known) || known != BatteryKind.Mouse)) kinds[id] = kind;
        }

        foreach (var path in paths)
        {
            string product = ProductId(path);
            if (probed.Contains(product)) continue;
            if (RetryAfter.TryGetValue(path, out var until) && until > now) continue;

            using var handle = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) continue;
            var (usagePage, _, featureLength) = Caps(handle);

            if (HeadsetProducts.TryGetValue(product, out var headsetName) && usagePage == HeadsetUsagePage && featureLength == HeadsetReportLength)
            {
                if (ReadHeadset(handle) is { } headset)
                {
                    probed.Add(product);
                    found.TryAdd(headsetName, new BatteryDevice("rx:" + headsetName, headsetName, BatterySource.Receiver, headset.Percent, headset.Charging, BatteryKind.Headset));
                }
                else
                {
                    RetryAfter[path] = now + OfflineRetry; // Headset aus oder außer Reichweite
                }
                continue;
            }
            if (featureLength != ReportLength) continue;

            var (raw, status) = Query(handle, path, CommandBattery);
            if (raw is not { } value || value == 0)
            {
                // Kein Akku (Kabel-Tastatur o. Ä.) → selten wieder fragen. Gerät aus, schlafend oder gerade per
                // Bluetooth verbunden (der Dongle meldet dann „keine Antwort“) → bald wieder.
                if (status == StatusNotSupported) KnownTransaction.Remove(path);
                RetryAfter[path] = now + (status == StatusNotSupported ? UnsupportedRetry : OfflineRetry);
                continue;
            }
            bool? charging = Query(handle, path, CommandCharging).Value is { } c ? c == 1 : null;
            probed.Add(product);
            string name = ProductName(handle) ?? "Kabelloses Gerät";
            int percent = (int)Math.Round(value * 100 / 255.0);
            found.TryAdd(name, new BatteryDevice("rx:" + name, name, BatterySource.Receiver, percent, charging, kinds.GetValueOrDefault(product)));
        }
        return found.Values.ToList();
    }

    /// <summary>Wert des ersten Arguments der Antwort (null ohne gültige Antwort) und der letzte Status des Geräts.</summary>
    private static (byte? Value, byte Status) Query(SafeFileHandle handle, string path, byte command)
    {
        byte[] ids = KnownTransaction.TryGetValue(path, out var known) ? [known] : TransactionIds;
        byte status = 0;
        foreach (var id in ids)
        {
            (var response, status) = Transact(handle, id, command);
            if (response is not null)
            {
                KnownTransaction[path] = id;
                return (response[10], status); // Argumente ab Byte 9; der Wert steht im zweiten
            }
            if (status == StatusNotSupported) break; // gilt für jede Transaktions-ID
        }
        return (null, status);
    }

    private static (byte[]? Response, byte Status) Transact(SafeFileHandle handle, byte transaction, byte command)
    {
        var request = new byte[ReportLength];
        request[2] = transaction;
        request[6] = 0x02; // Datenlänge
        request[7] = CommandClassPower;
        request[8] = command;
        byte crc = 0;
        for (int i = 3; i < 89; i++) crc ^= request[i];
        request[89] = crc;
        if (!HidD_SetFeature(handle, request, ReportLength)) return (null, 0);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 50 : 80); // das Gerät braucht einen Moment für die Antwort
            var response = new byte[ReportLength];
            if (!HidD_GetFeature(handle, response, ReportLength)) return (null, 0);
            if (response[1] == StatusBusy) continue;
            bool matches = response[1] == StatusSuccess && response[2] == transaction && response[7] == CommandClassPower && response[8] == command;
            return (matches ? response : null, response[1]);
        }
        return (null, StatusBusy);
    }

    // ---- Headsets (eigenes Protokoll) -----------------------------------------------------
    // Headset-Empfänger 1532:0528: 64-Byte-Feature-Report mit Report-ID 0xFF an der Herstellerschnittstelle
    // (Usage-Page 0xFF00). Die Anfrage entspricht der der Hersteller-Software. Antwort: Kopf ff 0f 05 fe 12 04 1f 08 05, dann in
    // Byte 11 der Zustand (Bit 0x08 = lädt), 12–13 die Spannung in mV, 14 der Ladestand in Prozent. Ohne neue Antwort
    // vom Headset (aus, außer Reichweite) wiederholt der Empfänger den alten Stand unter dem Kopf ff 01 00 … – verworfen.
    private const int HeadsetReportLength = 64;
    private const ushort HeadsetUsagePage = 0xFF00;
    // Die Herstellerschnittstelle meldet als Produktnamen nur „Hid Interface“ – deshalb ein allgemeiner Name je Produkt-ID.
    private static readonly Dictionary<string, string> HeadsetProducts = new(StringComparer.OrdinalIgnoreCase) { ["pid_0528"] = "Headset" };
    private static readonly byte[] HeadsetRequest = [0xFF, 0x0A, 0x00, 0xFD, 0x04, 0x12, 0xF1, 0x02, 0x05];
    private static readonly byte[] HeadsetReply = [0xFF, 0x0F, 0x05, 0xFE, 0x12, 0x04, 0x1F, 0x08, 0x05];

    private static (int Percent, bool Charging)? ReadHeadset(SafeFileHandle handle)
    {
        var request = new byte[HeadsetReportLength];
        HeadsetRequest.CopyTo(request, 0);
        if (!HidD_SetFeature(handle, request, HeadsetReportLength)) return null;
        Thread.Sleep(100); // wie die Hersteller-Software; nur die erste Antwort ist frisch
        var response = new byte[HeadsetReportLength];
        response[0] = 0xFF;
        if (!HidD_GetFeature(handle, response, HeadsetReportLength) || !response.AsSpan(0, HeadsetReply.Length).SequenceEqual(HeadsetReply))
            return null;
        int voltage = response[12] << 8 | response[13];
        int level = response[14];
        if (voltage == 0 || level > 100) return null;
        return (level, (response[11] & 0x08) != 0);
    }

    private static (ushort UsagePage, ushort Usage, int FeatureLength) Caps(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var data)) return (0, 0, 0);
        try
        {
            return HidP_GetCaps(data, out var caps) == HIDP_STATUS_SUCCESS ? (caps.UsagePage, caps.Usage, (int)caps.FeatureReportByteLength) : ((ushort)0, (ushort)0, 0);
        }
        finally
        {
            HidD_FreePreparsedData(data);
        }
    }

    private static unsafe string? ProductName(SafeFileHandle handle)
    {
        char* buffer = stackalloc char[128];
        if (!HidD_GetProductString(handle, buffer, 256)) return null;
        string name = new string(buffer).Trim();
        return name.Length > 0 ? name : null;
    }

    /// <summary>"pid_00ab" aus dem Gerätepfad – Dongle und Kabel desselben Geräts haben verschiedene IDs.</summary>
    private static string ProductId(string path)
    {
        int index = path.IndexOf("pid_", StringComparison.OrdinalIgnoreCase);
        return index >= 0 && index + 8 <= path.Length ? path.Substring(index, 8).ToLowerInvariant() : path;
    }

    private static List<string> HidPaths()
    {
        HidD_GetHidGuid(out var guid);
        var paths = new List<string>();
        var set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return paths;
        try
        {
            var iface = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref iface); i++)
            {
                SetupDiGetDeviceInterfaceDetailW(set, ref iface, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
                if (size == 0) continue;
                var detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, 8); // cbSize von SP_DEVICE_INTERFACE_DETAIL_DATA_W auf x64
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref iface, detail, size, out _, IntPtr.Zero) &&
                        Marshal.PtrToStringUni(detail + 4) is { Length: > 0 } path)
                        paths.Add(path);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Battery", $"HID-Geräte nicht lesbar: {ex.Message}");
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return paths;
    }

    private const uint FILE_SHARE_READ = 0x1;
    private const uint FILE_SHARE_WRITE = 0x2;
    private const uint OPEN_EXISTING = 3;
    private const uint DIGCF_PRESENT = 0x2;
    private const uint DIGCF_DEVICEINTERFACE = 0x10;
    private const int HIDP_STATUS_SUCCESS = 0x00110000;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("hid.dll")]
    private static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetFeature(SafeFileHandle device, byte[] buffer, int length);

    [DllImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(SafeFileHandle device, byte[] buffer, int length);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);

    [DllImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern unsafe bool HidD_GetProductString(SafeFileHandle device, char* buffer, int length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint size,
        out uint requiredSize, IntPtr devInfo);

    [DllImport("setupapi.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
}
