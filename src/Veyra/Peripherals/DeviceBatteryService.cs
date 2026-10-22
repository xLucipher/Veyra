using System.Windows.Threading;
using Veyra.Core;

namespace Veyra.Peripherals;

public enum BatterySource
{
    Bluetooth,
    Receiver,
}

/// <summary>Geräteart für das Symbol – laut Windows bzw. den HID-Schnittstellen des Geräts.</summary>
public enum BatteryKind
{
    Unknown,
    Mouse,
    Keyboard,
    Headset,
    Controller,
    Pen,
}

/// <param name="Key">Kennung für die Geräteauswahl ("bt:Name" bzw. "rx:Name") – bleibt über Neuverbindungen gleich.</param>
/// <param name="Charging">null = unbekannt (Bluetooth meldet das nicht).</param>
public sealed record BatteryDevice(string Key, string Name, BatterySource Source, int Percent, bool? Charging, BatteryKind Kind = BatteryKind.Unknown)
{
    /// <summary>Name aus der Kennung, für gewählte Geräte, die gerade nicht verbunden sind.</summary>
    public static string NameFromKey(string key) => key.IndexOf(':') is var i and >= 0 ? key[(i + 1)..] : key;
}

/// <summary>
/// Akkustände aller Quellen für alle Geräte-Akku-Widgets. Gelesen wird im Hintergrund (Abfragen über Funk-Empfänger dauern je Gerät
/// einige Millisekunden) einmal pro Minute, nur solange ein Widget läuft. Nur ein Lesevorgang gleichzeitig – auch
/// zusammen mit der Geräteliste im Einstellungsfenster –, damit sich keine Anfragen an ein Gerät überschneiden.
/// </summary>
public sealed class DeviceBatteryService : IDisposable
{
    private static readonly SemaphoreSlim ReadLock = new(1, 1);

    private readonly Dictionary<object, (bool Bluetooth, bool Receiver)> _users = [];
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

    public void Acquire(object owner, bool bluetooth, bool receiver)
    {
        bool first = _users.Count == 0;
        bool newSource = !_users.TryGetValue(owner, out var old) || (bluetooth && !old.Bluetooth) || (receiver && !old.Receiver);
        _users[owner] = (bluetooth, receiver);
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
        bool receiver = _users.Values.Any(u => u.Receiver);
        try
        {
            Devices = await ReadAsync(bluetooth, receiver);
            HasRead = true;
        }
        finally
        {
            _reading = false;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Einmal alle Geräte lesen (auch für die Geräteauswahl in den Einstellungen).</summary>
    public static async Task<IReadOnlyList<BatteryDevice>> ReadAsync(bool bluetooth = true, bool receiver = true)
    {
        await ReadLock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var devices = new List<BatteryDevice>();
                if (bluetooth) devices.AddRange(Safe(BluetoothBattery.Read, "Bluetooth"));
                if (receiver) devices.AddRange(Safe(ReceiverHid.Read, "Funk-Empfänger"));
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
