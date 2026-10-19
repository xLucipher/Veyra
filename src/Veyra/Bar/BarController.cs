using System.Globalization;
using System.Windows.Interop;
using System.Windows.Threading;
using Veyra.Audio;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Fullscreen;
using Veyra.Interop;
using Veyra.Monitors;
using Veyra.Widgets;
using Veyra.Widgets.Audio;
using Veyra.Widgets.Clock;
using Veyra.Widgets.Metrics;
using Veyra.Widgets.Network;
using Veyra.Widgets.Timers;
using Veyra.Widgets.Volume;

namespace Veyra.Bar;

/// <summary>
/// Steuert genau eine Leiste (<see cref="BarSettings"/>): Fenster und Widgets erzeugen, Einstellungen anwenden,
/// positionieren, auf Systemereignisse reagieren und bei Vollbild auf ihrem Monitor aus-/einblenden.
/// Jede Leiste hat ihre eigene Vollbild-Erkennung, weil sie auf einem anderen Monitor liegen kann.
/// </summary>
public sealed class BarController : IDisposable
{
    private readonly SettingsService _settings;
    private readonly WidgetServices _services;
    private readonly Action<string> _openSettings;
    private readonly Action _reload;
    private readonly Action _exit;
    private readonly FullscreenDetector _detector = new();
    private readonly DispatcherTimer _layoutDebounce;
    private readonly List<IBarWidget> _widgets = [];

    private string _widgetLayout = "";
    private TopBarViewModel? _viewModel;
    private TopBarWindow? _window;
    private MonitorInfo? _monitor;
    private bool _hiddenForFullscreen;
    private bool _forceNextLayout;
    private bool _disposed;

    public BarController(SettingsService settings, string barId, WidgetServices services,
        Action<string> openSettings, Action reload, Action exit)
    {
        _settings = settings;
        BarId = barId;
        _services = services;
        _openSettings = openSettings;
        _reload = reload;
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

    public string BarId { get; }

    /// <summary>True, solange die Leiste wegen einer Vollbildanwendung ausgeblendet ist (dann nicht stören).</summary>
    public bool IsHiddenForFullscreen => _hiddenForFullscreen;

    /// <summary>Einstellungen dieser Leiste; null, wenn sie gerade entfernt wurde.</summary>
    private BarSettings? Bar(AppSettings settings) => settings.FindBar(BarId);

    private string Name => Bar(_settings.Current)?.Name ?? BarId;

    public void Start()
    {
        _detector.StateChanged += (_, _) => ApplyVisibility();
        _detector.ForegroundChanged += (_, _) => KeepOnTop();
        // Sofort prüfen, nicht gebündelt: Jede Verzögerung wäre als kurzes Aufblitzen der Taskleiste sichtbar.
        _detector.ZOrderChanged += (_, _) => StayAboveTaskbar();

        BuildBar();
        _detector.Start();
    }

    /// <summary>Fenster samt Widgets neu aufbauen (Kontextmenü "Leiste neu laden").</summary>
    public void Rebuild()
    {
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
        _layoutDebounce.Stop();
        _detector.Dispose();
        TearDownBar();
    }

    private void BuildBar()
    {
        var settings = _settings.Current;
        var bar = Bar(settings);
        if (bar is null) return;

        _viewModel = new TopBarViewModel(() => _openSettings(BarId), _reload, _exit, ToggleLock);
        var window = new TopBarWindow(_viewModel, bar.Appearance.IsDynamic, bar.Appearance.NeedsTransparency);
        _window = window;
        window.WidgetPanel.WidgetMoved += OnWidgetMoved;
        window.UserBoundsChanged += OnUserBoundsChanged;
        CreateWidgets(settings, bar);

        window.SourceInitialized += (_, _) =>
        {
            _detector.OwnWindow = window.Handle;
            ApplySettings(settings, bar);
            UpdatePlacement(force: true);
        };
        window.DisplayConfigurationChanged += (_, _) => ScheduleLayout(force: true);
        window.AppBarPositionChanged += (_, _) => ScheduleLayout(force: false);
        window.SessionUnlocked += (_, _) => ScheduleLayout(force: true);
        window.ShellRestarted += (_, _) => OnShellRestarted();
        window.SystemTimeChanged += (_, _) => OnSystemTimeChanged();
        window.HotkeyPressed += (_, id) => OnHotkeyPressed(id);
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
    private void CreateWidgets(AppSettings settings, BarSettings bar)
    {
        if (_window is null) return;

        foreach (var entry in bar.Widgets.Where(w => w.Enabled))
        {
            IBarWidget? widget = entry.Type switch
            {
                WidgetType.Clock => new ClockWidget(entry.Id),
                WidgetType.AudioOutputs => new AudioDevicesWidget(entry.Id, AudioFlow.Output, _services.Audio),
                WidgetType.AudioInputs => new AudioDevicesWidget(entry.Id, AudioFlow.Input, _services.Audio),
                WidgetType.Cpu or WidgetType.Gpu or WidgetType.Ram or WidgetType.GpuTemperature =>
                    new MetricWidget(entry.Id, entry.Type, _services.SystemMonitor),
                WidgetType.Network => new NetworkWidget(entry.Id, _services.NetworkStatus),
                WidgetType.NetworkTraffic => new TrafficWidget(entry.Id, _services.NetworkTraffic),
                WidgetType.Volume => new VolumeWidget(entry.Id, _services.Volume),
                WidgetType.Timer => new TimerWidget(entry.Id, _services.Timers, UpdateEntry),
                WidgetType.Stopwatch => new StopwatchWidget(entry.Id, _services.Timers),
                _ => null,
            };
            if (widget is null) continue;

            widget.ApplySettings(settings, bar);
            BarLayoutPanel.SetWidgetId(widget.View, entry.Id);
            _widgets.Add(widget);
            _window.WidgetPanel.Children.Add(widget.View);
        }

        ApplyWidgetLayout(bar);
        _widgetLayout = LayoutSignature(bar);
    }

    /// <summary>Überträgt Bereich, Abstände und Positionen auf die Widget-Ansichten (ohne Neuaufbau).</summary>
    private void ApplyWidgetLayout(BarSettings bar)
    {
        if (_window is not null) _window.WidgetPanel.FreePlacement = bar.Appearance.FreeWidgetPlacement;
        foreach (var widget in _widgets)
        {
            var view = widget.View;
            var entry = bar.Widgets.FirstOrDefault(w => w.Id == BarLayoutPanel.GetWidgetId(view));
            if (entry is null) continue;

            BarLayoutPanel.SetFixedSlot(view, entry.IsFixed ? entry.Slot : null);
            BarLayoutPanel.SetOuterMargin(view, entry.OuterMargin);
            BarLayoutPanel.SetInnerSpacing(view, entry.InnerSpacing);
            BarLayoutPanel.SetPosition(view, entry.Position);
            BarLayoutPanel.SetFreeX(view, entry.FreeX ?? double.NaN);
            BarLayoutPanel.SetFreeY(view, entry.FreeY ?? double.NaN);
            // LayoutTransform statt RenderTransform: Das Layout rechnet mit der vergrößerten Fläche (keine Überlappung im normalen Modus).
            view.LayoutTransform = Math.Abs(entry.Scale - 1) < 0.001
                ? System.Windows.Media.Transform.Identity
                : new System.Windows.Media.ScaleTransform(entry.Scale, entry.Scale);
        }
    }

    /// <summary>Drag &amp; Drop auf der Leiste abgeschlossen → neue Position dauerhaft speichern.</summary>
    private void OnWidgetMoved(object? sender, WidgetMovedEventArgs e)
    {
        var settings = _settings.Current.Clone();
        var entry = Bar(settings)?.Widgets.FirstOrDefault(w => w.Id == e.WidgetId);
        if (entry is null) return;
        if (e.Free is { } free)
        {
            entry.FreeX = Math.Round(free.X, 4);
            entry.FreeY = Math.Round(free.Y, 4);
            Log.Info("Bar", $"{Name}: Widget {entry.Type} ({entry.Id}) frei platziert auf {entry.FreeX:P1} / {entry.FreeY:P1}");
        }
        else
        {
            entry.Position = Math.Round(e.Position, 4);
            Log.Info("Bar", $"{Name}: Widget {entry.Type} ({entry.Id}) verschoben auf {entry.Position:P1}");
        }
        _settings.Save(settings);
    }

    /// <summary>Ein Widget ändert seinen eigenen Eintrag (z. B. Timer im Popup bearbeitet) → dauerhaft speichern.</summary>
    private void UpdateEntry(string entryId, Action<WidgetEntry> change)
    {
        var settings = _settings.Current.Clone();
        var entry = Bar(settings)?.Widgets.FirstOrDefault(w => w.Id == entryId);
        if (entry is null) return;
        change(entry);
        _settings.Save(settings);
    }

    private void DisposeWidgets()
    {
        foreach (var widget in _widgets) widget.Dispose();
        _widgets.Clear();
        _window?.WidgetPanel.Children.Clear();
    }

    /// <summary>Nur Hinzufügen/Entfernen/Aktivieren erfordert einen Neuaufbau; Position, Bereich und Geräte nicht.</summary>
    private static string LayoutSignature(BarSettings bar) =>
        string.Join(";", bar.Widgets.Where(w => w.Enabled).Select(w => $"{w.Type}#{w.Id}"));

    // ---- Tastenkürzel der Widgets -----------------------------------------------------------

    /// <summary>
    /// Angemeldete Tastenkürzel: ID → Widget-Eintrag und Aktion (null = Gerätewechsel eines Audio-Widgets).
    /// IDs sind über alle Leisten eindeutig.
    /// </summary>
    private readonly Dictionary<int, (string EntryId, string? Action)> _hotkeyTargets = [];
    private static int _nextHotkeyId;

    /// <summary>Alle Tastenkürzel dieser Leiste abmelden (vor dem Neuanmelden aller Leisten).</summary>
    public void UnregisterHotkeys()
    {
        _window?.UnregisterHotkeys();
        _hotkeyTargets.Clear();
    }

    /// <summary>Tastenkürzel der aktiven Widgets anmelden. Belegte Kürzel werden protokolliert und übersprungen.</summary>
    public void RegisterHotkeys()
    {
        var bar = Bar(_settings.Current);
        var window = _window;
        if (window is null || bar is null) return;

        foreach (var entry in bar.Widgets.Where(w => w.Enabled))
        {
            if (entry.Hotkey is not null) Register(entry, null, entry.Hotkey, repeat: false);
            foreach (var (action, text) in entry.Hotkeys ?? [])
                Register(entry, action, text, WidgetHotkeys.Find(entry.Type, action)?.Repeat ?? false);
        }

        void Register(WidgetEntry entry, string? action, string text, bool repeat)
        {
            if (!Hotkey.TryParse(text, out var hotkey)) return;
            string target = action is null ? $"Widget {entry.Type} ({entry.Id})" : $"Widget {entry.Type} ({entry.Id}): {action}";
            int id = _nextHotkeyId = (_nextHotkeyId + 1) % 0xC000; // Anwendungs-IDs: 0x0000–0xBFFF
            if (window.RegisterHotkey(id, hotkey, repeat))
            {
                _hotkeyTargets[id] = (entry.Id, action);
                Log.Info("Hotkey", $"{bar.Name}: {hotkey.DisplayText} → {target}");
            }
            else
            {
                Log.Warning("Hotkey", $"{bar.Name}: {hotkey.DisplayText} ist bereits belegt (anderes Programm oder Widget) – {target} ohne Tastenkürzel");
            }
        }
    }

    private void OnHotkeyPressed(int id)
    {
        if (!_hotkeyTargets.TryGetValue(id, out var target)) return;
        // Auch während die Leiste bei Vollbild ausgeblendet ist: Die Widgets existieren weiter, nur ohne Aktualisierung.
        if (target.Action is null)
        {
            _widgets.OfType<AudioDevicesWidget>().FirstOrDefault(w => w.EntryId == target.EntryId)?.SelectNextDevice();
            return;
        }
        _widgets.OfType<IHotkeyTarget>().FirstOrDefault(w => w.EntryId == target.EntryId)?.OnHotkey(target.Action);
    }

    private void TearDownBar()
    {
        _hotkeyTargets.Clear();
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

    /// <summary>Geänderte Einstellungen übernehmen (vom <see cref="BarManager"/> aufgerufen).</summary>
    public void OnSettingsChanged(AppSettings settings)
    {
        var bar = Bar(settings);
        if (bar is null) return;

        if (_window is not null && _window.IsDynamic != bar.Appearance.IsDynamic)
        {
            // Moduswechsel fixiert ↔ dynamisch: anderes Fenster (Transparenz) → Leiste neu aufbauen.
            Log.Info("Bar", $"{bar.Name}: Modus gewechselt: {(bar.Appearance.IsDynamic ? "dynamisch" : "fixiert")}");
            Rebuild();
            return;
        }

        if (_window is not null && _window.IsTransparent != bar.Appearance.NeedsTransparency)
        {
            // Fixierte Leiste: Deckkraft 100 % ↔ darunter braucht ein anderes Fenster → Leiste neu aufbauen.
            Log.Info("Bar", $"{bar.Name}: Transparenz {(bar.Appearance.NeedsTransparency ? "an" : "aus")}");
            Rebuild();
            return;
        }

        if (LayoutSignature(bar) != _widgetLayout)
        {
            // Widgets hinzugefügt, entfernt oder (de)aktiviert → nur die Widgets neu aufbauen, nicht das Fenster.
            DisposeWidgets();
            CreateWidgets(settings, bar);
            if (!_hiddenForFullscreen) StartWidgets();
            Log.Info("Bar", $"{bar.Name}: Widgets: {_widgetLayout}");
        }
        else
        {
            ApplyWidgetLayout(bar);
        }

        ApplySettings(settings, bar);
        UpdatePlacement(force: true);
        _detector.EvaluateNow();
        ApplyVisibility();
    }

    private void ApplySettings(AppSettings settings, BarSettings bar)
    {
        _detector.Configure(settings.Behavior);
        _viewModel?.Apply(bar);
        foreach (var widget in _widgets) widget.ApplySettings(settings, bar);

        if (_window is not null)
        {
            if (_window.IsDynamic)
            {
                _window.Layer = bar.Appearance.Layer;
                _window.IsLocked = bar.Appearance.LockPosition;
                _window.ApplyLayer();
            }
            else
            {
                _window.Topmost = settings.Behavior.AlwaysOnTop;
            }

            var appBar = _window.AppBar;
            if (appBar is not null && !ReservesScreenSpace(settings, bar) && appBar.IsReserving)
            {
                // Reservierung aufheben: neu registrieren (nur noch Benachrichtigungen, kein reservierter Bereich).
                appBar.Reset();
                Log.Info("AppBar", $"{bar.Name}: Reservierter Bildschirmbereich freigegeben");
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
        var bar = Bar(settings);
        if (bar is null) return;

        var monitor = MonitorService.Resolve(bar.MonitorSelection);
        if (monitor is null)
        {
            Log.Warning("Bar", $"{bar.Name}: Kein Monitor gefunden – Positionierung übersprungen");
            return;
        }

        bool monitorChanged = !monitor.IsSameLayout(_monitor);
        _monitor = monitor;
        _detector.Monitor = monitor;

        RECT rect = BarPlacement.Compute(bar.Appearance, monitor);
        if (ReservesScreenSpace(settings, bar) && window.AppBar is { } appBar)
            rect = appBar.Reserve(rect, force || monitorChanged);

        window.ApplyBounds(rect);
        KeepOnTop();
        if (window.IsDynamic) window.ApplyLayer();

        if (force || monitorChanged)
            Log.Info("Bar", $"{bar.Name}: positioniert auf {monitor.Description}: {rect}");
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
            Log.Info("Bar", $"{Name}: ausgeblendet (Vollbild)");
        }
        else
        {
            StartWidgets();
            window.Show(); // ShowActivated=False → SW_SHOWNA, kein Fokuswechsel
            UpdatePlacement(force: false);
            Log.Info("Bar", $"{Name}: wieder eingeblendet");
        }
    }

    private void KeepOnTop()
    {
        if (ShouldStayOnTop()) _window!.EnsureTopmost();
    }

    /// <summary>
    /// Die Windows-Taskleiste holt sich beim Anklicken selbst nach vorne, und zwar erst nach dem Vordergrundwechsel.
    /// Eine Leiste im Vordergrund, die über der Taskleiste liegt, würde dann verschwinden → wieder darüber legen.
    /// </summary>
    private void StayAboveTaskbar()
    {
        if (ShouldStayOnTop() && _window!.IsVisible && _window.IsBelowTaskbar()) _window.EnsureTopmost();
    }

    /// <summary>Leiste sichtbar und im Vordergrund gewünscht (fixiert: "Immer im Vordergrund", dynamisch: Ebene Vordergrund).</summary>
    private bool ShouldStayOnTop()
    {
        if (_window is null || _hiddenForFullscreen) return false;
        var settings = _settings.Current;
        var bar = Bar(settings);
        if (bar is null) return false;
        return _window.IsDynamic
            ? bar.Appearance.Layer == WindowLayer.Topmost
            : settings.Behavior.AlwaysOnTop;
    }

    /// <summary>Bildschirmbereich reservieren gibt es nur im fixierten Modus.</summary>
    private static bool ReservesScreenSpace(AppSettings settings, BarSettings bar) =>
        !bar.Appearance.IsDynamic && settings.Behavior.ReserveScreenSpace;

    /// <summary>
    /// Dynamischer Modus: Der Benutzer hat die Leiste verschoben oder ihre Größe geändert → speichern.
    /// Landet sie auf einem anderen Monitor, wird dieser zum Monitor der Leiste.
    /// </summary>
    private void OnUserBoundsChanged(object? sender, RECT rect)
    {
        var hMonitor = NativeMethods.MonitorFromRect(ref rect, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var monitor = MonitorService.Query(hMonitor) ?? _monitor;
        if (monitor is null) return;

        var settings = _settings.Current.Clone();
        var bar = Bar(settings);
        if (bar is null) return;

        double scale = monitor.Scale;
        var a = bar.Appearance;
        a.FloatingLeft = Math.Round((rect.Left - monitor.Bounds.Left) / scale, 1);
        a.FloatingTop = Math.Round((rect.Top - monitor.Bounds.Top) / scale, 1);
        a.Width = (int)Math.Round(rect.Width / scale);
        a.FloatingHeight = (int)Math.Round(rect.Height / scale);

        if (_monitor is not null && monitor.DeviceName != _monitor.DeviceName)
            bar.MonitorSelection = monitor.IsPrimary ? MonitorService.PrimarySelection : monitor.DeviceName;

        Log.Info("Bar", $"{bar.Name}: {a.Width}×{a.FloatingHeight} bei {a.FloatingLeft},{a.FloatingTop} auf {monitor.DeviceName}");
        _settings.Save(settings);
    }

    /// <summary>Kontextmenü "Position sperren/entsperren".</summary>
    private void ToggleLock()
    {
        var settings = _settings.Current.Clone();
        var bar = Bar(settings);
        if (bar is null) return;
        bar.Appearance.LockPosition = !bar.Appearance.LockPosition;
        _settings.Save(settings);
    }

    private void OnShellRestarted()
    {
        // Explorer hat alle AppBars vergessen → neu registrieren und (falls aktiv) Bereich erneut reservieren.
        Log.Info("Bar", $"{Name}: Explorer wurde neu gestartet – Leiste wird neu registriert");
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
