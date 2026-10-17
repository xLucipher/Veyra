using System.Windows;
using System.Windows.Controls;
using Veyra.Audio;

namespace Veyra.SettingsUI;

/// <summary>Symbolauswahl als Raster nach Kategorien (Wiedergabe, Mikrofone, Allgemein) plus "Automatisch".</summary>
public partial class IconPicker : UserControl
{
    public static readonly DependencyProperty SelectedKeyProperty = DependencyProperty.Register(
        nameof(SelectedKey), typeof(string), typeof(IconPicker),
        new FrameworkPropertyMetadata(AudioIcons.AutoKey, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((IconPicker)d).UpdateCurrent()));

    /// <summary>Symbol, das "Automatisch" für dieses Gerät ergibt (für die Vorschau).</summary>
    public static readonly DependencyProperty AutoKeyProperty = DependencyProperty.Register(
        nameof(AutoKey), typeof(string), typeof(IconPicker), new PropertyMetadata("Speakers", (d, _) => ((IconPicker)d).UpdateCurrent()));

    private DateTime _closedUtc;

    public IconPicker()
    {
        InitializeComponent();
        OutputIcons.ItemsSource = AudioIcons.InCategory(IconCategory.Output).ToList();
        InputIcons.ItemsSource = AudioIcons.InCategory(IconCategory.Input).ToList();
        GeneralIcons.ItemsSource = AudioIcons.InCategory(IconCategory.General).ToList();
        Picker.Closed += (_, _) => _closedUtc = DateTime.UtcNow;
        UpdateCurrent();
    }

    public string SelectedKey
    {
        get => (string)GetValue(SelectedKeyProperty);
        set => SetValue(SelectedKeyProperty, value);
    }

    public string AutoKey
    {
        get => (string)GetValue(AutoKeyProperty);
        set => SetValue(AutoKeyProperty, value);
    }

    private void UpdateCurrent()
    {
        if (CurrentIcon is null) return;
        bool auto = string.IsNullOrEmpty(SelectedKey) || SelectedKey == AudioIcons.AutoKey;
        var shown = AudioIcons.Get(auto ? AutoKey : SelectedKey);
        CurrentIcon.IconKey = shown.Key;
        CurrentName.Text = auto ? $"Automatisch ({shown.Name})" : shown.Name;
        AutoIcon.IconKey = AutoKey;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        // Klick auf den Knopf schließt ein offenes Popup bereits beim Drücken – dann nicht sofort wieder öffnen.
        if ((DateTime.UtcNow - _closedUtc).TotalMilliseconds < 250) return;
        Picker.IsOpen = true;
    }

    private void OnIconClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string key }) SelectedKey = key;
        Picker.IsOpen = false;
    }

    private void OnAutoClick(object sender, RoutedEventArgs e)
    {
        SelectedKey = AudioIcons.AutoKey;
        Picker.IsOpen = false;
    }
}
