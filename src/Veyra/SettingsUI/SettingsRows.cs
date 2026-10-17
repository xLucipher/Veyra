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
    private bool _allDevices;

    public WidgetRowViewModel(WidgetEntry entry, IReadOnlyList<AudioDevice> devices, AudioSettings audio, Action<WidgetRowViewModel> remove)
    {
        _entry = entry;
        Info = WidgetCatalog.Get(entry.Type);
        _slot = entry.Slot;
        _enabled = entry.Enabled;
        _outerMargin = entry.OuterMargin;
        _innerSpacing = entry.InnerSpacing;
        _allDevices = entry.Devices.Count == 0;
        RemoveCommand = new RelayCommand(() => remove(this));

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

    public bool IsFixed => _entry.IsFixed;
    public bool IsAudio => _entry.Type is WidgetType.AudioOutputs or WidgetType.AudioInputs;
    public Visibility FixedVisibility => IsFixed ? Visibility.Visible : Visibility.Collapsed;
    public Visibility FreeVisibility => IsFixed ? Visibility.Collapsed : Visibility.Visible;
    public Visibility AudioVisibility => IsAudio ? Visibility.Visible : Visibility.Collapsed;

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

    /// <param name="currentPosition">Position aus der laufenden Konfiguration (kann sich durch Drag &amp; Drop geändert haben).</param>
    public WidgetEntry ToEntry(double? currentPosition) => new()
    {
        Id = _entry.Id,
        Type = _entry.Type,
        Enabled = Enabled,
        Slot = Slot,
        OuterMargin = OuterMargin,
        InnerSpacing = InnerSpacing,
        Position = currentPosition ?? _entry.Position,
        Devices = AllDevices ? [] : Devices.Where(d => d.Selected).Select(d => d.Id).ToList(),
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
