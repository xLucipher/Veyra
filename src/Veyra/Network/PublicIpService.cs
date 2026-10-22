using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Threading;
using Veyra.Core;

namespace Veyra.Network;

/// <summary>Öffentliche Adressen; null = keine (z. B. keine IPv6-Verbindung) bzw. noch nicht abgefragt.</summary>
public sealed record PublicAddresses(string? IPv4, string? IPv6, DateTime CheckedAt);

/// <summary>
/// Öffentliche IPv4- und IPv6-Adresse über ipify.org (api.ipify.org nur IPv4, api6.ipify.org nur IPv6). Gemeinsam für
/// alle Widgets: abgefragt wird nur, solange eines läuft – im kürzesten gewünschten Abstand und sofort (leicht
/// verzögert) nach einer Änderung der Netzwerkadressen, z. B. beim Verbinden eines VPN.
/// </summary>
public sealed class PublicIpService : IDisposable
{
    private readonly Dictionary<object, int> _users = [];
    private readonly DispatcherTimer _timer = new();
    private readonly DispatcherTimer _changeDelay = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private bool _busy;

    public PublicIpService()
    {
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _changeDelay.Tick += (_, _) =>
        {
            _changeDelay.Stop();
            _ = RefreshAsync();
        };
    }

    public PublicAddresses? Current { get; private set; }

    public bool IsBusy => _busy;

    public event EventHandler? Changed;

    /// <param name="intervalMinutes">Gewünschter Abstand der Abfragen; es gilt der kürzeste aller Widgets.</param>
    public void Acquire(object owner, int intervalMinutes)
    {
        bool first = _users.Count == 0;
        _users[owner] = intervalMinutes;
        _timer.Interval = TimeSpan.FromMinutes(_users.Values.Min());
        if (!first) return;
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        _timer.Start();
        // Beim Fortsetzen (z. B. nach einem Spiel) nur neu fragen, wenn der letzte Stand alt ist.
        if (Current is null || DateTime.Now - Current.CheckedAt > _timer.Interval) _ = RefreshAsync();
    }

    public void Release(object owner)
    {
        if (!_users.Remove(owner)) return;
        if (_users.Count > 0)
        {
            _timer.Interval = TimeSpan.FromMinutes(_users.Values.Min());
            return;
        }
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        _timer.Stop();
        _changeDelay.Stop();
    }

    /// <summary>Kommt auf einem Threadpool-Thread, oft als Salve → gebündelt und etwas später fragen, wenn die Route steht.</summary>
    private void OnAddressChanged(object? sender, EventArgs e) => _dispatcher.BeginInvoke(() =>
    {
        _changeDelay.Stop();
        _changeDelay.Start();
    });

    public async Task RefreshAsync()
    {
        if (_busy) return;
        _busy = true;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            var v4 = Web.GetStringAsync("https://api.ipify.org", "PublicIp");
            var v6 = Web.GetStringAsync("https://api6.ipify.org", "PublicIp");
            await Task.WhenAll(v4, v6);
            var result = new PublicAddresses(Parse(v4.Result, AddressFamily.InterNetwork), Parse(v6.Result, AddressFamily.InterNetworkV6), DateTime.Now);
            if (Current?.IPv4 != result.IPv4 || Current?.IPv6 != result.IPv6)
                Log.Info("PublicIp", $"Öffentliche Adresse: IPv4 {(result.IPv4 is null ? "–" : "erkannt")}, IPv6 {(result.IPv6 is null ? "–" : "erkannt")}");
            Current = result;
        }
        finally
        {
            _busy = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Nur eine gültige Adresse der erwarteten Familie übernehmen (keine Fehlerseiten o. Ä.).</summary>
    private static string? Parse(string? text, AddressFamily family) =>
        IPAddress.TryParse(text?.Trim(), out var address) && address.AddressFamily == family ? address.ToString() : null;

    public void Dispose()
    {
        foreach (var owner in _users.Keys.ToList()) Release(owner);
    }
}
