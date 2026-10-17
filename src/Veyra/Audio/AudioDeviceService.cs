using System.Runtime.InteropServices;
using System.Windows.Threading;
using Veyra.Core;
using static Veyra.Audio.CoreAudioNative;

namespace Veyra.Audio;

public enum AudioFlow
{
    Output,
    Input,
}

/// <summary>Endpunkt-Formfaktor laut Windows (EndpointFormFactor).</summary>
public enum AudioFormFactor
{
    RemoteNetworkDevice = 0,
    Speakers = 1,
    LineLevel = 2,
    Headphones = 3,
    Microphone = 4,
    Headset = 5,
    Handset = 6,
    UnknownDigitalPassthrough = 7,
    Spdif = 8,
    DigitalAudioDisplayDevice = 9,
    Unknown = 10,
}

public sealed record AudioDevice(string Id, string Name, AudioFlow Flow, AudioFormFactor FormFactor, bool IsDefault);

/// <summary>
/// Listet die aktiven Wiedergabe-/Aufnahmegeräte des Systems und setzt das Standardgerät.
/// Rein ereignisbasiert: Windows meldet hinzugefügte/entfernte Geräte und Standardwechsel per
/// IMMNotificationClient; ohne Änderungen läuft hier nichts. Alle öffentlichen Mitglieder: UI-Thread.
/// </summary>
public sealed class AudioDeviceService : IDisposable
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _changeDebounce;
    private readonly Dictionary<AudioFlow, IReadOnlyList<AudioDevice>> _cache = [];
    private IMMDeviceEnumerator? _enumerator;
    private NotificationClient? _client;

    public AudioDeviceService()
    {
        // Ein Gerätewechsel erzeugt mehrere Benachrichtigungen kurz hintereinander → zusammenfassen.
        _changeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _changeDebounce.Tick += (_, _) =>
        {
            _changeDebounce.Stop();
            _cache.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _client = new NotificationClient(OnNotification);
            int hr = _enumerator.RegisterEndpointNotificationCallback(_client);
            if (hr != 0) Log.Warning("Audio", $"Gerätebenachrichtigung nicht verfügbar (0x{hr:X8})");
        }
        catch (Exception ex)
        {
            Log.Error("Audio", "Core Audio ist nicht verfügbar", ex);
            _enumerator = null;
        }
    }

    /// <summary>Geräteliste oder Standardgerät hat sich geändert (UI-Thread).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<AudioDevice> GetDevices(AudioFlow flow)
    {
        if (_cache.TryGetValue(flow, out var cached)) return cached;
        var devices = Enumerate(flow);
        _cache[flow] = devices;
        return devices;
    }

    /// <summary>Setzt das Gerät als Standard für alle Rollen (Medien, Spiele, Kommunikation).</summary>
    public bool SetDefault(string deviceId)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClientComObject();
            try
            {
                int hr = 0;
                foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
                    hr |= policy.SetDefaultEndpoint(deviceId, role);
                if (hr != 0) Log.Warning("Audio", $"SetDefaultEndpoint meldet 0x{hr:X8}");
                else Log.Info("Audio", $"Standardgerät gesetzt: {deviceId}");
                return hr == 0;
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Audio", "Standardgerät konnte nicht gesetzt werden", ex);
            return false;
        }
    }

    public void Dispose()
    {
        _changeDebounce.Stop();
        if (_enumerator is not null)
        {
            if (_client is not null) _enumerator.UnregisterEndpointNotificationCallback(_client);
            Marshal.ReleaseComObject(_enumerator);
            _enumerator = null;
        }
    }

    private void OnNotification() =>
        // Kommt auf einem Core-Audio-Thread an → nur auf den UI-Thread weiterreichen.
        _dispatcher.BeginInvoke(() =>
        {
            _changeDebounce.Stop();
            _changeDebounce.Start();
        });

    private IReadOnlyList<AudioDevice> Enumerate(AudioFlow flow)
    {
        if (_enumerator is null) return [];
        var dataFlow = flow == AudioFlow.Output ? EDataFlow.Render : EDataFlow.Capture;
        string? defaultId = GetDefaultId(dataFlow);
        var result = new List<AudioDevice>();

        if (_enumerator.EnumAudioEndpoints(dataFlow, DEVICE_STATE_ACTIVE, out var collection) != 0 || collection is null)
            return result;
        try
        {
            collection.GetCount(out uint count);
            for (uint i = 0; i < count; i++)
            {
                if (collection.Item(i, out var device) != 0 || device is null) continue;
                try
                {
                    if (device.GetId(out var id) != 0 || string.IsNullOrEmpty(id)) continue;
                    ReadProperties(device, out var name, out var formFactor);
                    result.Add(new AudioDevice(id, name ?? id, flow, formFactor,
                        string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
                }
                finally
                {
                    Marshal.ReleaseComObject(device);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Audio", "Audiogeräte konnten nicht gelesen werden", ex);
        }
        finally
        {
            Marshal.ReleaseComObject(collection);
        }

        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private string? GetDefaultId(EDataFlow flow)
    {
        if (_enumerator!.GetDefaultAudioEndpoint(flow, ERole.Console, out var device) != 0 || device is null) return null;
        try
        {
            return device.GetId(out var id) == 0 ? id : null;
        }
        finally
        {
            Marshal.ReleaseComObject(device);
        }
    }

    private static void ReadProperties(IMMDevice device, out string? name, out AudioFormFactor formFactor)
    {
        name = null;
        formFactor = AudioFormFactor.Unknown;
        if (device.OpenPropertyStore(STGM_READ, out var store) != 0 || store is null) return;
        try
        {
            var key = PKEY_Device_FriendlyName;
            if (store.GetValue(ref key, out var value) == 0)
            {
                if (value.vt == VT_LPWSTR) name = Marshal.PtrToStringUni(value.pointerValue);
                PropVariantClear(ref value);
            }

            key = PKEY_AudioEndpoint_FormFactor;
            if (store.GetValue(ref key, out value) == 0)
            {
                if (value.vt == VT_UI4 && Enum.IsDefined(typeof(AudioFormFactor), (int)value.uintValue))
                    formFactor = (AudioFormFactor)value.uintValue;
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(store);
        }
    }

    [ComVisible(true)]
    private sealed class NotificationClient(Action onChange) : IMMNotificationClient
    {
        public int OnDeviceStateChanged(string deviceId, uint newState) => Notify();
        public int OnDeviceAdded(string deviceId) => Notify();
        public int OnDeviceRemoved(string deviceId) => Notify();
        public int OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) => Notify();
        public int OnPropertyValueChanged(string deviceId, PROPERTYKEY key) => 0; // sehr häufig, für uns irrelevant

        private int Notify()
        {
            onChange();
            return 0;
        }
    }
}
