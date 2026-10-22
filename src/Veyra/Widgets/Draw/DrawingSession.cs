using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Interop;
using Veyra.Monitors;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Widgets.Draw;

public enum DrawTool
{
    Pen,
    Marker,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Eraser,
}

/// <summary>
/// Zeichnen auf dem Bildschirm, gemeinsam für alle Zeichnen-Widgets: je Monitor ein durchsichtiges Overlay über allem.
/// Beim Zeichnen nimmt das Overlay unter der Maus die Eingaben an und zeigt die Werkzeugleiste; danach bleibt das
/// Gezeichnete stehen, Klicks gehen aber wieder an die Fenster darunter. „Alles löschen“ entfernt alle Overlays.
/// Unabhängig von Flächen, Profilen und Vollbild – nur das Beenden von Veyra räumt sonst auf.
/// </summary>
internal static class DrawingSession
{
    private static readonly Dictionary<string, DrawOverlay> Overlays = [];
    private static readonly DispatcherTimer TopmostTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private static DrawOverlay? _active;
    private static IntPtr _previousForeground;
    private static Action<bool>? _saveOrientation;

    static DrawingSession()
    {
        // Andere Topmost-Fenster (Spiele, Overlays, die eigenen Flächen) dürfen das Gezeichnete nicht verdecken.
        TopmostTimer.Tick += (_, _) =>
        {
            // Das aktive Overlay hat den Fokus und liegt damit oben – nicht anheben, sonst verschwinden seine Tooltips.
            foreach (var overlay in Overlays.Values.Where(o => o.IsVisible && !(o == _active && o.IsActive))) overlay.BringToTop();
        };
    }

    public static readonly IReadOnlyList<Color> Palette =
    [
        Color.FromRgb(0xFF, 0x3B, 0x30), Color.FromRgb(0xFF, 0x95, 0x00),
        Color.FromRgb(0xFF, 0xD6, 0x0A), Color.FromRgb(0x34, 0xC7, 0x59),
        Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xBF, 0x5A, 0xF2),
        Colors.White, Colors.Black,
    ];

    public static readonly IReadOnlyList<double> Thicknesses = [2, 4, 8, 14];

    public static DrawTool Tool { get; set; } = DrawTool.Pen;
    public static Color Color { get; set; } = Palette[0];
    public static double Thickness { get; set; } = 4;
    public static bool ToolbarHorizontal { get; private set; }

    /// <summary>Position der Werkzeugleiste als Anteil der Monitorfläche; null = Standardplatz.</summary>
    public static Point? ToolbarPosition { get; set; }

    private static bool _clearOnExit;

    /// <summary>Zeichnen begonnen/beendet oder Gezeichnetes hinzugekommen/entfernt.</summary>
    public static event Action? Changed;

    public static bool IsDrawing => _active is not null;
    public static bool HasDrawings => Overlays.Values.Any(o => o.HasStrokes);

    public static void Toggle(DrawSettings settings, Action<bool> saveOrientation)
    {
        if (IsDrawing) Stop();
        else Start(settings, saveOrientation);
    }

    /// <summary>Zeichnen auf dem Monitor unter der Maus beginnen.</summary>
    public static void Start(DrawSettings settings, Action<bool> saveOrientation)
    {
        GetCursorPos(out var cursor);
        var point = new RECT(cursor.X, cursor.Y, cursor.X + 1, cursor.Y + 1);
        if (MonitorService.Query(MonitorFromRect(ref point, MONITOR_DEFAULTTONEAREST)) is not { } monitor) return;

        if (_active is not null) EndActive(restoreFocus: false);
        ToolbarHorizontal = settings.ToolbarHorizontal;
        _clearOnExit = settings.ClearOnExit;
        _saveOrientation = saveOrientation;
        _previousForeground = GetForegroundWindow();

        if (!Overlays.TryGetValue(monitor.DeviceName, out var overlay))
            Overlays[monitor.DeviceName] = overlay = new DrawOverlay();
        _active = overlay;
        overlay.ShowOn(monitor.Bounds);
        overlay.SetInteractive(true);
        overlay.ActivateForDrawing();
        TopmostTimer.Start();
        Changed?.Invoke();
    }

    /// <summary>Zeichnen beenden; das Gezeichnete bleibt stehen (außer „Beim Beenden alles löschen“).</summary>
    /// <param name="restoreFocus">Das vorher aktive Fenster (z. B. das Spiel) bekommt den Fokus zurück.</param>
    public static void Stop(bool restoreFocus = true)
    {
        if (_active is null) return;
        if (_clearOnExit)
        {
            ClearAll(restoreFocus);
            return;
        }
        EndActive(restoreFocus);
        UpdateTimer();
        Changed?.Invoke();
    }

    /// <summary>Alles Gezeichnete auf allen Monitoren entfernen und das Zeichnen beenden – der Bildschirm ist wieder normal.</summary>
    public static void ClearAll(bool restoreFocus = true)
    {
        if (_active is not null) EndActive(restoreFocus);
        foreach (var overlay in Overlays.Values)
        {
            overlay.Clear();
            overlay.HideWhenEmpty();
        }
        UpdateTimer();
        Changed?.Invoke();
    }

    public static void ToggleOrientation()
    {
        ToolbarHorizontal = !ToolbarHorizontal;
        ToolbarPosition = null;
        _active?.RebuildToolbar();
        _saveOrientation?.Invoke(ToolbarHorizontal);
    }

    internal static void NotifyChanged() => Changed?.Invoke();

    /// <summary>Overlay wurde geschlossen (z. B. beim Beenden von Veyra) – nicht mehr verwenden.</summary>
    internal static void Forget(DrawOverlay overlay)
    {
        foreach (var key in Overlays.Where(o => o.Value == overlay).Select(o => o.Key).ToList()) Overlays.Remove(key);
        if (_active == overlay) _active = null;
        UpdateTimer();
        Changed?.Invoke();
    }

    private static void EndActive(bool restoreFocus)
    {
        var overlay = _active!;
        _active = null;
        overlay.SetInteractive(false);
        if (!overlay.HasStrokes) overlay.HideWhenEmpty();
        if (restoreFocus && _previousForeground != IntPtr.Zero && IsWindow(_previousForeground) &&
            _previousForeground != new WindowInteropHelper(overlay).Handle)
            SetForegroundWindow(_previousForeground);
        _previousForeground = IntPtr.Zero;
    }

    internal static void UpdateTimer()
    {
        if (!Overlays.Values.Any(o => o.IsVisible)) TopmostTimer.Stop();
    }
}
