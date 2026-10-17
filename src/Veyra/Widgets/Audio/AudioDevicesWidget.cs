using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Veyra.Audio;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Widgets.Audio;

public sealed record AudioButtonItem(
    string IconKey,
    string Label,
    Visibility IconVisibility,
    Visibility LabelVisibility,
    string ToolTip,
    bool IsDefault,
    ICommand Command);

/// <summary>
/// Ein Button je aktivem Wiedergabegerät bzw. Mikrofon des Systems. Klick = als Standardgerät setzen,
/// das aktuelle Standardgerät ist hervorgehoben. Funktioniert auf jedem Rechner, da die Geräte von
/// Windows gelesen werden; Ein-/Ausblenden, Symbol und Kurzname sind je Gerät einstellbar.
/// </summary>
public sealed class AudioDevicesWidget : ObservableObject, IBarWidget
{
    private readonly AudioFlow _flow;
    private readonly AudioDeviceService _service;
    private AudioDevicesView? _view;
    private AudioSettings _audio = new();
    private bool _running;
    private Brush _foreground = Brushes.White;
    private FontFamily _fontFamily = new("Segoe UI");

    private readonly string _entryId;
    private HashSet<string> _selectedDevices = new(StringComparer.OrdinalIgnoreCase);

    public AudioDevicesWidget(string entryId, AudioFlow flow, AudioDeviceService service)
    {
        _entryId = entryId;
        _flow = flow;
        _service = service;
    }

    public FrameworkElement View => _view ??= new AudioDevicesView { DataContext = this };

    public ObservableCollection<AudioButtonItem> Items { get; } = [];

    public FontFamily IconFontFamily { get; } = new(AudioIcons.FontFamily);

    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }

    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        _audio = settings.Audio;
        // Geräteauswahl dieser Widget-Instanz; leer = alle aktiven Geräte (neue erscheinen automatisch).
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _selectedDevices = new HashSet<string>(entry?.Devices ?? [], StringComparer.OrdinalIgnoreCase);
        Foreground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        FontFamily = new FontFamily(bar.Appearance.FontFamily);
        if (_running) Rebuild();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _service.Changed += OnDevicesChanged;
        Rebuild();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _service.Changed -= OnDevicesChanged;
    }

    public void Refresh()
    {
        if (_running) Rebuild();
    }

    public void Dispose() => Stop();

    private void OnDevicesChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        bool showIcon = _audio.LabelMode != AudioLabelMode.NameOnly;
        bool showLabel = _audio.LabelMode != AudioLabelMode.IconOnly;
        string kind = _flow == AudioFlow.Output ? "Wiedergabegerät" : "Mikrofon";

        Items.Clear();
        foreach (var device in _service.GetDevices(_flow))
        {
            if (_selectedDevices.Count > 0 && !_selectedDevices.Contains(device.Id)) continue;
            var custom = _audio.Find(device.Id);

            string label = string.IsNullOrEmpty(custom?.Label) ? ShortName(device.Name) : custom!.Label;
            string tip = device.IsDefault
                ? $"{device.Name}\nAktuelles Standard-{kind}"
                : $"{device.Name}\nKlicken: als Standard-{kind} verwenden";
            string id = device.Id;

            Items.Add(new AudioButtonItem(
                AudioIcons.Resolve(custom?.Icon, _flow, device.FormFactor, device.Name),
                label,
                showIcon ? Visibility.Visible : Visibility.Collapsed,
                showLabel ? Visibility.Visible : Visibility.Collapsed,
                tip,
                device.IsDefault,
                new RelayCommand(() => _service.SetDefault(id))));
        }
    }

    /// <summary>"Lautsprecher (Realtek High Definition Audio)" → "Lautsprecher"; die Klammer steht im Tooltip.</summary>
    private static string ShortName(string name)
    {
        int bracket = name.IndexOf(" (", StringComparison.Ordinal);
        return bracket > 0 ? name[..bracket] : name;
    }
}
