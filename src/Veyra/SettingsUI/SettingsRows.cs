using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Veyra.Audio;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.SettingsUI;

public sealed record EnumOption(object Value, string Display);


public sealed record WidgetInfo(WidgetType Type, string Name, string Description, string Glyph);

public static class WidgetCatalog
{
    public static readonly IReadOnlyList<WidgetInfo> All =
    [
        new(WidgetType.Clock, "Uhr", "Feste Position Links, Mitte oder Rechts – Klick öffnet den Kalender", ""),
        new(WidgetType.AudioOutputs, "Wiedergabegeräte", "Buttons zum schnellen Wechsel des Ausgabegeräts", ""),
        new(WidgetType.AudioInputs, "Mikrofone", "Buttons zum schnellen Wechsel des Mikrofons", ""),
    ];

    public static WidgetInfo Get(WidgetType type) => All.First(w => w.Type == type);

    public static IReadOnlyList<EnumOption> Slots { get; } =
    [
        new(WidgetSlot.Left, "Links"),
        new(WidgetSlot.Center, "Mitte"),
        new(WidgetSlot.Right, "Rechts"),
    ];

    public static IReadOnlyList<double> MarginPresets { get; } = [0, 4, 8, 12, 16, 24, 32, 48, 64];

    public static IReadOnlyList<double> SpacingPresets { get; } = [0, 4, 8, 12, 16, 24, 32, 48];

    public static IReadOnlyList<double> ScalePresets { get; } = [50, 75, 100, 125, 150, 200, 250, 300, 400, 500];
}

/// <summary>Ein Gerät mit Häkchen in einem Audio-Widget.</summary>
public sealed class DeviceChoice(string id, string name, string iconKey, bool connected, bool selected) : ObservableObject
{
    private bool _selected = selected;

    public string Id { get; } = id;
    public string Name { get; } = connected ? name : $"{name} (nicht verbunden)";
    public string IconKey { get; } = iconKey;
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
}

/// <summary>
/// Eine Widget-Instanz im Einstellungsfenster. Feste Widgets (Uhr) haben Bereich und Abstände,
/// freie Widgets werden auf der Leiste per Drag &amp; Drop platziert; Audio-Widgets haben eine Geräteauswahl.
/// </summary>
public sealed class WidgetRowViewModel : ObservableObject
{
    private readonly WidgetEntry _entry;
    private WidgetSlot _slot;
    private bool _enabled;
    private double _outerMargin;
    private double _innerSpacing;
    private double _scalePercent;
    private bool _allDevices;
    private string _title;
    private readonly string _barForeground;
    private string _iconColor;
    private string _labelColor;
    private bool _iconColorCustom;
    private bool _labelColorCustom;
    private string _indicatorColor;
    private bool _indicatorColorCustom;

    /// <param name="barForeground">Schriftfarbe der Leiste – gilt, solange das Widget keine eigene Farbe hat.</param>
    /// <param name="editClock">Öffnet die Seite "Uhr &amp; Kalender" mit dieser Uhr.</param>
    public WidgetRowViewModel(WidgetEntry entry, IReadOnlyList<AudioDevice> devices, AudioSettings audio, string barForeground,
        Action<WidgetRowViewModel> remove, Action<WidgetRowViewModel> editClock)
    {
        _entry = entry;
        Info = WidgetCatalog.Get(entry.Type);
        _title = Info.Name;
        _slot = entry.Slot;
        _enabled = entry.Enabled;
        _outerMargin = entry.OuterMargin;
        _innerSpacing = entry.InnerSpacing;
        _scalePercent = Math.Round(entry.Scale * 100);
        _allDevices = entry.Devices.Count == 0;
        _barForeground = barForeground;
        _iconColorCustom = entry.IconColor is not null;
        _labelColorCustom = entry.LabelColor is not null;
        _iconColor = entry.IconColor ?? barForeground;
        _labelColor = entry.LabelColor ?? barForeground;
        _indicatorColorCustom = entry.IndicatorColor is not null;
        _indicatorColor = entry.IndicatorColor ?? WidgetEntry.DefaultIndicatorColor;
        // Eigene Kopie: Änderungen auf der Seite "Uhr & Kalender" wirken erst beim Übernehmen.
        Clock = entry.Clock?.Clone() ?? new ClockSettings();
        RemoveCommand = new RelayCommand(() => remove(this));
        EditClockCommand = new RelayCommand(() => editClock(this));
        ResetColorsCommand = new RelayCommand(() =>
        {
            IconColor = _barForeground;
            LabelColor = _barForeground;
            IndicatorColor = WidgetEntry.DefaultIndicatorColor;
            _iconColorCustom = false;
            _labelColorCustom = false;
            _indicatorColorCustom = false;
        });

        if (IsAudio)
        {
            var flow = entry.Type == WidgetType.AudioOutputs ? AudioFlow.Output : AudioFlow.Input;
            foreach (var device in devices)
            {
                Devices.Add(new DeviceChoice(device.Id, device.Name,
                    AudioIcons.Resolve(audio.Find(device.Id)?.Icon, flow, device.FormFactor, device.Name), connected: true,
                    selected: _allDevices || entry.Devices.Contains(device.Id, StringComparer.OrdinalIgnoreCase)));
            }
            // Gewählte, aber gerade getrennte Geräte bleiben in der Auswahl erhalten.
            foreach (var missing in entry.Devices.Where(id => devices.All(d => !d.Id.Equals(id, StringComparison.OrdinalIgnoreCase))))
            {
                var name = audio.Find(missing)?.Name;
                Devices.Add(new DeviceChoice(missing, string.IsNullOrEmpty(name) ? "Unbekanntes Gerät" : name,
                    AudioIcons.Resolve(audio.Find(missing)?.Icon, flow, AudioFormFactor.Unknown, name ?? ""), connected: false, selected: true));
            }
        }
    }

    public string Id => _entry.Id;
    public WidgetInfo Info { get; }
    public ICommand RemoveCommand { get; }
    public ICommand EditClockCommand { get; }
    public ICommand ResetColorsCommand { get; }

    /// <summary>Anzeigename; bei mehreren Uhren auf einer Leiste nummeriert ("Uhr 2").</summary>
    public string Title { get => _title; set => SetProperty(ref _title, value); }

    public override string ToString() => Title; // Name für Auswahllisten und Bildschirmleser

    public bool IsFixed => _entry.IsFixed;
    public bool IsClock => _entry.Type == WidgetType.Clock;
    public bool IsAudio => _entry.Type is WidgetType.AudioOutputs or WidgetType.AudioInputs;
    public Visibility FixedVisibility => IsFixed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FreeVisibility => IsFixed ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AudioVisibility => IsAudio ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ClockVisibility => IsClock ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Nur Uhren: Einstellungen dieser Uhr (wird auf der Seite "Uhr &amp; Kalender" bearbeitet).</summary>
    public ClockSettings Clock { get; }

    /// <summary>Nur Audio-Widgets: Farbe der Symbole.</summary>
    public string IconColor
    {
        get => _iconColor;
        set
        {
            if (SetProperty(ref _iconColor, value ?? "")) _iconColorCustom = true;
        }
    }

    /// <summary>Nur Audio-Widgets: Farbe der Gerätenamen.</summary>
    public string LabelColor
    {
        get => _labelColor;
        set
        {
            if (SetProperty(ref _labelColor, value ?? "")) _labelColorCustom = true;
        }
    }

    /// <summary>Nur Audio-Widgets: Farbe des Strichs unter dem Standardgerät.</summary>
    public string IndicatorColor
    {
        get => _indicatorColor;
        set
        {
            if (SetProperty(ref _indicatorColor, value ?? "")) _indicatorColorCustom = true;
        }
    }

    public bool Enabled { get => _enabled; set => SetProperty(ref _enabled, value); }

    public WidgetSlot Slot
    {
        get => _slot;
        set
        {
            if (SetProperty(ref _slot, value)) OnPropertyChanged(nameof(OuterMarginEnabled));
        }
    }

    /// <summary>In der Mitte gibt es keinen Randabstand.</summary>
    public bool OuterMarginEnabled => Slot != WidgetSlot.Center;

    public double OuterMargin { get => _outerMargin; set => SetProperty(ref _outerMargin, value); }
    public double InnerSpacing { get => _innerSpacing; set => SetProperty(ref _innerSpacing, value); }

    /// <summary>Größe in Prozent (100 = normal).</summary>
    public double ScalePercent { get => _scalePercent; set => SetProperty(ref _scalePercent, value); }

    public bool AllDevices
    {
        get => _allDevices;
        set
        {
            if (!SetProperty(ref _allDevices, value)) return;
            OnPropertyChanged(nameof(DeviceListEnabled));
            if (value) foreach (var device in Devices) device.Selected = true;
        }
    }

    public bool DeviceListEnabled => !AllDevices;

    public ObservableCollection<DeviceChoice> Devices { get; } = [];

    public string PositionText => $"Position auf der Leiste: {_entry.Position:P0} – per Drag & Drop verschieben";

    /// <param name="live">Eintrag aus der laufenden Konfiguration – Positionen können sich durch Drag &amp; Drop geändert haben.</param>
    public WidgetEntry ToEntry(WidgetEntry? live) => new()
    {
        Id = _entry.Id,
        Type = _entry.Type,
        Enabled = Enabled,
        Slot = Slot,
        OuterMargin = OuterMargin,
        InnerSpacing = InnerSpacing,
        Scale = ScalePercent / 100,
        Position = live?.Position ?? _entry.Position,
        FreeX = live is null ? _entry.FreeX : live.FreeX,
        FreeY = live is null ? _entry.FreeY : live.FreeY,
        Devices = AllDevices ? [] : Devices.Where(d => d.Selected).Select(d => d.Id).ToList(),
        // Ohne eigene Farbe (null) folgt das Widget der Schriftfarbe der Leiste.
        IconColor = IsAudio && _iconColorCustom ? IconColor : null,
        LabelColor = IsAudio && _labelColorCustom ? LabelColor : null,
        IndicatorColor = IsAudio && _indicatorColorCustom ? IndicatorColor : null,
        Clock = IsClock ? Clock.Clone() : null,
    };
}

/// <summary>Symbol und Kurzname je Audiogerät (gilt für alle Audio-Widgets).</summary>
public sealed class AudioDeviceRowViewModel : ObservableObject
{
    private string _icon;
    private string _label;

    public AudioDeviceRowViewModel(AudioDevice device, AudioDeviceSettings? settings)
    {
        Device = device;
        _icon = settings?.Icon ?? AudioIcons.AutoKey;
        _label = settings?.Label ?? "";
    }

    public AudioDevice Device { get; }
    public string Name => Device.Name;
    public string State => Device.IsDefault ? "Standardgerät" : "";

    public string Icon
    {
        get => _icon;
        set
        {
            if (string.IsNullOrEmpty(value)) return; // Listenaustausch schreibt kurz null
            if (SetProperty(ref _icon, value)) OnPropertyChanged(nameof(IconKey));
        }
    }

    public string Label { get => _label; set => SetProperty(ref _label, value ?? ""); }

    /// <summary>Tatsächlich angezeigtes Symbol (bei "Auto" das automatisch gewählte).</summary>
    public string IconKey => AudioIcons.Resolve(_icon, Device.Flow, Device.FormFactor, Device.Name);

    public string AutoIconKey => AudioIcons.Auto(Device.Flow, Device.FormFactor, Device.Name);

    public bool IsCustomized => Icon != AudioIcons.AutoKey || Label.Length > 0;
}

/// <summary>Eintrag in der Leistenauswahl; der Name aktualisiert sich beim Umbenennen sofort.</summary>
public sealed class BarOption(int index, string name) : ObservableObject
{
    private string _name = name;

    public int Index { get; } = index;
    public string Name { get => _name; set => SetProperty(ref _name, value); }

    public override string ToString() => Name; // Name für Bildschirmleser
}
