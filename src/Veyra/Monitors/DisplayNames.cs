using System.Runtime.InteropServices;

namespace Veyra.Monitors;

/// <summary>Monitor laut Anzeigekonfiguration: Name aus den EDID-Daten (wie in den Windows-Einstellungen) und Anschlussart.</summary>
public sealed record DisplayTarget(string GdiName, string FriendlyName, bool IsInternal);

/// <summary>
/// Monitornamen über <c>QueryDisplayConfig</c>/<c>DisplayConfigGetDeviceInfo</c>: ordnet den GDI-Gerätenamen
/// („\\.\DISPLAY1“) dem Monitornamen zu („LG ULTRAGEAR“). <c>EnumDisplayDevices</c> liefert dafür meist nur
/// „Generischer PnP-Monitor“.
/// </summary>
public static class DisplayNames
{
    private const uint QDC_ONLY_ACTIVE_PATHS = 2;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const int DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;
    private const uint OutputInternal = 0x80000000;
    private const uint OutputLvds = 6;
    private const uint OutputDisplayPortEmbedded = 11;
    private const uint OutputUdiEmbedded = 13;

    public static IReadOnlyList<DisplayTarget> Query()
    {
        var result = new List<DisplayTarget>();
        try
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0) return result;
            var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modes = new byte[modeCount * 64]; // DISPLAYCONFIG_MODE_INFO, wird hier nicht ausgewertet
            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return result;

            foreach (var path in paths.Take((int)pathCount))
            {
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = path.sourceAdapterId,
                        id = path.sourceId,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;

                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size = (uint)Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = path.targetAdapterId,
                        id = path.targetId,
                    },
                };
                string friendly = DisplayConfigGetDeviceInfo(ref target) == 0 ? target.monitorFriendlyDeviceName?.Trim() ?? "" : "";
                bool internalPanel = path.outputTechnology is OutputInternal or OutputLvds or OutputDisplayPortEmbedded or OutputUdiEmbedded;
                if (friendly.Length == 0) friendly = internalPanel ? "Integrierter Bildschirm" : source.viewGdiDeviceName.TrimStart('\\', '.');
                result.Add(new DisplayTarget(source.viewGdiDeviceName, friendly, internalPanel));
            }
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Sehr alte Systeme ohne CCD-API: ohne Namen weiter.
        }
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    /// <summary>DISPLAYCONFIG_PATH_INFO flach ausgeschrieben (Quelle, Ziel, Flags) – 72 Byte.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public LUID sourceAdapterId;
        public uint sourceId;
        public uint sourceModeInfoIdx;
        public uint sourceStatusFlags;
        public LUID targetAdapterId;
        public uint targetId;
        public uint targetModeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshNumerator;
        public uint refreshDenominator;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint targetStatusFlags;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int type;
        public uint size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
        ref uint modeCount, [Out] byte[] modes, IntPtr currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);
}
