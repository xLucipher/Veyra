using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Veyra.Core;

namespace Veyra.SettingsUI;

public sealed record ColorPreset(string Name, string? Hex);

/// <summary>
/// Farbauswahl: Farbfeld (öffnet den Farbkreis), Pipette (Farbe vom Bildschirm), Hex-Feld für exakte
/// Werte und Dropdown mit abgestimmten Vorlagen. Alle bearbeiten denselben Wert.
/// </summary>
public partial class ColorField : UserControl
{
    private static readonly ColorPreset Custom = new("Eigene Farbe", null);

    public static readonly IReadOnlyList<ColorPreset> Palette =
    [
        new("Weiß", "#FFFFFF"),
        new("Hellgrau", "#B4B7C2"),
        new("Grau", "#8A8D96"),
        new("Schiefer", "#4A4D57"),
        new("Dunkelgrau (Linie)", "#2E3038"),
        new("Graphit", "#31363B"),
        new("Anthrazit (Standard)", "#202127"),
        new("Nachtschwarz", "#1B1E20"),
        new("Polargrau", "#2E3440"),
        new("Schwarz", "#000000"),
        new("Himmelblau", "#3DAEE9"),
        new("Türkis", "#1ABC9C"),
        new("Grün", "#27AE60"),
        new("Gelb", "#FDBC4B"),
        new("Orange", "#F67400"),
        new("Rot", "#DA4453"),
        new("Violett", "#9B59B6"),
    ];

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(ColorField),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    private bool _syncing;
    private DateTime _popupClosedUtc;

    public ColorField()
    {
        InitializeComponent();
        WheelPopup.Closed += (_, _) => _popupClosedUtc = DateTime.UtcNow;
        Presets.ItemsSource = Palette.Append(Custom).ToList();

        // Popup-Inhalt liegt in einem eigenen visuellen Baum → Bindungen direkt an dieses Steuerelement.
        var converter = new HexToBrushConverter();
        SwatchButton.SetBinding(BackgroundProperty, new Binding(nameof(Value)) { Source = this, Converter = converter });
        PopupPreview.SetBinding(Border.BackgroundProperty, new Binding(nameof(Value)) { Source = this, Converter = converter });
        PopupHex.SetBinding(TextBlock.TextProperty, new Binding(nameof(Value)) { Source = this });
        Wheel.SetBinding(ColorWheel.HexProperty, new Binding(nameof(Value)) { Source = this, Mode = BindingMode.TwoWay });

        SyncSelection();
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ColorField)d).SyncSelection();

    private void SyncSelection()
    {
        if (Presets is null) return;
        _syncing = true;
        var value = Value?.Trim() ?? "";
        Presets.SelectedItem = Palette.FirstOrDefault(p => string.Equals(p.Hex, value, StringComparison.OrdinalIgnoreCase)) ?? Custom;
        _syncing = false;
    }

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        // Ein Klick auf das Farbfeld schließt den offenen Farbkreis bereits beim Drücken – dann nicht sofort wieder öffnen.
        if ((DateTime.UtcNow - _popupClosedUtc).TotalMilliseconds < 250) return;
        WheelPopup.IsOpen = true;
    }

    private void OnDoneClick(object sender, RoutedEventArgs e) => WheelPopup.IsOpen = false;

    private async void OnEyedropperClick(object sender, RoutedEventArgs e)
    {
        bool reopen = WheelPopup.IsOpen;
        if (reopen)
        {
            WheelPopup.IsOpen = false;
            await Task.Delay(250); // Ausblenden abwarten, sonst landet der Farbkreis im Standbild
        }

        ScreenColorPicker.Pick(color =>
        {
            if (color is { } picked)
            {
                // Transparenz eines bisherigen #AARRGGBB-Werts beibehalten.
                byte alpha = ColorUtil.TryParse(Value, out var current) ? current.A : (byte)255;
                Value = alpha == 255
                    ? $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}"
                    : $"#{alpha:X2}{picked.R:X2}{picked.G:X2}{picked.B:X2}";
            }
            if (reopen) WheelPopup.IsOpen = true;
        });
    }

    private void OnPresetSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || Presets.SelectedItem is not ColorPreset preset) return;
        if (preset.Hex is null)
        {
            WheelPopup.IsOpen = true; // "Eigene Farbe" → direkt den Farbkreis anbieten
            return;
        }
        Value = preset.Hex;
    }
}
