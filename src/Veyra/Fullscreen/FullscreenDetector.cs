using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Interop;
using Veyra.Monitors;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Fullscreen;

public enum FullscreenKind
{
    None,

    /// <summary>Randloses Vollbild: Fenster ohne Rahmen deckt den kompletten Monitor ab.</summary>
    Borderless,

    /// <summary>Exklusives Direct3D-Vollbild laut Shell (SHQueryUserNotificationState).</summary>
    Exclusive,
}

public readonly record struct FullscreenState(FullscreenKind Kind, IntPtr Window, string Reason)
{
    public static FullscreenState NotFullscreen(string reason) => new(FullscreenKind.None, IntPtr.Zero, reason);
}

/// <summary>
/// Ereignisbasierte Vollbild-Erkennung für genau einen Monitor.
///
/// Auslöser (kein hochfrequentes Polling):
///  • WinEvents: Vordergrundwechsel, Minimieren/Wiederherstellen, Ende von Verschieben/Größenänderung
///  • Shell-Hook (Aktivierung, "rude app" = Vollbild-Aktivierung, Fenster zerstört, Monitorwechsel) – vom Leistenfenster weitergereicht
///  • ABN_FULLSCREENAPP der Explorer-AppBar-Schnittstelle – vom Leistenfenster weitergereicht
///  • eine billige Sicherheitsprüfung alle paar Sekunden (Standard 2 s) für Fälle ohne Ereignis
///
/// Heuristik (siehe README, Abschnitt "Vollbild-Erkennung"):
///  1. Shell meldet QUNS_RUNNING_D3D_FULL_SCREEN und das Vordergrundfenster liegt auf unserem Monitor → exklusives Vollbild.
///  2. Sonst wird die Z-Reihenfolge von oben durchlaufen; das erste "echte" Anwendungsfenster auf dem Monitor entscheidet.
///     Click-through-Overlays, Tool-Fenster, unsichtbare/gecloakte Fenster und Shell-Oberflächen (Alt+Tab, Startmenü)
///     werden übersprungen. Topmost-Fenster, die kein Vollbild sind (z. B. Bild-im-Bild), werden ebenfalls übersprungen.
///  3. Ein Fenster gilt als randloses Vollbild, wenn es den gesamten Monitor überdeckt UND
///     – keine Titelleiste hat, oder
///     – sein Client-Bereich den Monitor vollständig überdeckt.
///     Maximierte Fenster mit Größenrahmen sind nie Vollbild (Ausnahme: Client deckt Monitor ab, Fenster ist im
///     Vordergrund und die Shell meldet QUNS_BUSY – z. B. Browser-Vollbild bei manchen Browsern).
///
/// Wechsel in den Vollbildzustand werden sofort gemeldet, das Verlassen erst, wenn es
/// <see cref="BehaviorSettings.FullscreenShowDelayMs"/> lang stabil ist (verhindert Flackern bei Alt+Tab und Ladebildschirmen).
/// </summary>
public sealed class FullscreenDetector : IDisposable
{
    private static readonly HashSet<string> DesktopClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW",
    };

    private static readonly HashSet<string> IgnoredShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
        "XamlExplorerHostIslandWindow", "MultitaskingViewFrame", "TaskSwitcherWnd", "ForegroundStaging",
        "Windows.UI.Core.CoreWindow", "Windows.Internal.Shell.TabProxyWindow", "SysShadow", "tooltips_class32",
        "#32768", "Xaml_WindowedPopupClass", "Shell_InputSwitchTopLevelWindow", "LockScreenBackstopFrame",
    };

    private readonly DispatcherTimer _evaluateTimer;
    private readonly DispatcherTimer _confirmLeaveTimer;
    private readonly DispatcherTimer _safetyTimer;
    private readonly List<WinEventHook> _hooks = [];

    private HashSet<string> _excludedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _forcedProcesses = new(StringComparer.OrdinalIgnoreCase);
    private FullscreenState _state = FullscreenState.NotFullscreen("Start");
    private bool _running;

    public FullscreenDetector()
    {
        // Kleines Sammelfenster: Ereignis-Salven (z. B. beim Spielstart) werden zu einer Auswertung zusammengefasst.
        _evaluateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _evaluateTimer.Tick += (_, _) =>
        {
            _evaluateTimer.Stop();
            Evaluate();
        };

        _confirmLeaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _confirmLeaveTimer.Tick += (_, _) =>
        {
            _confirmLeaveTimer.Stop();
            Report(Detect());
        };

        _safetyTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _safetyTimer.Tick += (_, _) => Evaluate();
    }

    /// <summary>Monitor, auf dem die Leiste liegt. Nur dieser wird betrachtet.</summary>
    public MonitorInfo? Monitor { get; set; }

    /// <summary>Fenster der eigenen Leiste (wird ignoriert).</summary>
    public IntPtr OwnWindow { get; set; }

    public FullscreenState State => _state;

    public event EventHandler<FullscreenState>? StateChanged;

    public event EventHandler? ForegroundChanged;

    public void Configure(BehaviorSettings behavior)
    {
        _excludedProcesses = new HashSet<string>(behavior.FullscreenExcludedProcesses, StringComparer.OrdinalIgnoreCase);
        _forcedProcesses = new HashSet<string>(behavior.FullscreenForcedProcesses, StringComparer.OrdinalIgnoreCase);
        _confirmLeaveTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, behavior.FullscreenShowDelayMs));

        _safetyTimer.Stop();
        if (behavior.FullscreenSafetyCheckIntervalMs > 0)
        {
            _safetyTimer.Interval = TimeSpan.FromMilliseconds(behavior.FullscreenSafetyCheckIntervalMs);
            if (_running) _safetyTimer.Start();
        }
    }

    public void Start()
    {
        if (_running) return;
        _running = true;

        _hooks.Add(new WinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, (_, _, _, _) =>
        {
            ForegroundChanged?.Invoke(this, EventArgs.Empty);
            RequestEvaluation();
        }));
        _hooks.Add(new WinEventHook(EVENT_SYSTEM_MOVESIZEEND, EVENT_SYSTEM_MOVESIZEEND, (_, _, _, _) => RequestEvaluation()));
        _hooks.Add(new WinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, (_, _, _, _) => RequestEvaluation()));

        if (_safetyTimer.Interval > TimeSpan.Zero) _safetyTimer.Start();
        EvaluateNow();
    }

    /// <summary>Fordert eine (gebündelte) Neubewertung an. Billig und beliebig oft aufrufbar.</summary>
    public void RequestEvaluation()
    {
        if (_running && !_evaluateTimer.IsEnabled) _evaluateTimer.Start();
    }

    public void EvaluateNow()
    {
        _evaluateTimer.Stop();
        Evaluate();
    }

    public void Dispose()
    {
        _running = false;
        _evaluateTimer.Stop();
        _confirmLeaveTimer.Stop();
        _safetyTimer.Stop();
        foreach (var hook in _hooks) hook.Dispose();
        _hooks.Clear();
    }

    private void Evaluate()
    {
        if (!_running) return;

        FullscreenState detected;
        try
        {
            detected = Detect();
        }
        catch (Exception ex)
        {
            Log.Error("Fullscreen", "Fehler bei der Vollbild-Erkennung", ex);
            return;
        }

        if (detected.Kind != FullscreenKind.None)
        {
            // Vollbild sofort melden, damit die Leiste nie sichtbar über einem Spiel liegt.
            _confirmLeaveTimer.Stop();
            Report(detected);
        }
        else if (_state.Kind == FullscreenKind.None)
        {
            _confirmLeaveTimer.Stop();
            _state = detected;
        }
        else if (!_confirmLeaveTimer.IsEnabled)
        {
            // Verlassen erst nach Bestätigung melden (Entprellung).
            _confirmLeaveTimer.Start();
        }
    }

    private void Report(FullscreenState state)
    {
        bool changed = state.Kind != _state.Kind ||
                       (state.Kind != FullscreenKind.None && state.Window != _state.Window);
        _state = state;
        if (!changed) return;

        Log.Info("Fullscreen", state.Kind == FullscreenKind.None
            ? $"Kein Vollbild ({state.Reason})"
            : $"{state.Kind} erkannt ({state.Reason}): {WindowInspector.Describe(state.Window)}");
        StateChanged?.Invoke(this, state);
    }

    private FullscreenState Detect()
    {
        var monitor = Monitor;
        if (monitor is null) return FullscreenState.NotFullscreen("kein Monitor");

        var monitorRect = monitor.Bounds;
        var foreground = GetForegroundWindow();
        int notificationState = 0;
        bool haveNotificationState = SHQueryUserNotificationState(out notificationState) == 0;

        // 1) Exklusives D3D-Vollbild – die Shell weiß das zuverlässig, aber nicht für welchen Monitor.
        if (haveNotificationState && notificationState == QUNS_RUNNING_D3D_FULL_SCREEN &&
            foreground != IntPtr.Zero && foreground != OwnWindow &&
            IsOnMonitor(foreground, monitor) && !IsExcludedProcess(foreground))
        {
            return new FullscreenState(FullscreenKind.Exclusive, foreground, "QUNS_RUNNING_D3D_FULL_SCREEN");
        }

        bool shellReportsBusy = haveNotificationState && notificationState == QUNS_BUSY;
        bool needProcessNames = _excludedProcesses.Count > 0 || _forcedProcesses.Count > 0;

        // 2) Z-Reihenfolge von oben durchlaufen.
        int guard = 0;
        for (var hwnd = GetTopWindow(IntPtr.Zero);
             hwnd != IntPtr.Zero && guard++ < 5000;
             hwnd = GetWindow(hwnd, GW_HWNDNEXT))
        {
            if (hwnd == OwnWindow || !IsWindowVisible(hwnd) || IsIconic(hwnd)) continue;

            long exStyle = WindowInspector.GetExStyle(hwnd);
            if ((exStyle & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT)) != 0) continue;

            if (!GetWindowRect(hwnd, out var rect) || rect.IsEmpty || !rect.Intersects(monitorRect)) continue;
            if (WindowInspector.IsCloaked(hwnd) || WindowInspector.IsInvisibleLayered(hwnd, exStyle)) continue;

            string className = WindowInspector.GetClassName(hwnd);
            if (DesktopClasses.Contains(className)) return FullscreenState.NotFullscreen("Desktop");
            if (IgnoredShellClasses.Contains(className)) continue;

            bool topmost = (exStyle & WS_EX_TOPMOST) != 0;
            string? process = needProcessNames ? WindowInspector.GetProcessName(hwnd) : null;

            if (process is not null && _excludedProcesses.Contains(process))
            {
                if (topmost) continue;
                return FullscreenState.NotFullscreen($"Ausnahme '{process}'");
            }

            bool fullscreen = process is not null && _forcedProcesses.Contains(process)
                ? rect.Covers(monitor.WorkArea)
                : IsBorderlessFullscreen(hwnd, rect, monitorRect, hwnd == foreground && shellReportsBusy);

            if (fullscreen)
                return new FullscreenState(FullscreenKind.Borderless, hwnd,
                    process is not null && _forcedProcesses.Contains(process) ? $"erzwungen '{process}'" : "Fenster deckt Monitor ab");

            if (topmost) continue; // z. B. Bild-im-Bild über einem Spiel
            return FullscreenState.NotFullscreen("oberstes Fenster ist kein Vollbild");
        }

        return FullscreenState.NotFullscreen("kein Fenster auf dem Monitor");
    }

    private static bool IsBorderlessFullscreen(IntPtr hwnd, RECT windowRect, RECT monitorRect, bool shellReportsBusy)
    {
        // Fenster muss den kompletten Monitor überdecken (Überstand erlaubt).
        if (!windowRect.Covers(monitorRect)) return false;

        long style = WindowInspector.GetStyle(hwnd);

        // Normales maximiertes Fenster: Größenrahmen + maximiert. Größenvergleich allein reicht hier nicht,
        // weil bei automatisch ausgeblendeter Windows-Taskleiste der Arbeitsbereich = Monitor ist.
        if (IsZoomed(hwnd) && (style & WS_THICKFRAME) != 0)
        {
            return shellReportsBusy &&
                   WindowInspector.TryGetClientScreenRect(hwnd, out var maximizedClient) &&
                   maximizedClient.Covers(monitorRect, tolerance: 1);
        }

        // Ohne Titelleiste und monitorfüllend → randloses Vollbild (Spiele, Videoplayer, Browser-F11).
        if ((style & WS_CAPTION) != WS_CAPTION) return true;

        // Mit Titelleiste nur dann, wenn der Client-Bereich den Monitor trotzdem vollständig füllt.
        return WindowInspector.TryGetClientScreenRect(hwnd, out var client) && client.Covers(monitorRect, tolerance: 1);
    }

    private static bool IsOnMonitor(IntPtr hwnd, MonitorInfo monitor)
    {
        var hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == monitor.Handle) return true;
        // Handles können sich nach Konfigurationsänderungen ändern → über die Geometrie vergleichen.
        return MonitorService.Query(hMonitor)?.Bounds.Equals(monitor.Bounds) == true;
    }

    private bool IsExcludedProcess(IntPtr hwnd)
    {
        if (_excludedProcesses.Count == 0) return false;
        var name = WindowInspector.GetProcessName(hwnd);
        return name is not null && _excludedProcesses.Contains(name);
    }
}
