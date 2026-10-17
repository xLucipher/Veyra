using System.Globalization;
using System.Windows.Interop;
using System.Windows.Threading;
using CustomTopBar.Audio;
using CustomTopBar.Configuration;
using CustomTopBar.Core;
using CustomTopBar.Fullscreen;
using CustomTopBar.Interop;
using CustomTopBar.Monitors;
using CustomTopBar.Widgets;
using CustomTopBar.Widgets.Audio;
using CustomTopBar.Widgets.Clock;

namespace CustomTopBar.Bar;

/// <summary>
/// Steuert den Lebenszyklus der Leiste: Fenster und Widgets erzeugen, Einstellungen anwenden,
/// positionieren, auf Systemereignisse reagieren und bei Vollbild aus-/einblenden.
/// Version 1 verwaltet genau eine Leiste; für mehrere Monitore würde später je Monitor ein
/// Fenster + Detektor erzeugt.
/// </summary>
public sealed class BarManager : IDisposable
{
    private readonly SettingsService _settings;
    private readonly Action _openSettings;
    private readonly Action _exit;
    private readonly FullscreenDetector _detector = new();
    private readonly DispatcherTimer _layoutDebounce;
    private readonly List<IBarWidget> _widgets = [];

    private AudioDeviceService? _audioService;
    private string _widgetLayout = "";
    private TopBarViewModel? _viewModel;
    private TopBarWindow? _window;
    private MonitorInfo? _monitor;
    private bool _hiddenForFullscreen;
    private bool _forceNextLayout;
    private bool _floatingWarningLogged;
    private bool _autoHideWarningLogged;
    private bool _disposed;

    public BarManager(SettingsService settings, Action openSettings, Action exit)
    {
        _settings = settings;
        _openSettings = openSettings;
        _exit = exit;

        // Anzeigeänderungen kommen oft als Salve (mehrere WM_DISPLAYCHANGE/DPI-Nachrichten) → bündeln.
        _layoutDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _layoutDebounce.Tick += (_, _) =>
        {
            _layoutDebounce.Stop();
            bool force = _forceNextLayout;
            _forceNextLayout = false;
            UpdatePlacement(force);
            _detector.RequestEvaluation();
        };
    }

    public void Start()
    {
        _detector.StateChanged += (_, _) => ApplyVisibility();
        _detector.ForegroundChanged += (_, _) => KeepOnTop();
        _settings.Changed += OnSettingsChanged;

        BuildBar();
        _detector.Start();
    }

    /// <summary>Kontextmenü "Leiste neu laden": Konfiguration neu einlesen und Fenster samt Widgets neu aufbauen.</summary>
    public void Reload()
    {
        Log.Info("Bar", "Leiste wird neu geladen");
        _settings.ReloadFromDisk();
        TearDownBar();
        BuildBar();
        _detector.EvaluateNow();
    }

    /// <summary>Notfall-Aufräumen aus dem Absturz-Handler (beliebiger Thread): reservierten Bildschirmbereich freigeben.</summary>
    public void EmergencyCleanup()
    {
        try
        {
            _window?.AppBar?.Unregister();
        }
        catch
        {
            // Im Absturzpfad nichts mehr werfen.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        _layoutDebounce.Stop();
        _detector.Dispose();
        TearDownBar();
        _audioService?.Dispose();
    }

    private void BuildBar()
    {
        var settings = _settings.Current;

        _viewModel = new TopBarViewModel(_openSettings, Reload, _exit);
        var window = new TopBarWindow(_viewModel);
        _window = window;
        window.WidgetPanel.WidgetMoved += OnWidgetMoved;
        CreateWidgets(settings);

        window.SourceInitialized += (_, _) =>
        {
            _detector.OwnWindow = window.Handle;
            ApplySettings(settings);
            UpdatePlacement(force: true);
        };
        window.DisplayConfigurationChanged += (_, _) => ScheduleLayout(force: true);
        window.AppBarPositionChanged += (_, _) => ScheduleLayout(force: false);
        window.SessionUnlocked += (_, _) => ScheduleLayout(force: true);
        window.ShellRestarted += (_, _) => OnShellRestarted();
        window.SystemTimeChanged += (_, _) => OnSystemTimeChanged();
        window.ShellWindowActivity += (_, _) => _detector.RequestEvaluation();
        window.FullscreenAppNotification += (_, opening) =>
        {
            Log.Debug("Fullscreen", $"ABN_FULLSCREENAPP: {(opening ? "Vollbild-App geöffnet" : "Vollbild-App geschlossen")}");
            _detector.RequestEvaluation();
        };

        if (_hiddenForFullscreen)
        {
            // Während eines Vollbildspiels neu geladen: Fenster nur erzeugen, nicht zeigen.
            new WindowInteropHelper(window).EnsureHandle();
        }
        else
        {
            window.Show();
            StartWidgets();
        }
    }

    /// <summary>Erzeugt die aktiven Widgets laut Konfiguration und übergibt sie dem Layout-Panel der Leiste.</summary>
    private void CreateWidgets(AppSettings settings)
    {
        if (_window is null) return;

        foreach (var entry in settings.Widgets.Where(w => w.Enabled))
        {
            IBarWidget? widget = entry.Type switch
            {
                WidgetType.Clock => new ClockWidget(),
                WidgetType.AudioOutputs => new AudioDevicesWidget(entry.Id, AudioFlow.Output, AudioService),
                WidgetType.AudioInputs => new AudioDevicesWidget(entry.Id, AudioFlow.Input, AudioService),
                _ => null,
            };
            if (widget is null) continue;

            widget.ApplySettings(settings);
            BarLayoutPanel.SetWidgetId(widget.View, entry.Id);
            _widgets.Add(widget);
            _window.WidgetPanel.Children.Add(widget.View);
        }

        ApplyWidgetLayout(settings);
        _widgetLayout = LayoutSignature(settings);
    }

    /// <summary>Überträgt Bereich, Abstände und Positionen auf die Widget-Ansichten (ohne Neuaufbau).</summary>
    private void ApplyWidgetLayout(AppSettings settings)
    {
        foreach (var widget in _widgets)
        {
            var view = widget.View;
            var entry = settings.Widgets.FirstOrDefault(w => w.Id == BarLayoutPanel.GetWidgetId(view));
            if (entry is null) continue;

            BarLayoutPanel.SetFixedSlot(view, entry.IsFixed ? entry.Slot : null);
            BarLayoutPanel.SetOuterMargin(view, entry.OuterMargin);
            BarLayoutPanel.SetInnerSpacing(view, entry.InnerSpacing);
            BarLayoutPanel.SetPosition(view, entry.Position);
        }
    }

    /// <summary>Drag &amp; Drop auf der Leiste abgeschlossen → neue Position dauerhaft speichern.</summary>
    private void OnWidgetMoved(object? sender, WidgetMovedEventArgs e)
    {
        var settings = _settings.Current.Clone();
        var entry = settings.Widgets.FirstOrDefault(w => w.Id == e.WidgetId);
        if (entry is null) return;
        entry.Position = Math.Round(e.Position, 4);
        Log.Info("Bar", $"Widget {entry.Type} ({entry.Id}) verschoben auf {entry.Position:P1}");
        _settings.Save(settings);
    }

    private void DisposeWidgets()
    {
        foreach (var widget in _widgets) widget.Dispose();
        _widgets.Clear();
        _window?.WidgetPanel.Children.Clear();
    }

    /// <summary>Nur Hinzufügen/Entfernen/Aktivieren erfordert einen Neuaufbau; Position, Bereich und Geräte nicht.</summary>
    private static string LayoutSignature(AppSettings settings) =>
        string.Join(";", settings.Widgets.Where(w => w.Enabled).Select(w => $"{w.Type}#{w.Id}"));

    private AudioDeviceService AudioService => _audioService ??= new AudioDeviceService();

    private void TearDownBar()
    {
        DisposeWidgets();
        _viewModel = null;

        if (_window is not null)
        {
            _window.Close(); // gibt AppBar, Shell-Hook und Sitzungsbenachrichtigung frei
            _window = null;
        }

        _detector.OwnWindow = IntPtr.Zero;
        _monitor = null;
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        if (LayoutSignature(settings) != _widgetLayout)
        {
            // Widgets hinzugefügt, entfernt oder (de)aktiviert → nur die Widgets neu aufbauen, nicht das Fenster.
            DisposeWidgets();
            CreateWidgets(settings);
            if (!_hiddenForFullscreen) StartWidgets();
            Log.Info("Bar", $"Widgets: {_widgetLayout}");
        }
        else
        {
            ApplyWidgetLayout(settings);
        }

        ApplySettings(settings);
        UpdatePlacement(force: true);
        _detector.EvaluateNow();
        ApplyVisibility();
    }

    private void ApplySettings(AppSettings settings)
    {
        Log.Verbose = settings.System.EnableLogging;
        AutoStart.Apply(settings.System.StartWithWindows);

        if (settings.Appearance.Mode == BarMode.Floating && !_floatingWarningLogged)
        {
            Log.Warning("Bar", "Modus 'Floating' ist in Version 1 noch nicht implementiert – 'FullWidth' wird verwendet.");
            _floatingWarningLogged = true;
        }
        if (settings.Behavior.AutoHide && !_autoHideWarningLogged)
        {
            Log.Warning("Bar", "'AutoHide' ist in Version 1 noch nicht implementiert – die Leiste bleibt sichtbar.");
            _autoHideWarningLogged = true;
        }

        _detector.Configure(settings.Behavior);
        _viewModel?.Apply(settings);
        foreach (var widget in _widgets) widget.ApplySettings(settings);

        if (_window is not null)
        {
            _window.Topmost = settings.Behavior.AlwaysOnTop;

            var appBar = _window.AppBar;
            if (appBar is not null && !settings.Behavior.ReserveScreenSpace && appBar.IsReserving)
            {
                // Reservierung aufheben: neu registrieren (nur noch Benachrichtigungen, kein reservierter Bereich).
                appBar.Reset();
                Log.Info("AppBar", "Reservierter Bildschirmbereich freigegeben");
            }
        }
    }

    private void ScheduleLayout(bool force)
    {
        _forceNextLayout |= force;
        _layoutDebounce.Stop();
        _layoutDebounce.Start();
    }

    private void UpdatePlacement(bool force)
    {
        var window = _window;
        if (window is null || window.Handle == IntPtr.Zero) return;

        var settings = _settings.Current;
        var monitor = MonitorService.Resolve(settings.System.MonitorSelection);
        if (monitor is null)
        {
            Log.Warning("Bar", "Kein Monitor gefunden – Positionierung übersprungen");
            return;
        }

        bool monitorChanged = !monitor.IsSameLayout(_monitor);
        _monitor = monitor;
        _detector.Monitor = monitor;

        RECT rect = BarPlacement.Compute(settings.Appearance, monitor);
        if (settings.Behavior.ReserveScreenSpace && window.AppBar is { } appBar)
            rect = appBar.Reserve(rect, force || monitorChanged);

        window.ApplyBounds(rect);
        KeepOnTop();

        if (force || monitorChanged)
            Log.Info("Bar", $"Positioniert auf {monitor.Description}: {rect}");
    }

    private void ApplyVisibility()
    {
        var behavior = _settings.Current.Behavior;
        var state = _detector.State;
        bool hide = state.Kind switch
        {
            FullscreenKind.Exclusive => behavior.HideOnFullscreen,
            FullscreenKind.Borderless => behavior.HideOnBorderlessFullscreen,
            _ => false,
        };

        if (hide == _hiddenForFullscreen) return;
        _hiddenForFullscreen = hide;

        var window = _window;
        if (window is null) return;

        if (hide)
        {
            window.Hide();
            StopWidgets();
            Log.Info("Bar", "Leiste ausgeblendet (Vollbild)");
        }
        else
        {
            StartWidgets();
            window.Show(); // ShowActivated=False → SW_SHOWNA, kein Fokuswechsel
            UpdatePlacement(force: false);
            Log.Info("Bar", "Leiste wieder eingeblendet");
        }
    }

    private void KeepOnTop()
    {
        if (_window is not null && !_hiddenForFullscreen && _settings.Current.Behavior.AlwaysOnTop)
            _window.EnsureTopmost();
    }

    private void OnShellRestarted()
    {
        // Explorer hat alle AppBars vergessen → neu registrieren und (falls aktiv) Bereich erneut reservieren.
        Log.Info("Bar", "Explorer wurde neu gestartet – Leiste wird neu registriert");
        _window?.AppBar?.Reset();
        ScheduleLayout(force: true);
    }

    private void OnSystemTimeChanged()
    {
        TimeZoneInfo.ClearCachedData();
        CultureInfo.CurrentCulture.ClearCachedData();
        foreach (var widget in _widgets) widget.Refresh();
    }

    private void StartWidgets()
    {
        foreach (var widget in _widgets) widget.Start();
    }

    private void StopWidgets()
    {
        foreach (var widget in _widgets) widget.Stop();
    }
}
