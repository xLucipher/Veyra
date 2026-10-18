using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Veyra.Audio;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Widgets.Volume;

/// <summary>
/// Systemlautstärke des Standard-Wiedergabegeräts (oder -Mikrofons): Symbol nach Pegel, optional Prozentwert und
/// Balken. Mausrad ändert die Lautstärke, Linksklick öffnet den Regler, Rechtsklick schaltet stumm. Änderungen durch
/// Windows oder andere Programme kommen als Ereignis vom gemeinsamen <see cref="VolumeService"/>.
/// </summary>
public sealed class VolumeWidget : ObservableObject, IBarWidget, IHotkeyTarget
{
    private const double TrackWidthValue = 36;

    private readonly string _entryId;
    private readonly VolumeService _service;
    private VolumeView? _view;
    private VolumeSettings _volume = new();
    private AudioFlow _flow = AudioFlow.Output;
    private bool _running;
    private int _wheelRemainder;

    private string _glyph = "";
    private string _percentText = "";
    private string _toolTipText = "";
    private Brush _foreground = Brushes.White;
    private Brush _iconBrush = Brushes.White;
    private Brush _accentBrush = Brushes.White;
    private Brush _trackBrush = Brushes.Transparent;
    private FontFamily _fontFamily = new("Segoe UI");
    private Visibility _percentVisibility = Visibility.Visible;
    private Visibility _barVisibility = Visibility.Collapsed;
    private double _barWidth;

    public VolumeWidget(string entryId, VolumeService service)
    {
        _entryId = entryId;
        _service = service;
        ToggleMuteCommand = new RelayCommand(() => _service.ToggleMute(_flow));
        OpenSoundSettingsCommand = new RelayCommand(() => _flyout?.OpenAndClose("ms-settings:sound"));
        OpenMixerCommand = new RelayCommand(() => _flyout?.OpenAndClose("ms-settings:apps-volume"));
    }

    public FrameworkElement View => _view ??= new VolumeView { DataContext = this };

    public string Glyph { get => _glyph; private set => SetProperty(ref _glyph, value); }
    public string PercentText { get => _percentText; private set => SetProperty(ref _percentText, value); }
    public string ToolTipText { get => _toolTipText; private set => SetProperty(ref _toolTipText, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public Brush IconBrush { get => _iconBrush; private set => SetProperty(ref _iconBrush, value); }
    public Brush AccentBrush { get => _accentBrush; private set => SetProperty(ref _accentBrush, value); }
    public Brush TrackBrush { get => _trackBrush; private set => SetProperty(ref _trackBrush, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }
    public Visibility PercentVisibility { get => _percentVisibility; private set => SetProperty(ref _percentVisibility, value); }
    public Visibility BarVisibility { get => _barVisibility; private set => SetProperty(ref _barVisibility, value); }
    public double BarWidth { get => _barWidth; private set => SetProperty(ref _barWidth, value); }
    public double TrackWidth => TrackWidthValue;

    // ---- Popup ------------------------------------------------------------------------------
    private VolumeFlyout? _flyout;

    public ICommand ToggleMuteCommand { get; }
    public ICommand OpenSoundSettingsCommand { get; }
    public ICommand OpenMixerCommand { get; }

    /// <summary>Lautstärke 0–100 für den Regler; Setzen ändert die Systemlautstärke sofort.</summary>
    public double Level
    {
        get => Math.Round(_service.Get(_flow).Level * 100);
        set
        {
            if (Math.Abs(value - Level) < 0.5) return;
            _service.SetLevel(_flow, value / 100);
        }
    }

    public bool IsAvailable => _service.Get(_flow).Available;
    public string DeviceName => _service.Get(_flow) is { Available: true } state
        ? (state.DeviceName.Length > 0 ? state.DeviceName : _flow == AudioFlow.Output ? "Wiedergabegerät" : "Mikrofon")
        : _flow == AudioFlow.Output ? "Kein Wiedergabegerät aktiv" : "Kein Mikrofon aktiv";

    /// <summary>Schrittweite des Mausrads in Prozentpunkten (auch für die App-Regler im Popup).</summary>
    public int WheelStep => _volume.WheelStep;

    public string FlowTitle => _flow == AudioFlow.Output ? "Lautstärke" : "Mikrofonpegel";
    public string MuteToolTip => _service.Get(_flow).Muted ? "Stummschaltung aufheben" : "Stummschalten";

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _volume = entry?.Volume ?? new VolumeSettings();
        if (_volume.Flow != _flow)
        {
            if (_running) _service.Release(_flow);
            _flow = _volume.Flow;
            if (_running) _service.Acquire(_flow);
        }

        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        Foreground = entry?.LabelColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        IconBrush = entry?.IconColor is { } icon ? ColorUtil.ToBrush(icon, Colors.White) : barForeground;
        AccentBrush = ColorUtil.ToBrush(entry?.IndicatorColor ?? WidgetEntry.DefaultIndicatorColor, Colors.DeepSkyBlue);
        TrackBrush = Units.Freeze(new SolidColorBrush(((SolidColorBrush)Foreground).Color) { Opacity = 0.14 });
        FontFamily = new FontFamily(bar.Appearance.FontFamily);
        PercentVisibility = _volume.ShowPercent ? Visibility.Visible : Visibility.Collapsed;
        BarVisibility = _volume.ShowBar ? Visibility.Visible : Visibility.Collapsed;
        Update();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _service.Changed += OnVolumeChanged;
        _service.Acquire(_flow);
        Update();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _service.Changed -= OnVolumeChanged;
        _service.Release(_flow);
    }

    public void Refresh() => Update();

    public void Dispose()
    {
        if (_view is not null) FlyoutWindow.CloseFor(_view);
        Stop();
    }

    internal void ToggleFlyout(FrameworkElement anchor)
    {
        _flyout = FlyoutWindow.Toggle(anchor, () => new VolumeFlyout { DataContext = this }) as VolumeFlyout;
        if (_flyout is null) return;
        OpenMixer();
        _flyout.Closed += (_, _) =>
        {
            _flyout = null;
            CloseMixer();
        };
    }

    // ---- Lautstärke je App (nur solange das Popup offen ist) ------------------------------------
    private AudioSessionMixer? _mixer;
    private string _mixerDevice = "";

    public System.Collections.ObjectModel.ObservableCollection<AudioApp>? Apps => _mixer?.Apps;

    public Visibility AppsVisibility => _volume.ShowApps && _mixer is { Apps.Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public string AppsTitle => _flow == AudioFlow.Output ? "Apps" : "Apps, die aufnehmen";

    private void OpenMixer()
    {
        CloseMixer();
        if (!_volume.ShowApps || !IsAvailable) return;
        _mixer = new AudioSessionMixer(_flow);
        _mixerDevice = _service.Get(_flow).DeviceName;
        _mixer.Apps.CollectionChanged += OnAppsChanged;
        OnPropertyChanged(nameof(Apps));
        OnPropertyChanged(nameof(AppsVisibility));
    }

    private void CloseMixer()
    {
        if (_mixer is null) return;
        _mixer.Apps.CollectionChanged -= OnAppsChanged;
        _mixer.Dispose();
        _mixer = null;
        OnPropertyChanged(nameof(Apps));
        OnPropertyChanged(nameof(AppsVisibility));
    }

    private void OnAppsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(AppsVisibility));

    public string EntryId => _entryId;

    /// <summary>
    /// Tastenkürzel – auch im Spiel, während die Leiste ausgeblendet und das Widget angehalten ist; dann wird der
    /// Endpunkt nur für diese eine Aktion gebunden.
    /// </summary>
    public void OnHotkey(string action)
    {
        if (!_running) _service.Acquire(_flow);
        try
        {
            switch (action)
            {
                case WidgetHotkeys.VolumeUp:
                    _service.Step(_flow, _volume.WheelStep);
                    break;
                case WidgetHotkeys.VolumeDown:
                    _service.Step(_flow, -_volume.WheelStep);
                    break;
                case WidgetHotkeys.Mute:
                    _service.ToggleMute(_flow);
                    break;
            }
        }
        finally
        {
            if (!_running) _service.Release(_flow);
        }
    }

    /// <summary>Mausrad: je Raste (120) um den eingestellten Schritt; Feinraster von Touchpads wird aufsummiert.</summary>
    internal void OnWheel(int delta)
    {
        _wheelRemainder += delta;
        int notches = _wheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        if (notches == 0) return;
        _wheelRemainder -= notches * Mouse.MouseWheelDeltaForOneLine;
        _service.Step(_flow, notches * _volume.WheelStep);
    }

    /// <summary>Rechtsklick: stummschalten, sofern eingestellt. false = Kontextmenü der Leiste öffnen lassen.</summary>
    internal bool OnRightClick()
    {
        if (!_volume.RightClickMutes || !IsAvailable) return false;
        _service.ToggleMute(_flow);
        return true;
    }

    private void OnVolumeChanged(object? sender, AudioFlow flow)
    {
        if (flow != _flow) return;
        Update();
        // Standardgerät gewechselt, während das Popup offen ist → Apps des neuen Geräts zeigen.
        if (_flyout is not null && _service.Get(_flow).DeviceName != _mixerDevice) OpenMixer();
    }

    private void Update()
    {
        var state = _service.Get(_flow);
        int percent = (int)Math.Round(state.Level * 100);
        bool output = _flow == AudioFlow.Output;

        // Segoe Fluent Icons: Lautsprecher mit null bis drei Wellen, durchgestrichen bei stumm; Mikrofon bzw. durchgestrichenes Mikrofon.
        Glyph = !output
            ? (state.Muted || !state.Available ? "" : "")
            : !state.Available || state.Muted ? ""
            : percent == 0 ? ""
            : percent < 34 ? ""
            : percent < 67 ? ""
            : "";
        PercentText = state.Available ? (state.Muted ? "Stumm" : $"{percent} %") : "–";
        BarWidth = state.Available && !state.Muted ? TrackWidthValue * Math.Clamp(state.Level, 0, 1) : 0;

        string what = output ? "Lautstärke" : "Mikrofonpegel";
        ToolTipText = state.Available
            ? $"{what}: {(state.Muted ? "stumm" : $"{percent} %")}\n{DeviceName}\nMausrad: ändern · Klicken: Regler{(_volume.RightClickMutes ? " · Rechtsklick: stumm" : "")}"
            : DeviceName;

        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(FlowTitle));
        OnPropertyChanged(nameof(MuteToolTip));
    }
}
