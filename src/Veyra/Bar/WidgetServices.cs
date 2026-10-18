using Veyra.Audio;
using Veyra.Network;
using Veyra.Sensors;
using Veyra.Timing;

namespace Veyra.Bar;

/// <summary>
/// Gemeinsame Datenquellen aller Leisten: Jeder Dienst wird erst beim ersten Widget erzeugt, das ihn braucht, und
/// von allen Widgets gleichen Typs (auch auf verschiedenen Leisten) geteilt – z. B. misst ein einziger
/// <see cref="NetworkTrafficMonitor"/> für alle Upload-/Download-Widgets.
/// </summary>
public sealed class WidgetServices : IDisposable
{
    private AudioDeviceService? _audio;
    private VolumeService? _volume;
    private SystemMonitor? _systemMonitor;
    private NetworkStatusService? _networkStatus;
    private NetworkTrafficMonitor? _networkTraffic;
    private TimerService? _timers;

    public AudioDeviceService Audio => _audio ??= new AudioDeviceService();
    public VolumeService Volume => _volume ??= new VolumeService(Audio);
    public SystemMonitor SystemMonitor => _systemMonitor ??= new SystemMonitor();
    public NetworkStatusService NetworkStatus => _networkStatus ??= new NetworkStatusService();
    public NetworkTrafficMonitor NetworkTraffic => _networkTraffic ??= new NetworkTrafficMonitor();
    public TimerService Timers => _timers ??= new TimerService();

    /// <summary>Zustände entfernter Timer-/Stoppuhr-Widgets verwerfen (nur falls der Dienst schon existiert).</summary>
    public void RetainTimers(IReadOnlySet<string> entryIds) => _timers?.Retain(entryIds);

    public void Dispose()
    {
        _volume?.Dispose(); // vor dem Audio-Dienst: meldet sich dort ab
        _audio?.Dispose();
        _systemMonitor?.Dispose();
        _networkStatus?.Dispose();
        _networkTraffic?.Dispose();
        _timers?.Dispose();
    }
}
