using System.Runtime.InteropServices;
using System.Windows.Threading;
using Veyra.Core;
using static Veyra.Audio.CoreAudioNative;

namespace Veyra.Audio;

/// <summary>Lautstärke des Standardgeräts einer Richtung. <see cref="Available"/> = false, wenn kein Gerät aktiv ist.</summary>
public sealed record VolumeState(bool Available, double Level, bool Muted, string DeviceName)
{
    public static readonly VolumeState None = new(false, 0, false, "");
}

/// <summary>
/// Systemlautstärke des Standard-Wiedergabegeräts bzw. -Mikrofons – gemeinsam für alle Lautstärke-Widgets.
/// Rein ereignisbasiert: Windows meldet jede Änderung (auch durch Tastatur, Windows oder andere Programme) über
/// IAudioEndpointVolumeCallback; ein Wechsel des Standardgeräts kommt vom <see cref="AudioDeviceService"/>.
/// Ein Endpunkt wird nur gebunden, solange ein laufendes Widget ihn angefordert hat (<see cref="Acquire"/>).
/// Alle öffentlichen Mitglieder: UI-Thread.
/// </summary>
public sealed class VolumeService : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly AudioDeviceService _devices;
    private readonly Dictionary<AudioFlow, Endpoint> _endpoints = [];
    private readonly Dictionary<AudioFlow, int> _demand = [];
    private IMMDeviceEnumerator? _enumerator;

    /// <summary>Kennzeichnet eigene Änderungen in den Benachrichtigungen (nur zur Diagnose).</summary>
    private static Guid _eventContext = Guid.NewGuid();

    public VolumeService(AudioDeviceService devices)
    {
        _devices = devices;
        _devices.Changed += OnDevicesChanged;
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
        }
        catch (Exception ex)
        {
            Log.Error("Volume", "Core Audio ist nicht verfügbar", ex);
        }
    }

    /// <summary>Lautstärke, Stummschaltung oder Standardgerät einer Richtung hat sich geändert (UI-Thread).</summary>
    public event EventHandler<AudioFlow>? Changed;

    public VolumeState Get(AudioFlow flow) => _endpoints.TryGetValue(flow, out var endpoint) ? endpoint.State : VolumeState.None;

    /// <summary>Endpunkt anfordern (Widget gestartet). Jeder Aufruf braucht ein passendes <see cref="Release"/>.</summary>
    public void Acquire(AudioFlow flow)
    {
        _demand[flow] = _demand.GetValueOrDefault(flow) + 1;
        if (!_endpoints.ContainsKey(flow)) Bind(flow);
    }

    public void Release(AudioFlow flow)
    {
        int count = _demand.GetValueOrDefault(flow) - 1;
        if (count > 0)
        {
            _demand[flow] = count;
            return;
        }
        _demand.Remove(flow);
        Unbind(flow);
    }

    /// <param name="level">0..1</param>
    public void SetLevel(AudioFlow flow, double level)
    {
        if (!_endpoints.TryGetValue(flow, out var endpoint) || endpoint.Volume is null) return;
        float value = (float)Math.Clamp(level, 0, 1);
        int hr = endpoint.Volume.SetMasterVolumeLevelScalar(value, ref _eventContext);
        if (hr != 0)
        {
            Log.Warning("Volume", $"Lautstärke konnte nicht gesetzt werden (0x{hr:X8})");
            return;
        }
        // Lautstärke ändern hebt die Stummschaltung auf – wie bei den Lautstärketasten von Windows.
        if (endpoint.State.Muted && value > 0) endpoint.Volume.SetMute(false, ref _eventContext);
        endpoint.Update(value, endpoint.State.Muted && value > 0 ? false : endpoint.State.Muted);
        Changed?.Invoke(this, flow);
    }

    /// <summary>Lautstärke um <paramref name="delta"/> Prozentpunkte ändern; das Ergebnis rastet auf ganze Vielfache ein.</summary>
    public void Step(AudioFlow flow, int delta)
    {
        var state = Get(flow);
        if (!state.Available || delta == 0) return;
        double percent = Math.Round(state.Level * 100);
        double target = delta > 0
            ? Math.Floor((percent + delta) / delta) * delta
            : Math.Ceiling((percent + delta) / -delta) * -delta;
        SetLevel(flow, Math.Clamp(target, 0, 100) / 100);
    }

    public void SetMute(AudioFlow flow, bool mute)
    {
        if (!_endpoints.TryGetValue(flow, out var endpoint) || endpoint.Volume is null) return;
        int hr = endpoint.Volume.SetMute(mute, ref _eventContext);
        if (hr != 0)
        {
            Log.Warning("Volume", $"Stummschaltung konnte nicht gesetzt werden (0x{hr:X8})");
            return;
        }
        endpoint.Update(endpoint.State.Level, mute);
        Changed?.Invoke(this, flow);
    }

    public void ToggleMute(AudioFlow flow) => SetMute(flow, !Get(flow).Muted);

    public void Dispose()
    {
        _devices.Changed -= OnDevicesChanged;
        foreach (var flow in _endpoints.Keys.ToList()) Unbind(flow);
        _demand.Clear();
        if (_enumerator is not null)
        {
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }

    /// <summary>Standardgerät gewechselt oder Gerät entfernt → betroffene Richtungen neu binden.</summary>
    private void OnDevicesChanged(object? sender, EventArgs e)
    {
        foreach (var flow in _demand.Keys.ToList())
        {
            string? defaultId = _devices.GetDevices(flow).FirstOrDefault(d => d.IsDefault)?.Id;
            if (_endpoints.TryGetValue(flow, out var endpoint) && string.Equals(endpoint.DeviceId, defaultId, StringComparison.OrdinalIgnoreCase))
            {
                endpoint.RefreshName(_devices);
                Changed?.Invoke(this, flow);
                continue;
            }
            Unbind(flow);
            Bind(flow);
        }
    }

    private void Bind(AudioFlow flow)
    {
        var endpoint = new Endpoint(flow, OnNotify);
        _endpoints[flow] = endpoint;
        if (_enumerator is not null)
        {
            try
            {
                endpoint.Open(_enumerator, _devices);
            }
            catch (Exception ex)
            {
                Log.Error("Volume", "Lautstärkeregelung konnte nicht geöffnet werden", ex);
                endpoint.Close();
            }
        }
        Changed?.Invoke(this, flow);
    }

    private void Unbind(AudioFlow flow)
    {
        if (!_endpoints.Remove(flow, out var endpoint)) return;
        endpoint.Close();
    }

    /// <summary>Kommt auf einem Core-Audio-Thread an → auf den UI-Thread weiterreichen.</summary>
    private void OnNotify(Endpoint endpoint, double level, bool muted) =>
        _dispatcher.BeginInvoke(() =>
        {
            if (!_endpoints.TryGetValue(endpoint.Flow, out var current) || current != endpoint) return; // inzwischen neu gebunden
            if (Math.Abs(endpoint.State.Level - level) < 0.0001 && endpoint.State.Muted == muted) return;
            endpoint.Update(level, muted);
            Changed?.Invoke(this, endpoint.Flow);
        });

    /// <summary>Das gebundene Standardgerät einer Richtung samt Benachrichtigung.</summary>
    private sealed class Endpoint(AudioFlow flow, Action<Endpoint, double, bool> notify)
    {
        private IMMDevice? _device;
        private Callback? _callback;

        public AudioFlow Flow { get; } = flow;
        public string? DeviceId { get; private set; }
        public IAudioEndpointVolume? Volume { get; private set; }
        public VolumeState State { get; private set; } = VolumeState.None;

        public void Open(IMMDeviceEnumerator enumerator, AudioDeviceService devices)
        {
            var dataFlow = Flow == AudioFlow.Output ? EDataFlow.Render : EDataFlow.Capture;
            if (enumerator.GetDefaultAudioEndpoint(dataFlow, ERole.Console, out var device) != 0 || device is null) return; // kein aktives Gerät
            _device = device;
            device.GetId(out var id);
            DeviceId = id;

            var iid = IID_IAudioEndpointVolume;
            int hr = device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var instance);
            if (hr != 0 || instance is not IAudioEndpointVolume volume)
            {
                Log.Warning("Volume", $"IAudioEndpointVolume nicht verfügbar (0x{hr:X8})");
                return;
            }
            Volume = volume;
            _callback = new Callback(this, notify);
            hr = volume.RegisterControlChangeNotify(_callback);
            if (hr != 0) Log.Warning("Volume", $"Lautstärkebenachrichtigung nicht verfügbar (0x{hr:X8})");

            volume.GetMasterVolumeLevelScalar(out float level);
            volume.GetMute(out bool muted);
            State = new VolumeState(true, level, muted, "");
            RefreshName(devices);
        }

        public void RefreshName(AudioDeviceService devices)
        {
            if (!State.Available) return;
            string name = devices.GetDevices(Flow).FirstOrDefault(d => string.Equals(d.Id, DeviceId, StringComparison.OrdinalIgnoreCase))?.Name ?? "";
            State = State with { DeviceName = name };
        }

        public void Update(double level, bool muted)
        {
            if (State.Available) State = State with { Level = level, Muted = muted };
        }

        public void Close()
        {
            if (Volume is not null)
            {
                if (_callback is not null) Volume.UnregisterControlChangeNotify(_callback);
                Marshal.ReleaseComObject(Volume);
                Volume = null;
            }
            if (_device is not null)
            {
                Marshal.ReleaseComObject(_device);
                _device = null;
            }
            _callback = null;
            State = VolumeState.None;
        }
    }

    [ComVisible(true)]
    private sealed class Callback(Endpoint endpoint, Action<Endpoint, double, bool> notify) : IAudioEndpointVolumeCallback
    {
        public int OnNotify(IntPtr notifyData)
        {
            if (notifyData == IntPtr.Zero) return 0;
            bool muted = Marshal.ReadInt32(notifyData, 16) != 0;
            float level = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(notifyData, 20));
            notify(endpoint, level, muted);
            return 0;
        }
    }
}
