using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Veyra.Core;
using static Veyra.Audio.CoreAudioNative;

namespace Veyra.Audio;

/// <summary>
/// Eine App im Mixer: alle Audio-Sitzungen desselben Programms (z. B. mehrere Browser-Prozesse) zusammengefasst.
/// Setzen von <see cref="Level"/> bzw. <see cref="Muted"/> wirkt sofort auf alle Sitzungen.
/// </summary>
public sealed class AudioApp : ObservableObject
{
    private readonly AudioSessionMixer _owner;
    private double _level;
    private bool _muted;

    internal AudioApp(AudioSessionMixer owner, string key, string name, ImageSource? icon, bool isSystemSounds)
    {
        _owner = owner;
        Key = key;
        Name = name;
        Icon = icon;
        IsSystemSounds = isSystemSounds;
        ToggleMuteCommand = new RelayCommand(() => Muted = !Muted);
    }

    internal string Key { get; }
    internal List<AudioSessionMixer.Session> Sessions { get; } = [];

    public string Name { get; }
    public ImageSource? Icon { get; }
    public bool IsSystemSounds { get; }
    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Ersatzsymbol ohne EXE-Symbol: Glocke für Systemklänge, sonst Lautsprecher.</summary>
    public string FallbackGlyph => IsSystemSounds ? "" : "";
    public Visibility FallbackVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;

    public System.Windows.Input.ICommand ToggleMuteCommand { get; }

    /// <summary>Lautstärke 0–100 relativ zur Gerätelautstärke (wie im Windows-Mixer).</summary>
    public double Level
    {
        get => Math.Round(_level * 100);
        set
        {
            double level = Math.Clamp(value / 100, 0, 1);
            if (Math.Abs(level - _level) < 0.004) return;
            _level = level;
            _owner.SetLevel(this, level);
            OnPropertyChanged();
        }
    }

    public bool Muted
    {
        get => _muted;
        set
        {
            if (value == _muted) return;
            _muted = value;
            _owner.SetMute(this, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MuteGlyph));
        }
    }

    public string MuteGlyph => _muted ? "" : "";

    /// <summary>Neuer Stand aus den Sitzungen (Benachrichtigung von Windows oder der App selbst).</summary>
    internal void Sync()
    {
        if (Sessions.Count == 0) return;
        var first = Sessions[0];
        double level = first.ReadLevel();
        bool muted = Sessions.All(s => s.ReadMute());
        if (Math.Abs(level - _level) >= 0.004)
        {
            _level = level;
            OnPropertyChanged(nameof(Level));
        }
        if (muted != _muted)
        {
            _muted = muted;
            OnPropertyChanged(nameof(Muted));
            OnPropertyChanged(nameof(MuteGlyph));
        }
    }
}

/// <summary>
/// Lautstärke je App (Audio-Sitzungen des Standard-Wiedergabegeräts, wie im Windows-Lautstärkemixer). Wird nur
/// erzeugt, solange das Lautstärke-Popup offen ist. Ereignisbasiert: neue Sitzungen meldet IAudioSessionNotification,
/// Lautstärke- und Statusänderungen je Sitzung IAudioSessionEvents. Alle öffentlichen Mitglieder: UI-Thread.
/// Nur für die Wiedergabe: Beim Mikrofon wirken Pegel und Stummschaltung je Aufnahme-Sitzung in der Praxis auf das
/// ganze Gerät, deshalb gibt es dort keine Regler je App.
/// </summary>
public sealed class AudioSessionMixer : IDisposable
{
    private static Guid _eventContext = Guid.NewGuid();
    private static readonly Dictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _device;
    private IAudioSessionManager2? _manager;
    private SessionCreatedSink? _createdSink;
    private bool _disposed;

    public AudioSessionMixer()
    {
        try
        {
            Open();
        }
        catch (Exception ex)
        {
            Log.Error("Volume", "Audio-Sitzungen konnten nicht gelesen werden", ex);
        }
    }

    /// <summary>Apps mit Audio-Sitzung, Systemklänge zuerst, dann alphabetisch.</summary>
    public ObservableCollection<AudioApp> Apps { get; } = [];

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var session in Apps.SelectMany(a => a.Sessions).ToList()) session.Close();
        Apps.Clear();
        if (_manager is not null)
        {
            if (_createdSink is not null) _manager.UnregisterSessionNotification(_createdSink);
            Marshal.ReleaseComObject(_manager);
            _manager = null;
        }
        if (_device is not null) Marshal.ReleaseComObject(_device);
        if (_enumerator is not null) Marshal.ReleaseComObject(_enumerator);
        _device = null;
        _enumerator = null;
    }

    private void Open()
    {
        _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Console, out _device) != 0 || _device is null) return;

        var iid = IID_IAudioSessionManager2;
        if (_device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var instance) != 0 || instance is not IAudioSessionManager2 manager) return;
        _manager = manager;

        // Erst anmelden, dann aufzählen: so geht keine Sitzung verloren, die genau dazwischen entsteht.
        _createdSink = new SessionCreatedSink(control => _dispatcher.BeginInvoke(() => Add(control)));
        int hr = manager.RegisterSessionNotification(_createdSink);
        if (hr != 0) Log.Warning("Volume", $"Benachrichtigung über neue Audio-Sitzungen nicht verfügbar (0x{hr:X8})");

        if (manager.GetSessionEnumerator(out var sessions) != 0 || sessions is null) return;
        try
        {
            sessions.GetCount(out int count);
            for (int i = 0; i < count; i++)
                if (sessions.GetSession(i, out var control) == 0 && control is not null) Add(control);
        }
        finally
        {
            Marshal.ReleaseComObject(sessions);
        }
    }

    private void Add(IAudioSessionControl2 control)
    {
        if (_disposed || control.GetState(out int state) != 0 || state == AudioSessionStateExpired)
        {
            Marshal.ReleaseComObject(control);
            return;
        }
        // Dieselbe Sitzung kann über Aufzählung und Benachrichtigung doppelt ankommen.
        control.GetSessionInstanceIdentifier(out var instanceId);
        if (instanceId is not null && Apps.SelectMany(a => a.Sessions).Any(s => s.InstanceId == instanceId))
        {
            Marshal.ReleaseComObject(control);
            return;
        }

        bool system = control.IsSystemSoundsSession() == 0;
        control.GetProcessId(out uint pid);
        string? path = system ? null : Fullscreen.WindowInspector.GetProcessImagePath(pid);
        string key = system ? "system" : path ?? $"pid:{pid}";

        var app = Apps.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (app is null)
        {
            control.GetDisplayName(out var displayName);
            app = new AudioApp(this, key, ResolveName(displayName, system, pid, path), system ? null : LoadIcon(path), system);
            int index = 0;
            while (index < Apps.Count && Compare(Apps[index], app) < 0) index++;
            Apps.Insert(index, app);
        }

        var session = new Session(this, app, control, instanceId);
        app.Sessions.Add(session);
        app.Sync();
    }

    private static int Compare(AudioApp a, AudioApp b) =>
        a.IsSystemSounds != b.IsSystemSounds ? (a.IsSystemSounds ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);

    private void Remove(Session session)
    {
        session.Close();
        var app = session.App;
        app.Sessions.Remove(session);
        if (app.Sessions.Count == 0) Apps.Remove(app);
    }

    internal void SetLevel(AudioApp app, double level)
    {
        foreach (var session in app.Sessions) session.Volume?.SetMasterVolume((float)level, ref _eventContext);
    }

    internal void SetMute(AudioApp app, bool mute)
    {
        foreach (var session in app.Sessions) session.Volume?.SetMute(mute, ref _eventContext);
    }

    /// <summary>Anzeigename der Sitzung; sonst die Dateibeschreibung der EXE ("Google Chrome"), sonst der Prozessname.</summary>
    private static string ResolveName(string? displayName, bool system, uint pid, string? path)
    {
        if (system) return "Systemsounds";
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            // Ressourcenverweise wie "@%SystemRoot%\System32\AudioSrv.Dll,-202" auflösen.
            if (!displayName.StartsWith('@')) return displayName.Trim();
            var buffer = new StringBuilder(260);
            if (SHLoadIndirectString(displayName, buffer, buffer.Capacity, IntPtr.Zero) == 0 && buffer.Length > 0) return buffer.ToString();
        }
        if (path is not null)
        {
            try
            {
                var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
            catch (Exception)
            {
                // Datei nicht lesbar – Prozessname genügt.
            }
            return Path.GetFileNameWithoutExtension(path);
        }
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return "Unbekannte App";
        }
    }

    /// <summary>Kleines Symbol der EXE (zwischengespeichert, damit wiederholtes Öffnen nichts neu lädt).</summary>
    private static ImageSource? LoadIcon(string? path)
    {
        if (path is null) return null;
        if (IconCache.TryGetValue(path, out var cached)) return cached;
        ImageSource? image = null;
        try
        {
            if (ExtractIconExW(path, 0, out var large, out var small, 1) > 0)
            {
                var handle = large != IntPtr.Zero ? large : small;
                if (handle != IntPtr.Zero)
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    image = source;
                }
                if (large != IntPtr.Zero) DestroyIcon(large);
                if (small != IntPtr.Zero) DestroyIcon(small);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("Volume", $"Symbol von {path} nicht lesbar: {ex.Message}");
        }
        IconCache[path] = image;
        return image;
    }

    /// <summary>Eine Audio-Sitzung samt Ereignis-Anmeldung.</summary>
    internal sealed class Session
    {
        private readonly AudioSessionMixer _mixer;
        private IAudioSessionControl2? _control;
        private SessionEventsSink? _events;

        public Session(AudioSessionMixer mixer, AudioApp app, IAudioSessionControl2 control, string? instanceId)
        {
            _mixer = mixer;
            App = app;
            InstanceId = instanceId;
            _control = control;
            Volume = control as ISimpleAudioVolume;
            _events = new SessionEventsSink(this);
            control.RegisterAudioSessionNotification(_events);
        }

        public AudioApp App { get; }
        public string? InstanceId { get; }
        public ISimpleAudioVolume? Volume { get; private set; }

        public double ReadLevel() => Volume?.GetMasterVolume(out float level) == 0 ? level : 0;
        public bool ReadMute() => Volume?.GetMute(out bool mute) == 0 && mute;

        // Rückrufe kommen auf Core-Audio-Threads an → auf den UI-Thread weiterreichen.
        internal void OnChanged() => _mixer._dispatcher.BeginInvoke(() =>
        {
            if (_control is not null) App.Sync();
        });

        internal void OnEnded() => _mixer._dispatcher.BeginInvoke(() =>
        {
            if (_control is not null && !_mixer._disposed) _mixer.Remove(this);
        });

        public void Close()
        {
            if (_control is null) return;
            if (_events is not null) _control.UnregisterAudioSessionNotification(_events);
            Marshal.ReleaseComObject(_control); // gibt auch die ISimpleAudioVolume-Sicht frei (dasselbe Objekt)
            _control = null;
            _events = null;
            Volume = null;
        }
    }

    [ComVisible(true)]
    private sealed class SessionCreatedSink(Action<IAudioSessionControl2> created) : IAudioSessionNotification
    {
        public int OnSessionCreated(IAudioSessionControl2 newSession)
        {
            created(newSession);
            return 0;
        }
    }

    [ComVisible(true)]
    private sealed class SessionEventsSink(Session session) : IAudioSessionEvents
    {
        public int OnDisplayNameChanged(string name, ref Guid eventContext) => 0;
        public int OnIconPathChanged(string path, ref Guid eventContext) => 0;

        public int OnSimpleVolumeChanged(float level, bool mute, ref Guid eventContext)
        {
            session.OnChanged();
            return 0;
        }

        public int OnChannelVolumeChanged(uint channelCount, IntPtr channelVolumes, uint changedChannel, ref Guid eventContext) => 0;
        public int OnGroupingParamChanged(ref Guid groupingParam, ref Guid eventContext) => 0;

        public int OnStateChanged(int state)
        {
            if (state == AudioSessionStateExpired) session.OnEnded();
            return 0;
        }

        public int OnSessionDisconnected(int reason)
        {
            session.OnEnded();
            return 0;
        }
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int SHLoadIndirectString(string source, StringBuilder output, int outputSize, IntPtr reserved);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
