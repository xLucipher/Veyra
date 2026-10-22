using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Peripherals;

namespace Veyra.Widgets.Metrics;

/// <summary>Ein Gerät im Popup des Geräte-Akku-Widgets.</summary>
public sealed record DeviceBatteryRow(string Name, string Detail, string Glyph, string Percent, double Fraction, Brush Brush);

/// <summary>
/// Akkustand kabelloser Geräte (Bluetooth oder eigener Funk-Empfänger): je Gerät ein Symbol (Maus, Tastatur, Headset …) mit
/// Prozentwert, bei wenig Akku orange bzw. rot – auf Wunsch mit Windows-Benachrichtigung. Klick zeigt alle gefundenen
/// Geräte. Die Werte liest der gemeinsame <see cref="DeviceBatteryService"/>.
/// </summary>
public sealed class DeviceBatteryWidget : ObservableObject, IBarWidget
{
    private readonly string _entryId;
    private readonly DeviceBatteryService _service;
    private readonly Border _view;
    private readonly StackPanel _panel;
    private readonly HashSet<string> _warned = [];
    private DeviceBatterySettings _battery = new();
    private List<string> _selected = [];
    private Brush _iconBrush = Brushes.White;
    private Brush _textBrush = Brushes.White;
    private Brush _barBrush = Brushes.White;
    private FontFamily _fontFamily = new("Segoe UI");
    private DeviceBatteryFlyout? _flyout;
    private bool _running;

    public DeviceBatteryWidget(string entryId, DeviceBatteryService service)
    {
        _entryId = entryId;
        _service = service;
        _panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _view = new Border { Child = _panel };
        _view.SetResourceReference(FrameworkElement.StyleProperty, "WidgetHoverArea");
        _view.MouseLeftButtonUp += (_, _) => ToggleFlyout();
        RefreshCommand = new RelayCommand(() => _ = _service.RefreshAsync());
    }

    public FrameworkElement View => _view;

    public ICommand RefreshCommand { get; }
    public IReadOnlyList<DeviceBatteryRow> Rows { get; private set; } = [];
    public Visibility EmptyVisibility => Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public string Sources => (_battery.Bluetooth, _battery.Receiver) switch
    {
        (false, false) => "Keine Quelle eingeschaltet (Einstellungen des Widgets)",
        _ => "Aktualisiert alle 60 Sekunden",
    };

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _battery = entry?.DeviceBattery ?? new DeviceBatterySettings();
        _selected = entry?.Devices ?? [];
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        _iconBrush = entry?.IconColor is { } icon ? ColorUtil.ToBrush(icon, Colors.White) : barForeground;
        _textBrush = entry?.LabelColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        _barBrush = ColorUtil.ToBrush(entry?.IndicatorColor ?? WidgetEntry.DefaultIndicatorColor, Colors.DeepSkyBlue);
        _fontFamily = new FontFamily(bar.Appearance.FontFamily);
        if (_running) _service.Acquire(this, _battery.Bluetooth, _battery.Receiver);
        Update();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _service.Changed += OnChanged;
        _service.Acquire(this, _battery.Bluetooth, _battery.Receiver);
        Update();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _service.Changed -= OnChanged;
        _service.Release(this);
    }

    public void Refresh()
    {
    }

    public void Dispose()
    {
        FlyoutWindow.CloseFor(_view);
        Stop();
    }

    private void OnChanged(object? sender, EventArgs e) => Update();

    private IEnumerable<BatteryDevice> Visible => _service.Devices.Where(d =>
        (d.Source == BatterySource.Bluetooth ? _battery.Bluetooth : _battery.Receiver) &&
        (_selected.Count == 0 || _selected.Contains(d.Key, StringComparer.OrdinalIgnoreCase)));

    private void Update()
    {
        var devices = Visible.ToList();
        _panel.Children.Clear();
        if (devices.Count == 0)
        {
            var glyph = Glyph("", _iconBrush);
            glyph.Opacity = 0.5;
            _panel.Children.Add(glyph);
            _panel.Children.Add(Text(_service.HasRead ? "–" : "…", _textBrush, 0.6));
        }
        for (int i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            var brush = WarnBrush(device) ?? _textBrush;
            var icon = Glyph(DeviceGlyph(device), _iconBrush);
            icon.Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0);
            _panel.Children.Add(icon);
            if (_battery.ShowNames) _panel.Children.Add(Text(device.Name, _textBrush, 0.75, maxWidth: 150));
            _panel.Children.Add(Text($"{device.Percent} %", brush, 1));
            if (device.Charging == true)
            {
                var bolt = Glyph("", brush);
                bolt.FontSize = 11;
                bolt.Margin = new Thickness(2, 0, 0, 0);
                _panel.Children.Add(bolt);
            }
        }

        _view.ToolTip = devices.Count == 0
            ? (_service.HasRead ? "Kein Gerät mit Akkustand gefunden" : "Akkustände werden gelesen …") + "\nKlicken für Details"
            : string.Join("\n", devices.Select(d => $"{d.Name}: {d.Percent} %{(d.Charging == true ? " (lädt)" : "")}")) + "\nKlicken für Details";

        Rows = _service.Devices
            .Where(d => d.Source == BatterySource.Bluetooth ? _battery.Bluetooth : _battery.Receiver)
            .Select(d => new DeviceBatteryRow(d.Name, d.Charging == true ? "Lädt" : "", DeviceGlyph(d),
                $"{d.Percent} %", d.Percent / 100.0, WarnBrush(d) ?? _barBrush))
            .ToList();
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(Sources));
        NotifyLow(devices);
    }

    /// <summary>Unter der Warnschwelle orange, unter der Hälfte davon rot – außer beim Laden.</summary>
    private Brush? WarnBrush(BatteryDevice device)
    {
        if (device.Charging == true) return null;
        if (device.Percent <= _battery.WarnLevel / 2) return Units.CriticalBrush;
        return device.Percent <= _battery.WarnLevel ? Units.WarnBrush : null;
    }

    private void NotifyLow(List<BatteryDevice> devices)
    {
        foreach (var device in devices)
        {
            bool low = device.Percent <= _battery.WarnLevel && device.Charging != true;
            if (!low)
            {
                // Erst nach etwas Erholung erneut warnen, damit ein schwankender Wert nicht dauernd meldet.
                if (device.Percent > _battery.WarnLevel + 5 || device.Charging == true) _warned.Remove(device.Key);
                continue;
            }
            if (!_battery.NotifyLow || !_warned.Add(device.Key)) continue;
            Notifier.Show("Akku fast leer", $"{device.Name}: noch {device.Percent} %", silent: false);
            Log.Info("Battery", $"Warnung: {device.Name} bei {device.Percent} %");
        }
    }

    /// <summary>Symbol nach Geräteart (Windows-Kategorie bzw. HID-Schnittstelle), sonst nach allgemeinen Wörtern im Namen.</summary>
    internal static string DeviceGlyph(BatteryDevice device)
    {
        var kind = device.Kind;
        if (kind == BatteryKind.Unknown)
        {
            bool Has(params string[] words) => words.Any(w => device.Name.Contains(w, StringComparison.OrdinalIgnoreCase));
            kind = Has("mouse", "maus") ? BatteryKind.Mouse
                : Has("keyboard", "tastatur") ? BatteryKind.Keyboard
                : Has("headset", "kopfhörer", "headphone", "buds") ? BatteryKind.Headset
                : Has("controller", "gamepad") ? BatteryKind.Controller
                : Has("pen", "stift") ? BatteryKind.Pen
                : BatteryKind.Unknown;
        }
        return kind switch
        {
            BatteryKind.Mouse => "",
            BatteryKind.Keyboard => "",
            BatteryKind.Headset => "",
            BatteryKind.Controller => "",
            BatteryKind.Pen => "",
            _ => "",
        };
    }

    private TextBlock Glyph(string glyph, Brush brush)
    {
        var text = new TextBlock { Text = glyph, FontSize = 15, Foreground = brush, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "WidgetIconFont");
        return text;
    }

    private TextBlock Text(string text, Brush brush, double opacity, double maxWidth = double.PositiveInfinity)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = brush,
            Opacity = opacity,
            FontFamily = _fontFamily,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0),
            MaxWidth = maxWidth,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
        return block;
    }

    private void ToggleFlyout()
    {
        _flyout = FlyoutWindow.Toggle(_view, () => new DeviceBatteryFlyout { DataContext = this }) as DeviceBatteryFlyout;
        if (_flyout is null) return;
        _flyout.Closed += (_, _) => _flyout = null;
        _ = _service.RefreshAsync();
    }
}
