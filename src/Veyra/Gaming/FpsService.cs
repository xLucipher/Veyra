using System.Diagnostics;
using System.Windows.Threading;
using Veyra.Core;
using Veyra.Interop;

namespace Veyra.Gaming;

public enum FpsState
{
    /// <summary>Misst gerade.</summary>
    Running,

    /// <summary>Gruppe fehlt.</summary>
    NoPermission,

    /// <summary>Gruppe zugewiesen, aber erst nach dem nächsten Anmelden wirksam.</summary>
    NeedsSignIn,

    /// <summary>Sitzung ließ sich aus einem anderen Grund nicht starten.</summary>
    Failed,
}

/// <param name="Fps">Bilder pro Sekunde der letzten Sekunde.</param>
/// <param name="OnePercentLow">FPS des langsamsten Prozents der Bilder der letzten 10 Sekunden.</param>
public sealed record FpsReading(string Process, double Fps, double FrameTimeMs, double OnePercentLow);

/// <summary>
/// FPS des Programms im Vordergrund für alle FPS-Widgets. Die Present-Zeitpunkte liefert <see cref="PresentTracer"/>
/// je Prozess und Swapchain; gezählt wird die Swapchain mit den meisten Bildern (das Spiel, nicht ein Overlay).
/// Gemessen wird nur, solange ein Widget läuft; zweimal pro Sekunde wird neu berechnet. Ist das eigene Fenster vorne
/// (Popup, Einstellungen), bleibt das zuletzt gemessene Programm.
/// </summary>
public sealed class FpsService : IDisposable
{
    private static readonly long Frequency = Stopwatch.Frequency;
    private readonly HashSet<object> _users = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly object _sync = new();
    private readonly Dictionary<(uint Pid, ulong Swapchain), Queue<long>> _presents = [];
    private readonly Dictionary<uint, string> _names = [];
    private readonly List<double> _history = [];
    private PresentTracer? _tracer;
    private uint _target;
    private int _historyTicks;

    public FpsService()
    {
        _timer.Tick += (_, _) => Update();
    }

    public FpsState State { get; private set; } = FpsState.Failed;

    /// <summary>null = kein Programm im Vordergrund, das gerade Bilder ausgibt.</summary>
    public FpsReading? Current { get; private set; }

    /// <summary>FPS je Sekunde der letzten Minute (älteste zuerst), solange dasselbe Programm vorne ist.</summary>
    public IReadOnlyList<double> History => _history;

    public event EventHandler? Changed;

    public void Acquire(object owner)
    {
        if (!_users.Add(owner) || _users.Count > 1) return;
        StartTracing();
    }

    public void Release(object owner)
    {
        if (!_users.Remove(owner) || _users.Count > 0) return;
        StopTracing();
    }

    /// <summary>Nach dem Freigeben der Berechtigung erneut versuchen.</summary>
    public void Retry()
    {
        if (_users.Count == 0 || State == FpsState.Running) return;
        StartTracing();
    }

    private void StartTracing()
    {
        StopTracing();
        var tracer = new PresentTracer(OnPresent);
        var result = tracer.Start();
        if (result == TraceStart.Started)
        {
            _tracer = tracer;
            State = FpsState.Running;
            _timer.Start();
        }
        else
        {
            tracer.Dispose();
            State = result == TraceStart.Failed ? FpsState.Failed
                : FpsPermission.Check() == FpsAccess.NeedsSignIn ? FpsState.NeedsSignIn : FpsState.NoPermission;
            Log.Info("Fps", $"FPS-Messung nicht möglich: {State}");
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void StopTracing()
    {
        _timer.Stop();
        _tracer?.Dispose();
        _tracer = null;
        lock (_sync) _presents.Clear();
        _history.Clear();
        Current = null;
    }

    /// <summary>Mess-Thread: Zeitpunkt merken, ältere als 10 Sekunden verwerfen.</summary>
    private void OnPresent(uint pid, ulong swapchain, long timestamp)
    {
        lock (_sync)
        {
            if (!_presents.TryGetValue((pid, swapchain), out var queue)) _presents[(pid, swapchain)] = queue = new Queue<long>();
            queue.Enqueue(timestamp);
            long oldest = timestamp - 10 * Frequency;
            while (queue.Count > 0 && queue.Peek() < oldest) queue.Dequeue();
        }
    }

    private void Update()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(foreground, out uint pid);
        if (pid != 0 && pid != (uint)Environment.ProcessId && pid != _target)
        {
            _target = pid;
            _history.Clear();
        }

        long now = Stopwatch.GetTimestamp();
        long[]? frames = null;
        lock (_sync)
        {
            // Programme ohne neue Bilder vergessen, damit die Liste nicht wächst.
            foreach (var stale in _presents.Where(p => p.Value.Count == 0 || p.Value.Last() < now - 10 * Frequency).Select(p => p.Key).ToList())
                _presents.Remove(stale);
            frames = _presents.Where(p => p.Key.Pid == _target)
                .OrderByDescending(p => p.Value.Count(t => t > now - Frequency))
                .Select(p => p.Value.ToArray())
                .FirstOrDefault();
        }

        var previous = Current;
        Current = Measure(frames, now);
        if (Current is { } reading && ++_historyTicks % 2 == 0)
        {
            _history.Add(reading.Fps);
            if (_history.Count > 60) _history.RemoveAt(0);
        }
        if (previous != Current) Changed?.Invoke(this, EventArgs.Empty);
    }

    private FpsReading? Measure(long[]? frames, long now)
    {
        // Ohne Bilder in den letzten 1,5 s gibt das Programm nichts aus (Desktop-App im Leerlauf, minimiertes Spiel).
        if (frames is null || frames.Length < 2 || frames[^1] < now - Frequency * 3 / 2) return null;

        var lastSecond = frames.Where(t => t > now - Frequency).ToArray();
        double fps;
        if (lastSecond.Length >= 2)
            fps = (lastSecond.Length - 1) * (double)Frequency / (lastSecond[^1] - lastSecond[0]);
        else
            fps = Frequency / (double)Math.Max(1, frames[^1] - frames[^2]); // unter 2 FPS

        var intervals = new double[frames.Length - 1];
        for (int i = 1; i < frames.Length; i++) intervals[i - 1] = (frames[i] - frames[i - 1]) * 1000.0 / Frequency;
        Array.Sort(intervals);
        double slowest = intervals[Math.Clamp((int)Math.Ceiling(intervals.Length * 0.99) - 1, 0, intervals.Length - 1)];

        return new FpsReading(ProcessName(_target), Math.Round(fps, 1), Math.Round(1000 / fps, 2), Math.Round(1000 / slowest, 1));
    }

    private string ProcessName(uint pid)
    {
        if (_names.TryGetValue(pid, out var name)) return name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            name = "";
        }
        if (_names.Count > 200) _names.Clear();
        return _names[pid] = name;
    }

    public void Dispose()
    {
        _users.Clear();
        StopTracing();
    }
}
