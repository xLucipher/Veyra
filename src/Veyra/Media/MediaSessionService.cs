using System.Runtime.InteropServices;
using System.Windows.Threading;
using Veyra.Core;

namespace Veyra.Media;

/// <summary>Wiedergabestatus laut Windows (GlobalSystemMediaTransportControlsSessionPlaybackStatus).</summary>
public enum MediaPlayback
{
    Closed = 0,
    Opened = 1,
    Changing = 2,
    Stopped = 3,
    Playing = 4,
    Paused = 5,
}

/// <summary>
/// Eine Wiedergabequelle (Sitzung der Windows-Mediensteuerung). <see cref="HasSession"/> false = nichts läuft.
/// <see cref="Key"/> erkennt die Quelle über Titelwechsel hinweg wieder (App-ID plus laufende Nummer, falls eine App
/// mehrere Sitzungen hat, z. B. zwei Browser-Tabs); <see cref="Source"/> ist der Anzeigename („YouTube Music · Chrome“).
/// </summary>
public sealed record MediaState(
    bool HasSession,
    string Title,
    string Artist,
    string Album,
    string AppId,
    MediaPlayback Playback,
    bool CanPrevious,
    bool CanNext,
    bool CanPlayPause,
    byte[]? Thumbnail,
    string Key = "",
    string Source = "")
{
    public static readonly MediaState None = new(false, "", "", "", "", MediaPlayback.Closed, false, false, false, null);

    public bool IsPlaying => Playback == MediaPlayback.Playing;
}

/// <summary>
/// Liest Wiedergaben aus der Windows-Mediensteuerung (dieselbe Quelle wie das Lautstärke-Overlay von Windows:
/// Spotify, Browser-Tabs, Medienwiedergabe …) und steuert sie. Darin stehen nur Apps, die sich als Player anmelden –
/// Spiele und Systemklänge nicht; Sitzungen ohne Titel werden zusätzlich ausgeblendet. Die WinRT-API wird ohne
/// Zusatzpakete direkt über ihre COM-Schnittstellen angesprochen (<see cref="WinRt"/>).
///
/// Angezeigt und gesteuert wird die gewählte Quelle (<see cref="Select"/>), nicht die, die Windows gerade für aktuell
/// hält – die wechselt mit jedem Klick in eine andere App. Ohne Wahl gilt einmalig die aktuelle von Windows; danach
/// bleibt es bei ihr, bis sie verschwindet (App beendet) oder eine andere gewählt wird.
///
/// Abgefragt wird nur, solange mindestens ein Widget läuft (<see cref="Acquire"/>): einmal pro Sekunde auf einem
/// eigenen Hintergrund-Thread, das Cover nur bei einem Titelwechsel. Die Liste aller Quellen nur, solange die Auswahl
/// offen ist (<see cref="AcquireList"/>). Ergebnisse kommen über <see cref="Changed"/> auf dem UI-Thread an – nur wenn
/// sich etwas geändert hat.
/// </summary>
public sealed class MediaSessionService : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly object _lock = new();
    private readonly Queue<(int Slot, string? Key)> _commands = new();
    private readonly AutoResetEvent _wake = new(false);
    private Thread? _thread;
    private int _demand;
    private int _listDemand;
    private string? _selectedKey;
    private bool _disposed;

    /// <summary>Gewählte Quelle.</summary>
    public MediaState Current { get; private set; } = MediaState.None;

    /// <summary>Alle Quellen (nur aktuell, solange <see cref="AcquireList"/> angefordert ist).</summary>
    public IReadOnlyList<MediaState> Sessions { get; private set; } = [];

    /// <summary>Neuer Zustand (UI-Thread).</summary>
    public event EventHandler? Changed;

    public void Acquire()
    {
        lock (_lock)
        {
            _demand++;
            StartThread();
        }
    }

    public void Release()
    {
        lock (_lock)
        {
            if (_demand > 0) _demand--;
        }
        _wake.Set(); // der Thread beendet sich ohne Bedarf selbst
    }

    /// <summary>Liste aller Quellen mitlesen (Auswahl geöffnet). Jeder Aufruf braucht ein <see cref="ReleaseList"/>.</summary>
    public void AcquireList()
    {
        lock (_lock) _listDemand++;
        _wake.Set(); // sofort lesen, nicht erst in einer Sekunde
    }

    public void ReleaseList()
    {
        lock (_lock)
        {
            if (_listDemand > 0) _listDemand--;
        }
    }

    /// <summary>Diese Quelle anzeigen und steuern (bleibt gewählt, bis sie verschwindet).</summary>
    public void Select(string key)
    {
        lock (_lock) _selectedKey = key;
        _wake.Set();
    }

    /// <param name="key">Quelle aus <see cref="Sessions"/>; null = die gewählte.</param>
    public void PlayPause(string? key = null) => Enqueue(Slot.TryTogglePlayPauseAsync, key);
    public void Next(string? key = null) => Enqueue(Slot.TrySkipNextAsync, key);
    public void Previous(string? key = null) => Enqueue(Slot.TrySkipPreviousAsync, key);

    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _demand = 0;
        }
        _wake.Set();
    }

    private void Enqueue(int slot, string? key)
    {
        lock (_lock)
        {
            // Auch ohne laufenden Thread (Fläche bei Vollbild ausgeblendet, Tastenkürzel): kurz starten.
            _commands.Enqueue((slot, key));
            StartThread();
        }
        _wake.Set();
    }

    /// <summary>Nur unter <see cref="_lock"/> aufrufen.</summary>
    private void StartThread()
    {
        if (_thread is not null || _disposed) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "Veyra Media" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    // ---- Hintergrund-Thread ---------------------------------------------------------------

    /// <summary>Zuletzt gelesene Cover je Quelle (Schlüssel: Quelle + Titel), damit sie nur beim Titelwechsel neu geladen werden.</summary>
    private readonly Dictionary<string, (string TitleKey, byte[]? Data)> _thumbnails = [];

    private void Run()
    {
        IntPtr manager = IntPtr.Zero;
        try
        {
            manager = WinRt.RequestManager();
            if (manager == IntPtr.Zero) Log.Warning("Media", "Windows-Mediensteuerung nicht verfügbar");

            while (true)
            {
                (int Slot, string? Key)[] commands;
                bool keepPolling, list;
                lock (_lock)
                {
                    commands = [.. _commands];
                    _commands.Clear();
                    keepPolling = _demand > 0 && !_disposed;
                    list = keepPolling && _listDemand > 0;
                    if (!keepPolling && commands.Length == 0)
                    {
                        _thread = null;
                        return;
                    }
                }

                if (manager != IntPtr.Zero)
                {
                    if (commands.Length > 0)
                    {
                        RunCommands(manager, commands);
                        // Nach einem Befehl braucht die App einen Moment, bis der neue Zustand gemeldet wird.
                        Thread.Sleep(150);
                    }
                    if (keepPolling) Poll(manager, list);
                }

                if (!keepPolling) continue;
                _wake.WaitOne(PollInterval);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Media", "Abfrage der Mediensteuerung fehlgeschlagen", ex);
            lock (_lock) _thread = null;
        }
        finally
        {
            if (manager != IntPtr.Zero) Marshal.Release(manager);
        }
    }

    /// <summary>Alle Sitzungen mit Schlüssel. Der Aufrufer gibt die Zeiger frei (<see cref="ReleaseAll"/>).</summary>
    private static List<(string Key, string AppId, IntPtr Session)> ListSessions(IntPtr manager)
    {
        var result = new List<(string, string, IntPtr)>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in WinRt.GetSessions(manager))
        {
            string app = WinRt.GetString(session, Slot.SourceAppUserModelId);
            int n = counts.GetValueOrDefault(app);
            counts[app] = n + 1;
            result.Add(($"{app}#{n}", app, session));
        }
        return result;
    }

    private static void ReleaseAll(List<(string Key, string AppId, IntPtr Session)> sessions)
    {
        foreach (var s in sessions) Marshal.Release(s.Session);
    }

    private string? SelectedKey
    {
        get { lock (_lock) return _selectedKey; }
    }

    private void RunCommands(IntPtr manager, (int Slot, string? Key)[] commands)
    {
        var sessions = ListSessions(manager);
        try
        {
            foreach (var (slot, key) in commands)
            {
                string? target = key ?? SelectedKey;
                var session = sessions.FirstOrDefault(s => s.Key == target).Session;
                if (session == IntPtr.Zero && key is null)
                {
                    // Noch nichts gewählt (z. B. Tastenkürzel direkt nach dem Start): die aktuelle von Windows.
                    WinRt.SendCommandToCurrent(manager, slot);
                    continue;
                }
                if (session != IntPtr.Zero) WinRt.SendCommand(session, slot);
            }
        }
        finally
        {
            ReleaseAll(sessions);
        }
    }

    private void Poll(IntPtr manager, bool list)
    {
        var sessions = ListSessions(manager);
        try
        {
            // Gewählte Quelle; fehlt sie (noch keine Wahl oder App beendet), einmalig eine neue bestimmen: bevorzugt eine,
            // die gerade spielt (die aktuelle von Windows, falls sie spielt), sonst die aktuelle von Windows.
            string? selected = SelectedKey;
            if (selected is null || sessions.All(s => s.Key != selected))
            {
                var current = WinRt.GetCurrentSession(manager);
                string? windowsCurrent = null;
                if (current != IntPtr.Zero)
                {
                    windowsCurrent = sessions.FirstOrDefault(s => WinRt.SameObject(s.Session, current)).Key;
                    Marshal.Release(current);
                }
                var playing = sessions.Where(s => WinRt.GetPlaybackInfo(s.Session).Playback == MediaPlayback.Playing).Select(s => s.Key).ToList();
                string? fallback = playing.Contains(windowsCurrent ?? "") ? windowsCurrent
                    : playing.FirstOrDefault() ?? windowsCurrent ?? (sessions.Count > 0 ? sessions[0].Key : null);
                lock (_lock)
                {
                    // Inzwischen vom Benutzer gewählt? Dann nicht überschreiben.
                    if (_selectedKey == selected) _selectedKey = fallback;
                    selected = _selectedKey;
                }
            }

            var states = new List<MediaState>();
            MediaState? chosen = null;
            foreach (var (key, app, session) in sessions)
            {
                bool isSelected = key == selected;
                if (!isSelected && !list) continue;
                var state = ReadState(session, key, app);
                if (isSelected) chosen = state;
                // Nur echte Wiedergaben in die Auswahl: ohne Titel ist es meist kein Player (z. B. stummes Browser-Video).
                if (isSelected || state.Title.Length > 0 || state.Artist.Length > 0) states.Add(state);
            }
            foreach (var stale in _thumbnails.Keys.Where(k => sessions.All(s => s.Key != k)).ToList()) _thumbnails.Remove(stale);
            Publish(chosen ?? MediaState.None, list ? states : null);
        }
        finally
        {
            ReleaseAll(sessions);
        }
    }

    private MediaState ReadState(IntPtr session, string key, string app)
    {
        var (playback, canPrevious, canNext, canPlayPause) = WinRt.GetPlaybackInfo(session);
        var (title, artist, album, thumbnailRef) = WinRt.GetMediaProperties(session);
        byte[]? thumbnail;
        try
        {
            string titleKey = $"{title}\n{artist}\n{album}";
            if (_thumbnails.TryGetValue(key, out var cached) && cached.TitleKey == titleKey)
            {
                thumbnail = cached.Data;
            }
            else
            {
                thumbnail = thumbnailRef == IntPtr.Zero ? null : WinRt.ReadThumbnail(thumbnailRef);
                _thumbnails[key] = (titleKey, thumbnail);
            }
        }
        finally
        {
            if (thumbnailRef != IntPtr.Zero) Marshal.Release(thumbnailRef);
        }
        string source = MediaSourceNames.Describe(app, title);
        return new MediaState(true, title, artist, album, app, playback, canPrevious, canNext, canPlayPause, thumbnail, key, source);
    }

    private void Publish(MediaState state, List<MediaState>? sessions)
    {
        _dispatcher.BeginInvoke(() =>
        {
            // Das Cover-Array wird bis zum nächsten Titelwechsel wiederverwendet – Gleichheit per Referenz genügt.
            bool changed = state != Current;
            Current = state;
            if (sessions is not null && !sessions.SequenceEqual(Sessions))
            {
                Sessions = sessions;
                changed = true;
            }
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        });
    }

    // ---- WinRT über COM -------------------------------------------------------------------

    /// <summary>Vtable-Plätze laut Windows.Media.winmd (IInspectable belegt 0–5).</summary>
    private static class Slot
    {
        // IGlobalSystemMediaTransportControlsSession
        public const int SourceAppUserModelId = 6;
        public const int TryGetMediaPropertiesAsync = 7;
        public const int GetPlaybackInfo = 9;
        public const int TrySkipNextAsync = 16;
        public const int TrySkipPreviousAsync = 17;
        public const int TryTogglePlayPauseAsync = 20;
    }

    /// <summary>
    /// Minimaler Zugriff auf WinRT-Objekte über ihre Vtables – ohne CsWinRT bzw. Windows-SDK-Projektion. Alle
    /// Aufrufe laufen auf dem MTA-Hintergrund-Thread; die Mediensteuerungs-Objekte sind agil.
    /// </summary>
    private static unsafe class WinRt
    {
        private static readonly Guid IID_ManagerStatics = new("2050c4ee-11a0-57de-aed7-c97c70338245");
        private static readonly Guid IID_AsyncInfo = new("00000036-0000-0000-c000-000000000046");
        private static readonly Guid IID_IStream = new("0000000c-0000-0000-c000-000000000046");
        private const int MaxThumbnailBytes = 4 * 1024 * 1024;

        [DllImport("combase.dll")]
        private static extern int RoInitialize(int initType);

        [DllImport("combase.dll", CharSet = CharSet.Unicode)]
        private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern int WindowsDeleteString(IntPtr hstring);

        [DllImport("combase.dll")]
        private static extern char* WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

        [DllImport("combase.dll")]
        private static extern int RoGetActivationFactory(IntPtr classId, in Guid iid, out IntPtr factory);

        [DllImport("shcore.dll")]
        private static extern int CreateStreamOverRandomAccessStream(IntPtr randomAccessStream, in Guid iid, out IntPtr stream);

        private static void* Method(IntPtr obj, int slot) => (*(void***)obj)[slot];

        /// <summary>Getter/Methode mit einem Zeiger-Ergebnis (Objekt, HSTRING).</summary>
        private static int CallPtr(IntPtr obj, int slot, out IntPtr result)
        {
            IntPtr value;
            int hr = ((delegate* unmanaged<IntPtr, IntPtr*, int>)Method(obj, slot))(obj, &value);
            result = hr >= 0 ? value : IntPtr.Zero;
            return hr;
        }

        private static int CallInt(IntPtr obj, int slot, out int result)
        {
            int value;
            int hr = ((delegate* unmanaged<IntPtr, int*, int>)Method(obj, slot))(obj, &value);
            result = hr >= 0 ? value : 0;
            return hr;
        }

        private static bool CallBool(IntPtr obj, int slot)
        {
            byte value;
            int hr = ((delegate* unmanaged<IntPtr, byte*, int>)Method(obj, slot))(obj, &value);
            return hr >= 0 && value != 0;
        }

        public static IntPtr RequestManager()
        {
            RoInitialize(1); // MTA; S_FALSE/RPC_E_CHANGED_MODE sind hier unkritisch
            const string className = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager";
            if (WindowsCreateString(className, className.Length, out var hstring) < 0) return IntPtr.Zero;
            try
            {
                if (RoGetActivationFactory(hstring, IID_ManagerStatics, out var statics) < 0) return IntPtr.Zero;
                try
                {
                    if (CallPtr(statics, 6, out var operation) < 0) return IntPtr.Zero; // RequestAsync
                    return AwaitResult(operation);
                }
                finally
                {
                    Marshal.Release(statics);
                }
            }
            finally
            {
                WindowsDeleteString(hstring);
            }
        }

        /// <summary>Wartet auf eine IAsyncOperation&lt;T&gt; (höchstens 3 s) und gibt ihr Ergebnis zurück; gibt die Operation frei.</summary>
        private static IntPtr AwaitResult(IntPtr operation, int timeoutMs = 3000)
        {
            if (operation == IntPtr.Zero) return IntPtr.Zero;
            try
            {
                if (Marshal.QueryInterface(operation, in IID_AsyncInfo, out var info) < 0) return IntPtr.Zero;
                try
                {
                    long deadline = Environment.TickCount64 + timeoutMs;
                    int status;
                    while (CallInt(info, 7, out status) >= 0 && status == 0 && Environment.TickCount64 < deadline) // get_Status: 0 = läuft
                        Thread.Sleep(5);
                    if (status != 1) // 1 = abgeschlossen
                    {
                        if (status == 0) ((delegate* unmanaged<IntPtr, int>)Method(info, 9))(info); // Cancel
                        return IntPtr.Zero;
                    }
                }
                finally
                {
                    Marshal.Release(info);
                }
                return CallPtr(operation, 8, out var result) >= 0 ? result : IntPtr.Zero; // IAsyncOperation<T>.GetResults
            }
            finally
            {
                Marshal.Release(operation);
            }
        }

        public static IntPtr GetCurrentSession(IntPtr manager) =>
            CallPtr(manager, 6, out var session) >= 0 ? session : IntPtr.Zero;

        public static string GetString(IntPtr obj, int slot)
        {
            if (CallPtr(obj, slot, out var hstring) < 0 || hstring == IntPtr.Zero) return "";
            try
            {
                char* buffer = WindowsGetStringRawBuffer(hstring, out uint length);
                return buffer == null ? "" : new string(buffer, 0, (int)length);
            }
            finally
            {
                WindowsDeleteString(hstring);
            }
        }

        public static (MediaPlayback Playback, bool CanPrevious, bool CanNext, bool CanPlayPause) GetPlaybackInfo(IntPtr session)
        {
            if (CallPtr(session, Slot.GetPlaybackInfo, out var info) < 0 || info == IntPtr.Zero) return (MediaPlayback.Closed, false, false, false);
            try
            {
                CallInt(info, 7, out int status); // get_PlaybackStatus
                if (CallPtr(info, 6, out var controls) < 0 || controls == IntPtr.Zero) // get_Controls
                    return ((MediaPlayback)status, false, false, false);
                try
                {
                    bool playPause = CallBool(controls, 16) || CallBool(controls, 6) || CallBool(controls, 7); // Toggle, Play, Pause
                    return ((MediaPlayback)status, CallBool(controls, 13), CallBool(controls, 12), playPause);
                }
                finally
                {
                    Marshal.Release(controls);
                }
            }
            finally
            {
                Marshal.Release(info);
            }
        }

        /// <summary>Titel, Interpret, Album und – falls vorhanden – die Cover-Referenz (Aufrufer gibt sie frei).</summary>
        public static (string Title, string Artist, string Album, IntPtr Thumbnail) GetMediaProperties(IntPtr session)
        {
            if (CallPtr(session, Slot.TryGetMediaPropertiesAsync, out var operation) < 0) return ("", "", "", IntPtr.Zero);
            var properties = AwaitResult(operation);
            if (properties == IntPtr.Zero) return ("", "", "", IntPtr.Zero);
            try
            {
                string title = GetString(properties, 6);
                string artist = GetString(properties, 9);
                if (artist.Length == 0) artist = GetString(properties, 8); // Album-Interpret
                string album = GetString(properties, 10);
                CallPtr(properties, 15, out var thumbnail);
                return (title, artist, album, thumbnail);
            }
            finally
            {
                Marshal.Release(properties);
            }
        }

        /// <summary>Liest das Cover (IRandomAccessStreamReference) als Bytes; null bei Fehler oder über 4 MB.</summary>
        public static byte[]? ReadThumbnail(IntPtr reference)
        {
            if (CallPtr(reference, 6, out var operation) < 0) return null; // OpenReadAsync
            var randomAccess = AwaitResult(operation);
            if (randomAccess == IntPtr.Zero) return null;
            try
            {
                if (CreateStreamOverRandomAccessStream(randomAccess, in IID_IStream, out var streamPtr) < 0) return null;
                try
                {
                    var stream = (System.Runtime.InteropServices.ComTypes.IStream)Marshal.GetObjectForIUnknown(streamPtr);
                    try
                    {
                        using var memory = new MemoryStream();
                        var chunk = new byte[64 * 1024];
                        int read;
                        var readPtr = Marshal.AllocHGlobal(sizeof(int));
                        try
                        {
                            do
                            {
                                stream.Read(chunk, chunk.Length, readPtr);
                                read = Marshal.ReadInt32(readPtr);
                                memory.Write(chunk, 0, read);
                                if (memory.Length > MaxThumbnailBytes) return null;
                            } while (read > 0);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(readPtr);
                        }
                        return memory.Length > 0 ? memory.ToArray() : null;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(stream);
                    }
                }
                finally
                {
                    Marshal.Release(streamPtr);
                }
            }
            finally
            {
                Marshal.Release(randomAccess);
            }
        }

        public static void SendCommand(IntPtr session, int slot)
        {
            if (CallPtr(session, slot, out var operation) >= 0) AwaitResult(operation, 1000);
        }

        public static void SendCommandToCurrent(IntPtr manager, int slot)
        {
            var session = GetCurrentSession(manager);
            if (session == IntPtr.Zero) return;
            try
            {
                SendCommand(session, slot);
            }
            finally
            {
                Marshal.Release(session);
            }
        }

        /// <summary>Alle Sitzungen (IVectorView: GetAt = 6, Size = 7). Der Aufrufer gibt jede frei.</summary>
        public static List<IntPtr> GetSessions(IntPtr manager)
        {
            var result = new List<IntPtr>();
            if (CallPtr(manager, 7, out var vector) < 0 || vector == IntPtr.Zero) return result; // GetSessions
            try
            {
                if (CallInt(vector, 7, out int size) < 0) return result;
                for (uint i = 0; i < size; i++)
                {
                    IntPtr item;
                    int hr = ((delegate* unmanaged<IntPtr, uint, IntPtr*, int>)Method(vector, 6))(vector, i, &item);
                    if (hr >= 0 && item != IntPtr.Zero) result.Add(item);
                }
            }
            finally
            {
                Marshal.Release(vector);
            }
            return result;
        }

        private static readonly Guid IID_IUnknown = new("00000000-0000-0000-c000-000000000046");

        /// <summary>Dasselbe COM-Objekt? (Vergleich der IUnknown-Zeiger.)</summary>
        public static bool SameObject(IntPtr a, IntPtr b)
        {
            if (Marshal.QueryInterface(a, in IID_IUnknown, out var ua) < 0) return false;
            try
            {
                if (Marshal.QueryInterface(b, in IID_IUnknown, out var ub) < 0) return false;
                Marshal.Release(ub);
                return ua == ub;
            }
            finally
            {
                Marshal.Release(ua);
            }
        }
    }
}
