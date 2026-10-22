using System.Runtime.InteropServices;
using Veyra.Core;

namespace Veyra.Interop;

[ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
internal class TaskbarListComObject;

[ComImport, Guid("602D4995-B13A-429b-A66E-1935E44F4317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList2
{
    // ITaskbarList
    void HrInit();
    void AddTab(IntPtr hwnd);
    void DeleteTab(IntPtr hwnd);
    void ActivateTab(IntPtr hwnd);
    void SetActiveAlt(IntPtr hwnd);

    // ITaskbarList2
    void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
}

/// <summary>
/// Meldet der Windows-Taskleiste ein Fenster als Vollbild (ITaskbarList2::MarkFullscreenWindow). Ist es im Vordergrund,
/// tritt die Taskleiste dann wie bei jedem Vollbildspiel zurück – auch wenn das Fenster den Monitor nicht ganz abdeckt.
/// </summary>
internal static class TaskbarList
{
    private static ITaskbarList2? _instance;

    public static bool MarkFullscreen(IntPtr hwnd, bool fullscreen)
    {
        try
        {
            if (_instance is null)
            {
                var instance = (ITaskbarList2)new TaskbarListComObject();
                instance.HrInit();
                _instance = instance;
            }
            _instance.MarkFullscreenWindow(hwnd, fullscreen);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Taskbar", "MarkFullscreenWindow fehlgeschlagen", ex);
            _instance = null;
            return false;
        }
    }
}
