using Veyra.Interop;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Fullscreen;

/// <summary>Kleine, allokationsarme Helfer zum Untersuchen fremder Top-Level-Fenster.</summary>
internal static class WindowInspector
{
    public static unsafe string GetClassName(IntPtr hwnd)
    {
        char* buffer = stackalloc char[256];
        int length = GetClassNameW(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    /// <summary>Prozessname ohne ".exe" oder null, wenn der Prozess nicht abgefragt werden darf (z. B. geschützte Anti-Cheat-Prozesse).</summary>
    public static string? GetProcessName(IntPtr hwnd)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == 0 ? null : GetProcessImagePath(pid) is { } path ? Path.GetFileNameWithoutExtension(path) : null;
    }

    /// <summary>Vollständiger Pfad der EXE eines Prozesses oder null, wenn er nicht abgefragt werden darf.</summary>
    public static unsafe string? GetProcessImagePath(uint pid)
    {
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero) return null;
        try
        {
            char* buffer = stackalloc char[1024];
            int size = 1024;
            return QueryFullProcessImageNameW(process, 0, buffer, ref size) ? new string(buffer, 0, size) : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Gecloakte Fenster (andere virtuelle Desktops, suspendierte UWP-Apps) sind unsichtbar, obwohl WS_VISIBLE gesetzt ist.</summary>
    public static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    public static long GetStyle(IntPtr hwnd) => GetWindowLongPtr(hwnd, GWL_STYLE).ToInt64();

    public static long GetExStyle(IntPtr hwnd) => GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

    /// <summary>Vollständig transparente Layered-Fenster (Alpha 0) werden von manchen Tools dauerhaft über den Bildschirm gelegt.</summary>
    public static bool IsInvisibleLayered(IntPtr hwnd, long exStyle) =>
        (exStyle & WS_EX_LAYERED) != 0 &&
        GetLayeredWindowAttributes(hwnd, out _, out byte alpha, out uint flags) &&
        (flags & LWA_ALPHA) != 0 && alpha == 0;

    public static bool TryGetClientScreenRect(IntPtr hwnd, out RECT rect)
    {
        rect = default;
        if (!GetClientRect(hwnd, out var client)) return false;
        var topLeft = new POINT { X = client.Left, Y = client.Top };
        var bottomRight = new POINT { X = client.Right, Y = client.Bottom };
        if (!ClientToScreen(hwnd, ref topLeft) || !ClientToScreen(hwnd, ref bottomRight)) return false;
        rect = new RECT(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        return true;
    }

    public static string Describe(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return "-";
        GetWindowRect(hwnd, out var rect);
        return $"0x{hwnd.ToInt64():X} class='{GetClassName(hwnd)}' process='{GetProcessName(hwnd) ?? "?"}' rect={rect}";
    }
}
