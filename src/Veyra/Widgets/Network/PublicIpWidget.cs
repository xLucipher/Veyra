using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Network;

namespace Veyra.Widgets.Network;

/// <summary>
/// Öffentliche IPv4- und/oder IPv6-Adresse. Auf Wunsch auf der Fläche verborgen (z. B. beim Streamen) – dann steht
/// sie erst im Popup. Klick öffnet das Popup mit beiden Adressen zum Kopieren.
/// </summary>
public sealed class PublicIpWidget : ObservableObject, IBarWidget
{
    private readonly string _entryId;
    private readonly PublicIpService _service;
    private readonly Border _view;
    private readonly StackPanel _panel;
    private PublicIpSettings _ip = new();
    private Brush _labelBrush = Brushes.White;
    private Brush _valueBrush = Brushes.White;
    private FontFamily _fontFamily = new("Segoe UI");
    private PublicIpFlyout? _flyout;
    private bool _running;
    private string _copied = "";

    public PublicIpWidget(string entryId, PublicIpService service)
    {
        _entryId = entryId;
        _service = service;
        _panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _view = new Border { Child = _panel };
        _view.SetResourceReference(FrameworkElement.StyleProperty, "WidgetHoverArea");
        _view.MouseLeftButtonUp += (_, _) => ToggleFlyout();
        CopyV4Command = new RelayCommand(() => Copy(_service.Current?.IPv4, "IPv4"));
        CopyV6Command = new RelayCommand(() => Copy(_service.Current?.IPv6, "IPv6"));
        RefreshCommand = new RelayCommand(() => _ = _service.RefreshAsync());
    }

    public FrameworkElement View => _view;

    public ICommand CopyV4Command { get; }
    public ICommand CopyV6Command { get; }
    public ICommand RefreshCommand { get; }

    public string IPv4Text => _service.Current is null ? "…" : _service.Current.IPv4 ?? "Keine IPv4-Verbindung";
    public string IPv6Text => _service.Current is null ? "…" : _service.Current.IPv6 ?? "Keine IPv6-Verbindung";
    public bool HasV4 => _service.Current?.IPv4 is not null;
    public bool HasV6 => _service.Current?.IPv6 is not null;

    public string StatusText => _service.IsBusy
        ? "Wird abgefragt …"
        : _service.Current is { } current
            ? $"Stand {current.CheckedAt.ToString("HH:mm", CultureInfo.CurrentCulture)} · abgefragt über ipify.org"
            : "Noch nicht abgefragt.";

    public string CopiedText { get => _copied; private set => SetProperty(ref _copied, value); }

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _ip = entry?.PublicIp ?? new PublicIpSettings();
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        _labelBrush = entry?.IconColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        _valueBrush = entry?.LabelColor is { } value ? ColorUtil.ToBrush(value, Colors.White) : barForeground;
        _fontFamily = new FontFamily(bar.Appearance.FontFamily);
        if (_running) _service.Acquire(this, _ip.IntervalMinutes);
        Update();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _service.Changed += OnChanged;
        _service.Acquire(this, _ip.IntervalMinutes);
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

    private void Update()
    {
        _panel.Children.Clear();
        var current = _service.Current;
        bool v4 = _ip.Show is IpVersionDisplay.IPv4 or IpVersionDisplay.Both;
        bool v6 = _ip.Show is IpVersionDisplay.IPv6 or IpVersionDisplay.Both;

        var icon = new TextBlock { Text = "", FontSize = 15, Foreground = _labelBrush, VerticalAlignment = VerticalAlignment.Center };
        icon.SetResourceReference(TextBlock.FontFamilyProperty, "WidgetIconFont");
        _panel.Children.Add(icon);
        if (v4) AddAddress("IPv4", current is null ? "…" : current.IPv4);
        if (v6) AddAddress("IPv6", current is null ? "…" : current.IPv6);

        _view.ToolTip = current is null
            ? "Öffentliche IP-Adresse wird abgefragt …"
            : (_ip.Mask ? "Öffentliche IP-Adresse (verborgen)" : $"IPv4: {current.IPv4 ?? "–"}\nIPv6: {current.IPv6 ?? "–"}") + "\nKlicken zum Anzeigen und Kopieren";

        OnPropertyChanged(nameof(IPv4Text));
        OnPropertyChanged(nameof(IPv6Text));
        OnPropertyChanged(nameof(HasV4));
        OnPropertyChanged(nameof(HasV6));
        OnPropertyChanged(nameof(StatusText));
    }

    private void AddAddress(string caption, string? address)
    {
        if (_ip.ShowLabel)
        {
            _panel.Children.Add(new TextBlock
            {
                Text = caption,
                Foreground = _labelBrush,
                Opacity = 0.65,
                FontFamily = _fontFamily,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 5, 0),
            });
        }
        // Verborgen: feste Platzhalter statt der Adresse, damit auch die Länge nichts verrät.
        string text = address is null ? "–" : address == "…" ? "…" : _ip.Mask ? "•••.•••.•••" : address;
        _panel.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = _valueBrush,
            FontFamily = _fontFamily,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(_ip.ShowLabel ? 0 : 7, 0, 0, 0),
            MaxWidth = 300,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
    }

    private void Copy(string? address, string caption)
    {
        if (address is null) return;
        try
        {
            Clipboard.SetDataObject(address, copy: true);
            CopiedText = $"{caption}-Adresse kopiert.";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            CopiedText = "Zwischenablage gerade belegt – bitte noch einmal versuchen.";
        }
    }

    private void ToggleFlyout()
    {
        CopiedText = "";
        _flyout = FlyoutWindow.Toggle(_view, () => new PublicIpFlyout { DataContext = this }) as PublicIpFlyout;
        if (_flyout is not null) _flyout.Closed += (_, _) => _flyout = null;
    }
}
