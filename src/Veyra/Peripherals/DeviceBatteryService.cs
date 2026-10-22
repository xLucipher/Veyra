using System.Windows.Threading;
using Veyra.Core;

namespace Veyra.Peripherals;

public enum BatterySource
{
    Bluetooth,
    Razer,
}

/// <param name="Key">Kennung für die Geräteauswahl ("bt:Name" bzw. "razer:Name") – bleibt über Neuverbindungen gleich.</param>
/// <param name="Charging">null = unbekannt (Bluetooth meldet das nicht).</param>
public sealed record BatteryDevice(string Key, string Name, BatterySource Source, int Percent, bool? Charging)
{
    public string SourceName => Source == BatterySource.Bluetooth ? "Bluetooth" : "Razer";

    /// <summary>Name aus der Kennung, für gewählte Geräte, die gerade nicht verbunden sind.</summary>
    public static string NameFromKey(string key) => key.IndexOf(':') is var i and >= 0 ? key[(i + 1)..] : key;
}

/// <summary>
/// Akkustände aller Quellen für alle Geräte-Akku-Widgets. Gelesen wird im Hintergrund (Razer-Abfragen dauern je Gerät
/// einige Millisekunden) einmal pro Minute, nur solange ein Widget läuft. Nur ein Lesevorgang gleichzeitig – auch
/// zusammen mit der Geräteliste im Einstellungsfenster –, damit sich keine Anfragen an ein Gerät überschneiden.
/// </summary>
public sealed class DeviceBatteryService : IDisposable
{
    private static readonly SemaphoreSlim ReadLock = new(1, 1);

    private readonly Dictionary<object, (bool Bluetooth, bool Razer)> _users = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(60) };
    private bool _reading;

    public DeviceBatteryService()
    {
        _timer.Tick += (_, _) => _ = RefreshAsync();
    }

    public IReadOnlyList<BatteryDevice> Devices { get; private set; } = [];

    /// <summary>Noch nie gelesen (Widget zeigt dann „…“ statt „keine Geräte“).</summary>
    public bool HasRead { get; private set; }

    public event EventHandler? Changed;

    public void Acquire(object owner, bool bluetooth, bool razer)
    {
        bool first = _users.Count == 0;
        bool newSource = !_users.TryGetValue(owner, out var old) || (bluetooth && !old.Bluetooth) || (razer && !old.Razer);
        _users[owner] = (bluetooth, razer);
        if (first) _timer.Start();
        if (first || newSource) _ = RefreshAsync();
    }

    public void Release(object owner)
    {
        if (_users.Remove(owner) && _users.Count == 0) _timer.Stop();
    }

    public async Task RefreshAsync()
    {
        if (_reading || _users.Count == 0) return;
        _reading = true;
        bool bluetooth = _users.Values.Any(u => u.Bluetooth);
        bool razer = _users.Values.Any(u => u.Razer);
        try
        {
            Devices = await ReadAsync(bluetooth, razer);
            HasRead = true;
        }
        finally
        {
            _reading = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Einmal alle Geräte lesen (auch für die Geräteauswahl in den Einstellungen).</summary>
    public static async Task<IReadOnlyList<BatteryDevice>> ReadAsync(bool bluetooth = true, bool razer = true)
    {
        await ReadLock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var devices = new List<BatteryDevice>();
                if (bluetooth) devices.AddRange(Safe(BluetoothBattery.Read, "Bluetooth"));
                if (razer) devices.AddRange(Safe(RazerHid.Read, "Razer"));
                return (IReadOnlyList<BatteryDevice>)devices.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            });
        }
        finally
        {
            ReadLock.Release();
        }
    }

    private static List<BatteryDevice> Safe(Func<List<BatteryDevice>> read, string source)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            Log.Warning("Battery", $"{source}: Akkustände nicht lesbar: {ex.Message}");
            return [];
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _users.Clear();
    }
}
