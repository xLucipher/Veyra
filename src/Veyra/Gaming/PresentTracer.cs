using System.Runtime.InteropServices;
using Veyra.Core;

namespace Veyra.Gaming;

/// <summary>Ergebnis beim Starten der Messung.</summary>
public enum TraceStart
{
    Started,

    /// <summary>Keine Berechtigung (Gruppe „Leistungsprotokollbenutzer“ fehlt oder ist noch nicht wirksam).</summary>
    AccessDenied,

    Failed,
}

/// <summary>
/// ETW-Echtzeitsitzung für die Present-Ereignisse von Spielen, nur das Nötigste: Ereignis
/// „Present_Start“ der Anbieter Microsoft-Windows-DXGI (DirectX 10/11/12, ID 42) und Microsoft-Windows-D3D9 (ID 1).
/// Ausgewertet werden nur Kopfdaten (Prozess, Zeitstempel) und als erstes Feld die Swapchain, damit Overlays im selben
/// Prozess (eigene Swapchain) nicht mitgezählt werden. Ohne Adminrechte nur für Mitglieder der Gruppe
/// „Leistungsprotokollbenutzer“. Sitzungen überleben einen Absturz – deshalb wird eine alte Sitzung gleichen Namens
/// vor dem Start beendet.
/// </summary>
internal sealed class PresentTracer : IDisposable
{
    public const string SessionName = "Veyra FPS";

    private static readonly Guid DxgiProvider = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9Provider = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    private const ushort DxgiPresentStart = 42;
    private const ushort D3D9PresentStart = 1;

    private readonly Action<uint, ulong, long> _onPresent;
    private readonly EventRecordCallback _callback; // muss leben, solange ProcessTrace läuft
    private ulong _session;
    private ulong _trace = InvalidTrace;
    private Thread? _thread;

    /// <param name="onPresent">Prozess-ID, Swapchain, QPC-Zeitstempel – auf dem Mess-Thread aufgerufen.</param>
    public PresentTracer(Action<uint, ulong, long> onPresent)
    {
        _onPresent = onPresent;
        _callback = OnEvent;
    }

    public TraceStart Start()
    {
        StopSession(); // Rest einer abgestürzten Instanz
        var properties = CreateProperties();
        try
        {
            int error = StartTraceW(out _session, SessionName, properties);
            if (error == ERROR_ACCESS_DENIED) return TraceStart.AccessDenied;
            if (error != 0)
            {
                Log.Warning("Fps", $"ETW-Sitzung konnte nicht gestartet werden (Fehler {error})");
                return TraceStart.Failed;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(properties);
        }

        foreach (var provider in new[] { DxgiProvider, D3D9Provider })
        {
            var guid = provider;
            int error = EnableTraceEx2(_session, ref guid, EVENT_CONTROL_CODE_ENABLE_PROVIDER, TRACE_LEVEL_VERBOSE, 0, 0, 0, IntPtr.Zero);
            if (error == ERROR_ACCESS_DENIED)
            {
                Stop();
                return TraceStart.AccessDenied;
            }
            if (error != 0) Log.Warning("Fps", $"Anbieter {provider} nicht aktivierbar (Fehler {error})");
        }

        var logfile = Marshal.AllocHGlobal(LogfileSize);
        var name = Marshal.StringToHGlobalUni(SessionName);
        try
        {
            unsafe
            {
                new Span<byte>((void*)logfile, LogfileSize).Clear();
            }
            Marshal.WriteIntPtr(logfile, LogfileLoggerNameOffset, name);
            Marshal.WriteInt32(logfile, LogfileModeOffset, PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD);
            Marshal.WriteIntPtr(logfile, LogfileCallbackOffset, Marshal.GetFunctionPointerForDelegate(_callback));
            _trace = OpenTraceW(logfile);
        }
        finally
        {
            Marshal.FreeHGlobal(logfile);
            Marshal.FreeHGlobal(name);
        }
        if (_trace == InvalidTrace)
        {
            int error = Marshal.GetLastWin32Error();
            Stop();
            return error == ERROR_ACCESS_DENIED ? TraceStart.AccessDenied : TraceStart.Failed;
        }

        var trace = _trace;
        _thread = new Thread(() =>
        {
            int result = ProcessTrace([trace], 1, IntPtr.Zero, IntPtr.Zero);
            if (result != 0 && result != ERROR_CANCELLED) Log.Debug("Fps", $"ProcessTrace beendet: {result}");
        })
        { IsBackground = true, Name = "Veyra-FPS" };
        _thread.Start();
        Log.Info("Fps", "FPS-Messung gestartet (ETW: DXGI, D3D9)");
        return TraceStart.Started;
    }

    public void Stop()
    {
        StopSession();
        if (_trace != InvalidTrace)
        {
            CloseTrace(_trace);
            _trace = InvalidTrace;
        }
        _thread?.Join(2000);
        _thread = null;
        _session = 0;
    }

    public void Dispose() => Stop();

    private static void StopSession()
    {
        var properties = CreateProperties();
        try
        {
            ControlTraceW(0, SessionName, properties, EVENT_TRACE_CONTROL_STOP);
        }
        finally
        {
            Marshal.FreeHGlobal(properties);
        }
    }

    private unsafe void OnEvent(IntPtr record)
    {
        try
        {
            byte* r = (byte*)record;
            ushort id = *(ushort*)(r + 40);
            var provider = *(Guid*)(r + 24);
            bool present = (id == DxgiPresentStart && provider == DxgiProvider) || (id == D3D9PresentStart && provider == D3D9Provider);
            if (!present) return;

            uint pid = *(uint*)(r + 12);
            long timestamp = *(long*)(r + 16);
            ushort flags = *(ushort*)(r + 4);
            ushort length = *(ushort*)(r + 86);
            byte* data = *(byte**)(r + 96);
            // Erstes Feld: Zeiger auf die Swapchain – 4 Byte bei 32-Bit-Prozessen.
            bool is32 = (flags & EVENT_HEADER_FLAG_32_BIT_HEADER) != 0;
            ulong swapchain = data == null ? 0 : is32 ? (length >= 4 ? *(uint*)data : 0) : (length >= 8 ? *(ulong*)data : 0);
            _onPresent(pid, swapchain, timestamp);
        }
        catch
        {
            // Im Rückruf von ETW nichts werfen.
        }
    }

    /// <summary>EVENT_TRACE_PROPERTIES (120 Byte) mit Platz für den Sitzungsnamen dahinter.</summary>
    private static IntPtr CreateProperties()
    {
        const int size = PropertiesSize + 1024;
        var p = Marshal.AllocHGlobal(size);
        unsafe
        {
            new Span<byte>((void*)p, size).Clear();
        }
        Marshal.WriteInt32(p, 0, size);                        // Wnode.BufferSize
        Marshal.WriteInt32(p, 40, 1);                          // Wnode.ClientContext: QPC-Zeitstempel
        Marshal.WriteInt32(p, 44, WNODE_FLAG_TRACED_GUID);     // Wnode.Flags
        Marshal.WriteInt32(p, 48, 64);                         // BufferSize (KB)
        Marshal.WriteInt32(p, 52, 4);                          // MinimumBuffers
        Marshal.WriteInt32(p, 56, 32);                         // MaximumBuffers
        Marshal.WriteInt32(p, 64, EVENT_TRACE_REAL_TIME_MODE); // LogFileMode
        Marshal.WriteInt32(p, 68, 1);                          // FlushTimer (s)
        Marshal.WriteInt32(p, 116, PropertiesSize);            // LoggerNameOffset
        return p;
    }

    private const int PropertiesSize = 120;
    private const int LogfileSize = 448;               // EVENT_TRACE_LOGFILEW auf x64
    private const int LogfileLoggerNameOffset = 8;
    private const int LogfileModeOffset = 28;
    private const int LogfileCallbackOffset = 424;
    private const ulong InvalidTrace = ulong.MaxValue;
    private const int ERROR_ACCESS_DENIED = 5;
    private const int ERROR_CANCELLED = 1223;
    private const int WNODE_FLAG_TRACED_GUID = 0x00020000;
    private const int EVENT_TRACE_REAL_TIME_MODE = 0x100;
    private const int PROCESS_TRACE_MODE_REAL_TIME = 0x100;
    private const int PROCESS_TRACE_MODE_EVENT_RECORD = 0x10000000;
    private const uint EVENT_TRACE_CONTROL_STOP = 1;
    private const uint EVENT_CONTROL_CODE_ENABLE_PROVIDER = 1;
    private const byte TRACE_LEVEL_VERBOSE = 5;
    private const ushort EVENT_HEADER_FLAG_32_BIT_HEADER = 0x20;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void EventRecordCallback(IntPtr record);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int StartTraceW(out ulong handle, string name, IntPtr properties);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int ControlTraceW(ulong handle, string name, IntPtr properties, uint control);

    [DllImport("advapi32.dll")]
    private static extern int EnableTraceEx2(ulong handle, ref Guid provider, uint control, byte level, ulong matchAny, ulong matchAll,
        uint timeout, IntPtr parameters);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern ulong OpenTraceW(IntPtr logfile);

    [DllImport("advapi32.dll")]
    private static extern int ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);

    [DllImport("advapi32.dll")]
    private static extern int CloseTrace(ulong handle);
}
