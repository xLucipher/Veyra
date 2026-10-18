using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Network;

namespace Veyra.Widgets.Network;

public sealed record InfoRow(string Label, string Value);

/// <summary>
/// Aktuelle Netzwerkverbindung: Symbol (LAN, WLAN mit Signalstufen, getrennt), optional Netzwerkname und Signalstärke.
/// Klick öffnet ein Popup mit IP-Adressen, Adapter und Geschwindigkeit. Die Daten kommen ereignisbasiert vom
/// gemeinsamen <see cref="NetworkStatusService"/>; während das Widget angehalten ist, ist dort nichts angemeldet.
/// </summary>
public sealed class NetworkWidget : ObservableObject, IBarWidget
{
    // Segoe Fluent Icons
    private const string GlyphWifi = "";
    private const string GlyphEthernet = "";
    private const string GlyphGlobe = "";
    private const string GlyphDisconnected = "";
    private static readonly string[] WifiLevels = ["", "", "", ""];

    private readonly string _entryId;
    private readonly NetworkStatusService _service;
    private NetworkView? _view;
    private NetworkSettings _network = new();
    private bool _running;

    private string _glyph = GlyphDisconnected;
    private string _levelGlyph = "";
    private double _glyphOpacity = 1;
    private string _nameText = "";
    private string _signalText = "";
    private string _toolTipText = "";
    private Brush _foreground = Brushes.White;
    private Brush _iconBrush = Brushes.White;
    private Brush _iconNormal = Brushes.White;
    private FontFamily _fontFamily = new("Segoe UI");
    private Visibility _nameVisibility = Visibility.Collapsed;
    private Visibility _signalVisibility = Visibility.Collapsed;

    public NetworkWidget(string entryId, NetworkStatusService service)
    {
        _entryId = entryId;
        _service = service;
        OpenSettingsCommand = new RelayCommand(() => _flyout?.OpenAndClose("ms-settings:network-status"));
        OpenLocationSettingsCommand = new RelayCommand(() => _flyout?.OpenAndClose("ms-settings:privacy-location"));
    }

    public FrameworkElement View => _view ??= new NetworkView { DataContext = this };

    public string Glyph { get => _glyph; private set => SetProperty(ref _glyph, value); }

    /// <summary>Nur WLAN: gefüllte Signalbögen über dem abgedunkelten Vollsymbol.</summary>
    public string LevelGlyph { get => _levelGlyph; private set => SetProperty(ref _levelGlyph, value); }

    public double GlyphOpacity { get => _glyphOpacity; private set => SetProperty(ref _glyphOpacity, value); }
    public string NameText { get => _nameText; private set => SetProperty(ref _nameText, value); }
    public string SignalText { get => _signalText; private set => SetProperty(ref _signalText, value); }
    public string ToolTipText { get => _toolTipText; private set => SetProperty(ref _toolTipText, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public Brush IconBrush { get => _iconBrush; private set => SetProperty(ref _iconBrush, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }
    public Visibility NameVisibility { get => _nameVisibility; private set => SetProperty(ref _nameVisibility, value); }
    public Visibility SignalVisibility { get => _signalVisibility; private set => SetProperty(ref _signalVisibility, value); }

    // ---- Popup ------------------------------------------------------------------------------
    private NetworkFlyout? _flyout;

    public string StatusTitle => _service.Status.IsConnected
        ? (_service.Status.NetworkName.Length > 0 ? _service.Status.NetworkName : KindName(_service.Status.Kind))
        : "Nicht verbunden";

    public string StatusText => Describe(_service.Status);
    public IReadOnlyList<InfoRow> Details { get; private set; } = [];
    public ICommand OpenSettingsCommand { get; }
    public ICommand OpenLocationSettingsCommand { get; }
    public Visibility SettingsButtonVisibility => _network.ShowSettingsButton ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RestrictedVisibility =>
        _service.Status.WifiDetailsRestricted ? Visibility.Visible : Visibility.Collapsed;

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _network = entry?.Network ?? new NetworkSettings();
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        Foreground = entry?.LabelColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        _iconNormal = entry?.IconColor is { } icon ? ColorUtil.ToBrush(icon, Colors.White) : barForeground;
        FontFamily = new FontFamily(bar.Appearance.FontFamily);
        OnPropertyChanged(nameof(SettingsButtonVisibility));
        Update();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _service.Changed += OnStatusChanged;
        _service.Acquire();
        Update();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _service.Changed -= OnStatusChanged;
        _service.Release();
    }

    public void Refresh()
    {
        if (_running) _service.Refresh();
    }

    public void Dispose()
    {
        if (_view is not null) FlyoutWindow.CloseFor(_view);
        Stop();
    }

    /// <summary>Klick auf das Widget: Popup öffnen bzw. schließen; vorher neu einlesen, damit die Details aktuell sind.</summary>
    internal void ToggleFlyout(FrameworkElement anchor)
    {
        if (_running) _service.Refresh();
        _flyout = FlyoutWindow.Toggle(anchor, () => new NetworkFlyout { DataContext = this }) as NetworkFlyout;
        if (_flyout is not null) _flyout.Closed += (_, _) => _flyout = null;
    }

    private void OnStatusChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var status = _service.Status;
        bool wifi = status.Kind == ConnectionKind.Wifi;

        Glyph = status.Kind switch
        {
            ConnectionKind.Wifi => GlyphWifi,
            ConnectionKind.Ethernet => GlyphEthernet,
            ConnectionKind.Other => GlyphGlobe,
            _ => GlyphDisconnected,
        };
        // WLAN mit bekanntem Signal: Vollsymbol abgedunkelt, darüber die erreichte Stufe (wie die Windows-Taskleiste).
        if (wifi && status.Signal is { } signal)
        {
            LevelGlyph = WifiLevels[Math.Clamp((signal - 1) / 25, 0, 3)];
            GlyphOpacity = 0.3;
        }
        else
        {
            LevelGlyph = "";
            GlyphOpacity = status.IsConnected ? 1 : 0.6;
        }
        // Verbunden, aber ohne Internetzugang: Symbol orange.
        IconBrush = status.IsConnected && status.Connectivity is Connectivity.None or Connectivity.LocalOnly or Connectivity.Constrained
            ? Units.WarnBrush
            : _iconNormal;

        NameText = !status.IsConnected ? "Getrennt" : status.NetworkName.Length > 0 ? status.NetworkName : KindName(status.Kind);
        NameVisibility = _network.ShowName ? Visibility.Visible : Visibility.Collapsed;
        SignalText = status.Signal is { } s ? $"{s} %" : "";
        SignalVisibility = _network.ShowSignal && wifi && status.Signal is not null ? Visibility.Visible : Visibility.Collapsed;

        var tip = new List<string> { status.IsConnected ? $"{KindName(status.Kind)}: {StatusTitle}" : "Keine Netzwerkverbindung" };
        if (status.IsConnected) tip.Add(Describe(status));
        if (wifi && status.Signal is { } q) tip.Add($"Signalstärke: {q} %");
        if (status.IPv4.Count > 0) tip.Add($"IPv4: {status.IPv4[0]}");
        tip.Add("Klicken für Details");
        ToolTipText = string.Join("\n", tip);

        Details = BuildDetails(status);
        OnPropertyChanged(nameof(Details));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(RestrictedVisibility));
    }

    private static List<InfoRow> BuildDetails(NetworkStatus status)
    {
        var rows = new List<InfoRow>();
        if (!status.IsConnected) return rows;

        rows.Add(new("Verbindung", KindName(status.Kind)));
        if (status.Kind == ConnectionKind.Wifi)
        {
            if (status.Signal is { } signal) rows.Add(new("Signalstärke", $"{signal} %"));
            if (status.WifiStandard.Length > 0) rows.Add(new("Standard", status.WifiStandard));
            if (status.WifiChannel is { } channel) rows.Add(new("Kanal", channel.ToString()));
            if (status.WifiSecured is { } secured) rows.Add(new("Sicherheit", secured ? "Verschlüsselt" : "Offen (unverschlüsselt)"));
            if (status.WifiRxRate > 0)
                rows.Add(new("Übertragungsrate", $"↓ {Units.FormatLinkSpeed(status.WifiRxRate * 1000.0)}   ↑ {Units.FormatLinkSpeed(status.WifiTxRate * 1000.0)}"));
        }
        rows.Add(new("Geschwindigkeit", Units.FormatLinkSpeed(status.LinkSpeed)));
        if (status.IPv4.Count > 0) rows.Add(new("IPv4-Adresse", string.Join("\n", status.IPv4)));
        if (status.IPv6.Count > 0) rows.Add(new("IPv6-Adresse", string.Join("\n", status.IPv6)));
        if (status.Gateways.Count > 0) rows.Add(new("Gateway", string.Join("\n", status.Gateways)));
        if (status.DnsServers.Count > 0) rows.Add(new("DNS-Server", string.Join("\n", status.DnsServers)));
        rows.Add(new("Adapter", status.AdapterDescription));
        if (status.AdapterAlias.Length > 0 && status.AdapterAlias != status.AdapterDescription) rows.Add(new("Name", status.AdapterAlias));
        if (status.MacAddress.Length > 0) rows.Add(new("MAC-Adresse", status.MacAddress));
        if (status.Category.Length > 0) rows.Add(new("Netzwerkprofil", status.Category + (status.Metered ? " · getaktet" : "")));
        return rows;
    }

    private static string KindName(ConnectionKind kind) => kind switch
    {
        ConnectionKind.Wifi => "WLAN",
        ConnectionKind.Ethernet => "LAN",
        ConnectionKind.Other => "Netzwerk",
        _ => "Getrennt",
    };

    private static string Describe(NetworkStatus status) => !status.IsConnected
        ? "Kein Netzwerkadapter verbunden."
        : status.Connectivity switch
        {
            Connectivity.Internet => "Verbunden, Internetzugriff",
            Connectivity.Constrained => "Verbunden, eingeschränkter Internetzugriff (z. B. Anmeldeseite)",
            Connectivity.LocalOnly => "Verbunden, kein Internetzugriff",
            Connectivity.None => "Verbunden, keine Konnektivität",
            _ => "Verbunden",
        };
}
