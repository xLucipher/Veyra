using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CustomTopBar.Configuration;
using CustomTopBar.Core;

namespace CustomTopBar.Bar;

/// <summary>ViewModel des Leistenfensters: Darstellung, Widget-Slots und Kontextmenü-Befehle.</summary>
public sealed class TopBarViewModel : ObservableObject
{
    private Brush _background = Brushes.Black;
    private Brush _borderBrush = Brushes.Transparent;
    private Thickness _borderThickness;
    private Brush _foreground = Brushes.White;
    private FontFamily _fontFamily = new("Segoe UI");

    public TopBarViewModel(Action openSettings, Action reload, Action exit)
    {
        OpenSettingsCommand = new RelayCommand(openSettings);
        ReloadCommand = new RelayCommand(reload);
        ExitCommand = new RelayCommand(exit);
    }

    public ICommand OpenSettingsCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ExitCommand { get; }


    public Brush Background { get => _background; private set => SetProperty(ref _background, value); }
    public Brush BorderBrush { get => _borderBrush; private set => SetProperty(ref _borderBrush, value); }
    public Thickness BorderThickness { get => _borderThickness; private set => SetProperty(ref _borderThickness, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }

    public void Apply(AppSettings settings)
    {
        var a = settings.Appearance;
        // FullWidth-Fenster ist nicht transparent (schneller, kein Layered-Window) → Deckkraft wirkt erst im Floating-Modus.
        Background = ColorUtil.ToBrush(a.BackgroundColor, Color.FromRgb(0x20, 0x21, 0x27));
        BorderBrush = ColorUtil.ToBrush(a.BorderColor, Colors.Transparent);
        BorderThickness = new Thickness(0, 0, 0, a.BorderThickness);
        Foreground = ColorUtil.ToBrush(a.ForegroundColor, Colors.White);
        FontFamily = new FontFamily(a.FontFamily);
    }
}
