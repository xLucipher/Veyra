using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Bar;

/// <summary>Widget unter dem Mauszeiger beim Öffnen des Kontextmenüs.</summary>
public sealed record WidgetMenuInfo(string Name, bool Locked);

/// <summary>ViewModel des Leistenfensters: Darstellung und Kontextmenü-Befehle.</summary>
public sealed class TopBarViewModel : ObservableObject
{
    private readonly Func<string, WidgetMenuInfo?> _describeWidget;
    private Brush _windowBackground = Brushes.Black;
    private Brush _panelBackground = Brushes.Transparent;
    private CornerRadius _cornerRadius;
    private Brush _borderBrush = Brushes.Transparent;
    private Thickness _borderThickness;
    private Brush _foreground = Brushes.White;
    private FontFamily _fontFamily = new("Segoe UI");
    private bool _isLocked;

    public TopBarViewModel(Action openSettings, Action reload, Action exit, Action toggleLock,
        Action showProfiles, Action<string> switchProfile, Action<string> toggleWidgetLock, Func<string, WidgetMenuInfo?> describeWidget)
    {
        OpenSettingsCommand = new RelayCommand(openSettings);
        ReloadCommand = new RelayCommand(reload);
        ExitCommand = new RelayCommand(exit);
        ToggleLockCommand = new RelayCommand(toggleLock);
        ShowProfilesCommand = new RelayCommand(showProfiles);
        SwitchProfileCommand = new RelayCommand<string>(switchProfile);
        ToggleWidgetLockCommand = new RelayCommand<string>(toggleWidgetLock);
        _describeWidget = describeWidget;
    }

    public ICommand OpenSettingsCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ToggleLockCommand { get; }
    public ICommand ShowProfilesCommand { get; }
    public ICommand SwitchProfileCommand { get; }

    /// <summary>Parameter: Widget-ID.</summary>
    public ICommand ToggleWidgetLockCommand { get; }

    public WidgetMenuInfo? DescribeWidget(string widgetId) => _describeWidget(widgetId);

    /// <summary>Profile für das Kontextmenü (Name, Id, aktiv) und Beschriftung der Profilauswahl samt Tastenkürzel.</summary>
    public IReadOnlyList<(string Id, string Name, bool IsActive)> ProfileEntries { get; private set; } = [];
    public string ProfilePickerText { get; private set; } = "Profilauswahl …";

    public void ApplyProfiles(AppSettings settings)
    {
        ProfileEntries = settings.Profiles.Select(p => (p.Id, p.Name, p.Id == settings.ActiveProfileId)).ToList();
        ProfilePickerText = Hotkey.TryParse(settings.ProfileHotkey, out var hotkey)
            ? $"Profilauswahl …  ({hotkey.DisplayText})"
            : "Profilauswahl …";
    }

    /// <summary>Fixiert voll deckend: Fensterfarbe. Sonst transparent – das Panel zeichnet Hintergrund (mit Deckkraft) und Ecken.</summary>
    public Brush WindowBackground { get => _windowBackground; private set => SetProperty(ref _windowBackground, value); }
    public Brush PanelBackground { get => _panelBackground; private set => SetProperty(ref _panelBackground, value); }
    public CornerRadius CornerRadius { get => _cornerRadius; private set => SetProperty(ref _cornerRadius, value); }
    public Brush BorderBrush { get => _borderBrush; private set => SetProperty(ref _borderBrush, value); }
    public Thickness BorderThickness { get => _borderThickness; private set => SetProperty(ref _borderThickness, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }

    public bool IsLocked
    {
        get => _isLocked;
        private set
        {
            if (!SetProperty(ref _isLocked, value)) return;
            OnPropertyChanged(nameof(LockMenuText));
            OnPropertyChanged(nameof(LockMenuGlyph));
        }
    }

    public string LockMenuText => _isLocked ? "Position entsperren" : "Position sperren";
    public string LockMenuGlyph => _isLocked ? "" : "";

    public void Apply(BarSettings bar)
    {
        var a = bar.Appearance;
        var background = ColorUtil.ToBrush(a.BackgroundColor, Color.FromRgb(0x20, 0x21, 0x27));
        var border = ColorUtil.ToBrush(a.BorderColor, Colors.Transparent);

        if (a.IsDynamic)
        {
            WindowBackground = Brushes.Transparent;
            PanelBackground = ColorUtil.ToBrush(a.BackgroundColor, Color.FromRgb(0x20, 0x21, 0x27), a.BackgroundOpacity);
            CornerRadius = new CornerRadius(a.CornerRadius);
            BorderThickness = new Thickness(a.BorderThickness);
        }
        else if (a.NeedsTransparency)
        {
            // Fixiert mit Deckkraft unter 100 %: transparentes Fenster, das Panel zeichnet den Hintergrund.
            // Mindestens 1/255 Deckkraft, damit die ganze Leiste für das Kontextmenü klickbar bleibt.
            WindowBackground = Brushes.Transparent;
            PanelBackground = ColorUtil.ToBrush(a.BackgroundColor, Color.FromRgb(0x20, 0x21, 0x27), Math.Max(a.BackgroundOpacity, 1 / 255.0));
            CornerRadius = new CornerRadius(0);
            BorderThickness = new Thickness(0, 0, 0, a.BorderThickness);
        }
        else
        {
            // Fixiert: nicht transparentes Fenster (schneller, kein Layered-Window), Trennlinie nur unten.
            WindowBackground = background;
            PanelBackground = Brushes.Transparent;
            CornerRadius = new CornerRadius(0);
            BorderThickness = new Thickness(0, 0, 0, a.BorderThickness);
        }

        BorderBrush = border;
        IsLocked = a.LockPosition;
        Foreground = ColorUtil.ToBrush(a.ForegroundColor, Colors.White);
        FontFamily = new FontFamily(a.FontFamily);
    }
}
