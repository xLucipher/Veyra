using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Gaming;

namespace Veyra.Widgets.Metrics;

/// <summary>
/// Bilder pro Sekunde des Programms im Vordergrund, optional mit Frametime, 1-%-Low und Programmname. Klick zeigt
/// Details mit Verlauf der letzten Minute – bzw. die Freigabe, solange die Gruppe „Leistungsprotokollbenutzer“ fehlt.
/// Die Fläche ist im Vollbild nur sichtbar, wenn sie dort nicht ausgeblendet wird (Einstellungen → Vollbild & Spiele).
/// </summary>
public sealed class FpsWidget : ObservableObject, IBarWidget
{
    private readonly string _entryId;
    private readonly FpsService _service;
    private readonly Border _view;
    private readonly TextBlock _caption;
    private readonly TextBlock _value;
    private readonly TextBlock _detail;
    private FpsSettings _fps = new();
    private Brush _valueBrush = Brushes.White;
    private Brush _accentBrush = Brushes.DeepSkyBlue;
    private FpsFlyout? _flyout;
    private bool _running;
    private bool _granting;
    private string _grantStatus = "";

    public FpsWidget(string entryId, FpsService service)
    {
        _entryId = entryId;
        _service = service;
        _caption = new TextBlock { Text = "FPS", FontSize = 11, FontWeight = FontWeights.SemiBold, Opacity = 0.65, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        _value = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 26, TextAlignment = TextAlignment.Right };
        _detail = new TextBlock { FontSize = 12, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
        Typography.SetNumeralAlignment(_value, FontNumeralAlignment.Tabular);
        Typography.SetNumeralAlignment(_detail, FontNumeralAlignment.Tabular);
        _view = new Border { Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _caption, _value, _detail } } };
        _view.SetResourceReference(FrameworkElement.StyleProperty, "WidgetHoverArea");
        _view.MouseLeftButtonUp += (_, _) => ToggleFlyout();
        GrantCommand = new RelayCommand(() => _ = GrantAsync());
    }

    public FrameworkElement View => _view;

    // ---- Popup ------------------------------------------------------------------------------
    public ICommand GrantCommand { get; }

    public Visibility MeasureVisibility => _service.State == FpsState.Running ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PermissionVisibility => _service.State == FpsState.Running ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GrantButtonVisibility => _service.State == FpsState.NoPermission && !_granting ? Visibility.Visible : Visibility.Collapsed;

    public string Subtitle => _service.Current is { } reading
        ? (reading.Process.Length > 0 ? reading.Process : "Programm im Vordergrund")
        : "Kein Programm im Vordergrund gibt gerade Bilder aus.";

    public string FpsText => _service.Current is { } r ? r.Fps.ToString("0", CultureInfo.CurrentCulture) : "–";
    public string FrameTimeText => _service.Current is { } r ? r.FrameTimeMs.ToString("0.0", CultureInfo.CurrentCulture) + " ms" : "–";
    public string LowText => _service.Current is { } r ? r.OnePercentLow.ToString("0", CultureInfo.CurrentCulture) + " FPS" : "–";

    public string SummaryText
    {
        get
        {
            var history = _service.History;
            if (history.Count < 2) return "";
            var culture = CultureInfo.CurrentCulture;
            return $"Letzte {history.Count} s: Ø {history.Average().ToString("0", culture)} · min. {history.Min().ToString("0", culture)} · max. {history.Max().ToString("0", culture)} FPS";
        }
    }

    /// <summary>Verlauf der letzten Minute für das Popup (320 × 60).</summary>
    public PointCollection Graph
    {
        get
        {
            var history = _service.History;
            var points = new PointCollection();
            if (history.Count < 2) return points;
            double max = Math.Max(30, history.Max() * 1.1);
            for (int i = 0; i < history.Count; i++)
                points.Add(new Point(i * 320.0 / 59, 60 - history[i] / max * 60));
            points.Freeze();
            return points;
        }
    }

    public Brush AccentBrush => _accentBrush;

    public string PermissionTitle => _service.State switch
    {
        FpsState.NeedsSignIn => "Bitte einmal ab- und wieder anmelden",
        FpsState.Failed => "FPS-Messung nicht möglich",
        _ => "FPS-Messung erlauben",
    };

    public string PermissionText => _service.State switch
    {
        FpsState.NeedsSignIn => $"Dein Konto ist in der Gruppe „{FpsPermission.GroupName}“. Windows übernimmt das erst bei der nächsten Anmeldung – danach misst Veyra die FPS ohne weitere Fragen.",
        FpsState.Failed => "Windows hat die Messsitzung abgelehnt. Details stehen im Protokoll (Einstellungen → System & Updates).",
        _ => $"Windows gibt die Bildausgabe von Spielen nur an Administratoren und Mitglieder der Gruppe „{FpsPermission.GroupName}“ heraus. " +
             "Ein Klick nimmt dein Konto einmalig auf – Windows fragt dabei nach deiner Erlaubnis. Danach einmal ab- und wieder anmelden. " +
             "Hinweis: Die Gruppe gilt für alle Programme deines Kontos, nicht nur für Veyra.",
    };

    public string GrantStatus { get => _grantStatus; private set => SetProperty(ref _grantStatus, value); }

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _fps = entry?.Fps ?? new FpsSettings();
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        _caption.Foreground = entry?.IconColor is { } icon ? ColorUtil.ToBrush(icon, Colors.White) : barForeground;
        _caption.Opacity = entry?.IconColor is null ? 0.65 : 1;
        _valueBrush = entry?.LabelColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        _detail.Foreground = _valueBrush;
        _accentBrush = ColorUtil.ToBrush(entry?.IndicatorColor ?? WidgetEntry.DefaultIndicatorColor, Colors.DeepSkyBlue);
        var family = new FontFamily(bar.Appearance.FontFamily);
        _caption.FontFamily = family;
        _value.FontFamily = family;
        _detail.FontFamily = family;
        Update();
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _service.Changed += OnChanged;
        _service.Acquire(this);
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
        var reading = _service.Current;
        if (_service.State != FpsState.Running)
        {
            _value.Text = "–";
            _value.Foreground = _valueBrush;
            _detail.Visibility = Visibility.Collapsed;
            _view.ToolTip = _service.State == FpsState.NeedsSignIn
                ? "FPS: einmal ab- und wieder anmelden, dann wird gemessen"
                : "FPS: Messung noch nicht erlaubt – klicken zum Einrichten";
        }
        else if (reading is null)
        {
            _value.Text = "–";
            _value.Foreground = _valueBrush;
            _detail.Visibility = Visibility.Collapsed;
            _view.ToolTip = "FPS des Programms im Vordergrund – gerade gibt es keine Bilder aus";
        }
        else
        {
            var culture = CultureInfo.CurrentCulture;
            _value.Text = reading.Fps.ToString("0", culture);
            _value.Foreground = _fps.WarnBelow > 0 && reading.Fps < _fps.WarnBelow ? Units.CriticalBrush
                : _fps.WarnBelow > 0 && reading.Fps < _fps.WarnBelow * 2 ? Units.WarnBrush
                : _valueBrush;
            var parts = new List<string>();
            if (_fps.ShowFrameTime) parts.Add(reading.FrameTimeMs.ToString("0.0", culture) + " ms");
            if (_fps.ShowOnePercentLow) parts.Add("1 % " + reading.OnePercentLow.ToString("0", culture));
            if (_fps.ShowProcess && reading.Process.Length > 0) parts.Add(reading.Process);
            _detail.Text = string.Join(" · ", parts);
            _detail.Visibility = parts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _view.ToolTip = $"{(reading.Process.Length > 0 ? reading.Process : "Programm im Vordergrund")}: {_value.Text} FPS\n" +
                            $"Frametime {reading.FrameTimeMs.ToString("0.0", culture)} ms · 1 % Low {reading.OnePercentLow.ToString("0", culture)} FPS\nKlicken für Details";
        }

        foreach (var name in new[] { nameof(MeasureVisibility), nameof(PermissionVisibility), nameof(GrantButtonVisibility), nameof(Subtitle),
                     nameof(FpsText), nameof(FrameTimeText), nameof(LowText), nameof(SummaryText), nameof(Graph), nameof(PermissionTitle),
                     nameof(PermissionText), nameof(AccentBrush) })
            OnPropertyChanged(name);
    }

    private async Task GrantAsync()
    {
        _granting = true;
        GrantStatus = "Warte auf die Windows-Abfrage …";
        Update();
        GrantStatus = await FpsPermission.RequestAsync();
        _granting = false;
        _service.Retry();
        Update();
    }

    private void ToggleFlyout()
    {
        GrantStatus = "";
        _flyout = FlyoutWindow.Toggle(_view, () => new FpsFlyout { DataContext = this }) as FpsFlyout;
        if (_flyout is null) return;
        _flyout.Closed += (_, _) => _flyout = null;
        _service.Retry(); // z. B. nach dem Anmelden ohne Neustart von Veyra
    }
}
