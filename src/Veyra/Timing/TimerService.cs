using System.Diagnostics;
using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Timing;

public enum TimerPhase
{
    Idle,
    Running,
    Paused,
    Finished,
}

/// <summary>Ablauf eines Timers. Zeiten laufen monoton (<see cref="Stopwatch"/>) – eine Änderung der Systemuhr verfälscht nichts.</summary>
public sealed class CountdownState
{
    private long _endTimestamp;
    private TimeSpan _pausedRemaining;

    internal CountdownState(string entryId, string timerId)
    {
        EntryId = entryId;
        TimerId = timerId;
    }

    public string EntryId { get; }
    public string TimerId { get; }
    public string Name { get; internal set; } = "";
    public TimeSpan Duration { get; internal set; }
    public TimerPhase Phase { get; private set; }

    public bool IsActive => Phase is TimerPhase.Running or TimerPhase.Paused or TimerPhase.Finished;

    public TimeSpan Remaining => Phase switch
    {
        TimerPhase.Running => TimeSpan.FromTicks(Math.Max(0, Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), _endTimestamp).Ticks)),
        TimerPhase.Paused => _pausedRemaining,
        TimerPhase.Finished => TimeSpan.Zero,
        _ => Duration,
    };

    /// <summary>Anteil der abgelaufenen Zeit 0..1.</summary>
    public double Progress => Duration <= TimeSpan.Zero ? 0 : Math.Clamp(1 - Remaining / Duration, 0, 1);

    internal long EndTimestamp => _endTimestamp;

    internal void Start(TimeSpan remaining)
    {
        _endTimestamp = Stopwatch.GetTimestamp() + (long)(remaining.TotalSeconds * Stopwatch.Frequency);
        Phase = TimerPhase.Running;
    }

    internal void Pause()
    {
        _pausedRemaining = Remaining;
        Phase = TimerPhase.Paused;
    }

    internal void Finish() => Phase = TimerPhase.Finished;

    internal void Reset() => Phase = TimerPhase.Idle;
}

public sealed record Lap(int Number, TimeSpan LapTime, TimeSpan Total);

/// <summary>Ablauf einer Stoppuhr samt Runden.</summary>
public sealed class StopwatchState
{
    private long _startTimestamp;
    private TimeSpan _accumulated;
    private readonly List<Lap> _laps = [];

    public TimerPhase Phase { get; private set; }

    public TimeSpan Elapsed => Phase == TimerPhase.Running
        ? _accumulated + Stopwatch.GetElapsedTime(_startTimestamp)
        : _accumulated;

    /// <summary>Neueste Runde zuerst.</summary>
    public IReadOnlyList<Lap> Laps => _laps;

    internal void StartPause()
    {
        if (Phase == TimerPhase.Running)
        {
            _accumulated = Elapsed;
            Phase = TimerPhase.Paused;
        }
        else
        {
            _startTimestamp = Stopwatch.GetTimestamp();
            Phase = TimerPhase.Running;
        }
    }

    internal void Lap()
    {
        var total = Elapsed;
        var previous = _laps.Count > 0 ? _laps[0].Total : TimeSpan.Zero;
        _laps.Insert(0, new Lap(_laps.Count + 1, total - previous, total));
    }

    internal void Reset()
    {
        Phase = TimerPhase.Idle;
        _accumulated = TimeSpan.Zero;
        _laps.Clear();
    }
}

/// <summary>
/// Zustand aller Timer und Stoppuhren – unabhängig von den Widget-Ansichten. Dadurch laufen Timer weiter, wenn die
/// Leiste neu aufgebaut oder bei Vollbild ausgeblendet wird, und melden ihren Ablauf trotzdem. Für den Ablauf gibt es
/// genau einen Zeitgeber, gestellt auf den nächsten Ablaufzeitpunkt; die sekündliche Anzeige aktualisieren nur
/// sichtbare Widgets selbst. Der Zustand wird nicht gespeichert (nach einem Neustart sind alle Timer zurückgesetzt).
/// </summary>
public sealed class TimerService : IDisposable
{
    private readonly DispatcherTimer _alarm;
    private readonly Dictionary<string, CountdownState> _timers = [];
    private readonly Dictionary<string, StopwatchState> _stopwatches = [];
    private readonly Dictionary<string, (bool Notify, TimerSound Sound)> _alerts = [];

    public TimerService()
    {
        _alarm = new DispatcherTimer(DispatcherPriority.Normal);
        _alarm.Tick += (_, _) => CheckFinished();
    }

    /// <summary>Zustand eines Widgets hat sich geändert (Argument: Widget-ID).</summary>
    public event EventHandler<string>? Changed;

    // ---- Timer -------------------------------------------------------------------------------

    /// <summary>
    /// Timer eines Widgets mit den aktuellen Einstellungen abgleichen: neue anlegen, Namen und Dauer übernehmen
    /// (die Dauer nur bei nicht laufenden Timern), entfernte Timer verwerfen.
    /// </summary>
    public IReadOnlyList<CountdownState> Configure(string entryId, TimerSettings settings)
    {
        _alerts[entryId] = (settings.Notify, settings.Sound);
        var result = new List<CountdownState>(settings.Timers.Count);
        foreach (var preset in settings.Timers)
        {
            string key = Key(entryId, preset.Id);
            if (!_timers.TryGetValue(key, out var state))
            {
                state = new CountdownState(entryId, preset.Id);
                _timers[key] = state;
            }
            state.Name = preset.Name;
            var duration = TimeSpan.FromSeconds(preset.DurationSeconds);
            if (state.Phase == TimerPhase.Idle || state.Duration == TimeSpan.Zero) state.Duration = duration;
            result.Add(state);
        }
        foreach (var removed in _timers.Where(t => t.Value.EntryId == entryId && settings.Timers.All(p => p.Id != t.Value.TimerId))
                     .Select(t => t.Key).ToList())
            _timers.Remove(removed);
        ScheduleAlarm();
        return result;
    }

    public void Start(CountdownState timer)
    {
        if (timer.Phase == TimerPhase.Running) return;
        timer.Start(timer.Phase == TimerPhase.Paused ? timer.Remaining : timer.Duration);
        Log.Info("Timer", $"„{timer.Name}“ gestartet ({timer.Remaining:hh\\:mm\\:ss})");
        OnTimerChanged(timer);
    }

    public void Pause(CountdownState timer)
    {
        if (timer.Phase != TimerPhase.Running) return;
        timer.Pause();
        OnTimerChanged(timer);
    }

    /// <summary>Zurück auf die volle Dauer (auch nach Ablauf: Alarm quittieren).</summary>
    public void Reset(CountdownState timer)
    {
        if (timer.Phase == TimerPhase.Idle) return;
        timer.Reset();
        OnTimerChanged(timer);
    }

    /// <summary>Dauer eines nicht laufenden Timers ändern (nur Laufzeit – gespeichert wird über die Einstellungen).</summary>
    public void SetDuration(CountdownState timer, TimeSpan duration)
    {
        timer.Duration = duration;
        if (timer.Phase == TimerPhase.Finished) timer.Reset();
        OnTimerChanged(timer);
    }

    // ---- Stoppuhren --------------------------------------------------------------------------

    public StopwatchState GetStopwatch(string entryId)
    {
        if (!_stopwatches.TryGetValue(entryId, out var state))
        {
            state = new StopwatchState();
            _stopwatches[entryId] = state;
        }
        return state;
    }

    public void StartPause(string entryId)
    {
        GetStopwatch(entryId).StartPause();
        Changed?.Invoke(this, entryId);
    }

    public void Lap(string entryId)
    {
        var state = GetStopwatch(entryId);
        if (state.Phase != TimerPhase.Running) return;
        state.Lap();
        Changed?.Invoke(this, entryId);
    }

    public void ResetStopwatch(string entryId)
    {
        GetStopwatch(entryId).Reset();
        Changed?.Invoke(this, entryId);
    }

    // ---- Verwaltung --------------------------------------------------------------------------

    /// <summary>Zustände entfernter Widgets verwerfen – ein laufender Timer eines gelöschten Widgets meldet sich dann nicht mehr.</summary>
    public void Retain(IReadOnlySet<string> entryIds)
    {
        foreach (var key in _timers.Where(t => !entryIds.Contains(t.Value.EntryId)).Select(t => t.Key).ToList())
            _timers.Remove(key);
        foreach (var key in _stopwatches.Keys.Where(k => !entryIds.Contains(k)).ToList())
            _stopwatches.Remove(key);
        foreach (var key in _alerts.Keys.Where(k => !entryIds.Contains(k)).ToList())
            _alerts.Remove(key);
        ScheduleAlarm();
    }

    public void Dispose() => _alarm.Stop();

    private static string Key(string entryId, string timerId) => entryId + "/" + timerId;

    private void OnTimerChanged(CountdownState timer)
    {
        ScheduleAlarm();
        Changed?.Invoke(this, timer.EntryId);
    }

    /// <summary>Genau ein Zeitgeber auf den nächsten Ablaufzeitpunkt (kein sekündliches Abfragen).</summary>
    private void ScheduleAlarm()
    {
        _alarm.Stop();
        var next = _timers.Values.Where(t => t.Phase == TimerPhase.Running).MinBy(t => t.EndTimestamp);
        if (next is null) return;
        var due = next.Remaining;
        _alarm.Interval = due <= TimeSpan.Zero ? TimeSpan.FromMilliseconds(1) : due + TimeSpan.FromMilliseconds(5);
        _alarm.Start();
    }

    private void CheckFinished()
    {
        _alarm.Stop();
        foreach (var timer in _timers.Values.Where(t => t.Phase == TimerPhase.Running && t.Remaining <= TimeSpan.Zero).ToList())
        {
            timer.Finish();
            Log.Info("Timer", $"„{timer.Name}“ abgelaufen");
            var (notify, sound) = _alerts.GetValueOrDefault(timer.EntryId, (true, TimerSound.Alarm));
            if (notify)
                Notifier.Show(timer.Name, $"Der Timer ({FormatDuration(timer.Duration)}) ist abgelaufen.", silent: sound != TimerSound.None);
            Notifier.Play(sound);
            Changed?.Invoke(this, timer.EntryId);
        }
        ScheduleAlarm();
    }

    /// <summary>"5 min", "1 h 30 min", "45 s".</summary>
    public static string FormatDuration(TimeSpan duration)
    {
        var parts = new List<string>(3);
        if (duration.TotalHours >= 1) parts.Add($"{(int)duration.TotalHours} h");
        if (duration.Minutes > 0) parts.Add($"{duration.Minutes} min");
        if (duration.Seconds > 0 || parts.Count == 0) parts.Add($"{duration.Seconds} s");
        return string.Join(" ", parts);
    }

    /// <summary>Restzeit für die Anzeige: "4:59", "1:04:59" – aufgerundet, damit "0:00" erst beim Ablauf erscheint.</summary>
    public static string FormatRemaining(TimeSpan remaining)
    {
        long seconds = (long)Math.Ceiling(Math.Max(0, remaining.TotalSeconds) - 0.0005);
        long h = seconds / 3600, m = seconds / 60 % 60, s = seconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
    }

    /// <summary>
    /// Eingabe einer Dauer: "90" (Minuten), "5:30" (min:s), "1:30:00" (h:min:s) oder mit Einheiten wie "1h 30m", "45s".
    /// </summary>
    public static bool TryParseDuration(string? text, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().ToLowerInvariant().Replace(',', '.');

        if (text.Contains(':'))
        {
            var parts = text.Split(':');
            if (parts.Length is < 2 or > 3 || parts.Any(p => !int.TryParse(p, out int v) || v < 0)) return false;
            var values = parts.Select(int.Parse).ToArray();
            duration = values.Length == 3
                ? new TimeSpan(values[0], values[1], values[2])
                : new TimeSpan(0, values[0], values[1]);
        }
        else if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double minutes))
        {
            duration = TimeSpan.FromMinutes(minutes);
        }
        else
        {
            var match = System.Text.RegularExpressions.Regex.Match(text,
                @"^\s*(?:(\d+(?:\.\d+)?)\s*(?:h|std|stunden?)\s*)?(?:(\d+(?:\.\d+)?)\s*(?:m|min|minuten?)\s*)?(?:(\d+)\s*(?:s|sek|sekunden?)\s*)?$");
            if (!match.Success || match.Length == 0 || text.Trim().Length == 0) return false;
            double h = match.Groups[1].Success ? double.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            double m = match.Groups[2].Success ? double.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            double s = match.Groups[3].Success ? double.Parse(match.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
            duration = TimeSpan.FromSeconds(h * 3600 + m * 60 + s);
        }

        duration = TimeSpan.FromSeconds(Math.Round(duration.TotalSeconds));
        return duration >= TimeSpan.FromSeconds(1) && duration.TotalSeconds <= TimerPreset.MaxDurationSeconds;
    }
}
