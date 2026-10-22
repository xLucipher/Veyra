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
using Veyra.Widgets.Brightness;
using Veyra.Widgets.ClipboardHistory;
using Veyra.Widgets.Clock;
using Veyra.Widgets.Content;
using Veyra.Widgets.Draw;
using Veyra.Widgets.Launcher;
using Veyra.Widgets.Media;
using Veyra.Widgets.Metrics;
using Veyra.Widgets.Network;
using Veyra.Widgets.Timers;
using Veyra.Widgets.Volume;
using Veyra.Widgets.Weather;

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
    private readonly Profiles.ProfileSwitcher _profiles;
    private readonly FullscreenDetector _detector = new();
    private readonly DispatcherTimer _layoutDebounce;
    private readonly DispatcherTimer _remarkTimer;
    private readonly List<IBarWidget> _widgets = [];

    private string _widgetLayout = "";
    private TopBarViewModel? _viewModel;
    private TopBarWindow? _window;
    private MonitorInfo? _monitor;
    private bool _hiddenForFullscreen;
    private IntPtr _fullscreenWindow;
    private FullscreenKind _fullscreenKind;
    private bool _reservationDeferred;
    private IntPtr _fittedWindow;
    private IntPtr _markedWindow;
    private IntPtr _fitGivenUpWindow;
    private long _fitStart;
    private int _fitCount;
    private long _remarkStart;
    private int _remarkCount;
    private bool _forceNextLayout;
    private bool _disposed;

    public BarController(SettingsService settings, string barId, WidgetServices services,
        Action<string> openSettings, Action reload, Action exit, Profiles.ProfileSwitcher profiles)
    {
        _settings = settings;
        BarId = barId;
        _services = services;
        _openSettings = openSettings;
        _reload = reload;
        _exit = exit;
        _profiles = profiles;

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

        _remarkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _remarkTimer.Tick += (_, _) =>
        {
            _remarkTimer.Stop();
            RemarkFittedWindow();
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
        _detector.Evaluated += (_, _) =>
        {
            // Vollbildfenster geschlossen oder minimiert → aufgeschobene Reservierung jetzt nachholen.
            if (_reservationDeferred && !DefersReservation()) UpdatePlacement(force: true);
            // Leiste im Spiel sichtbar: Spiele setzen ihr Fenster gern wieder auf den ganzen Monitor zurück.
            FitGameBelowBar();
        };

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
        _remarkTimer.Stop();
        _detector.Dispose();
        TearDownBar();
    }

    private void BuildBar()
    {
        var settings = _settings.Current;
        var bar = Bar(settings);
        if (bar is null) return;

        _viewModel = new TopBarViewModel(() => _openSettings(BarId), _reload, _exit, ToggleLock,
            _profiles.ShowOverlay, _profiles.SwitchTo, ToggleWidgetLock, DescribeWidget);
        var window = new TopBarWindow(_viewModel, bar.Appearance.IsDynamic, bar.Appearance.NeedsTransparency);
        _window = window;
        window.WidgetPanel.WidgetMoved += OnWidgetMoved;
        window.WidgetPanel.FreePositionsPinned += OnFreePositionsPinned;
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
            // Die Taskleiste hält das eingepasste Spiel nicht mehr für Vollbild (z. B. nach einer Größenänderung) → neu melden.
            if (!opening) ScheduleRemark();
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
                _ when entry.IsMetric => new MetricWidget(entry.Id, entry.Type, _services.SystemMonitor),
                WidgetType.Network => new NetworkWidget(entry.Id, _services.NetworkStatus),
                WidgetType.NetworkTraffic => new TrafficWidget(entry.Id, _services.NetworkTraffic),
                WidgetType.Volume => new VolumeWidget(entry.Id, _services.Volume),
                WidgetType.Timer => new TimerWidget(entry.Id, _services.Timers, UpdateEntry),
                WidgetType.Stopwatch => new StopwatchWidget(entry.Id, _services.Timers),
                WidgetType.Image => new ImageWidget(entry.Id),
                WidgetType.Note => new NoteWidget(entry.Id, UpdateEntry),
                WidgetType.Media => new MediaWidget(entry.Id, _services.Media),
                WidgetType.Countdown => new CountdownWidget(entry.Id),
                WidgetType.Separator => new SeparatorWidget(entry.Id),
                WidgetType.Draw => new DrawWidget(entry.Id, UpdateEntry),
                WidgetType.Clipboard => new ClipboardWidget(entry.Id, _services.Clipboard, UpdateEntry),
                WidgetType.WorldClock => new WorldClockWidget(entry.Id),
                WidgetType.Launcher => new LauncherWidget(entry.Id, UpdateEntry),
                WidgetType.Weather => new WeatherWidget(entry.Id),
                WidgetType.PublicIp => new PublicIpWidget(entry.Id, _services.PublicIp),
                WidgetType.DeviceBattery => new DeviceBatteryWidget(entry.Id, _services.DeviceBattery),
                WidgetType.Brightness => new BrightnessWidget(entry.Id, _services.Brightness, UpdateEntry),
                WidgetType.Fps => new FpsWidget(entry.Id, _services.Fps),
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
        if (_window is not null)
        {
            _window.WidgetPanel.FreePlacement = bar.Appearance.FreeWidgetPlacement;
            // Fixierte Leiste: zu viele Widgets brechen in eine zweite Zeile um, wenn die Leiste hoch genug ist.
            _window.WidgetPanel.AllowWrap = !bar.Appearance.IsDynamic;
        }
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
            BarLayoutPanel.SetFreeLeft(view, entry.FreeLeft ?? double.NaN);
            BarLayoutPanel.SetFreeY(view, entry.FreeY ?? double.NaN);
            BarLayoutPanel.SetFreeOffsetLeft(view, entry.FreeOffsetLeft ?? double.NaN);
            BarLayoutPanel.SetFreeOffsetTop(view, entry.FreeOffsetTop ?? double.NaN);
            BarLayoutPanel.SetIsWidgetLocked(view, entry.Locked);
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
        if (e.Offset is { } offset)
        {
            SetFreeOffset(entry, offset);
            Log.Info("Bar", $"{Name}: Widget {entry.Type} ({entry.Id}) frei platziert, {offset.X:0} px von links / {offset.Y:0} px von oben");
        }
        else
        {
            entry.Position = Math.Round(e.Position, 4);
            Log.Info("Bar", $"{Name}: Widget {entry.Type} ({entry.Id}) verschoben auf {entry.Position:P1}");
        }
        _settings.Save(settings);
    }

    /// <summary>
    /// Freie Platzierung: Widgets ohne festen Abstand (neu oder aus älteren Konfigurationen mit Anteilen der
    /// Flächengröße) wurden dort festgehalten, wo sie stehen → in einem Rutsch speichern.
    /// </summary>
    private void OnFreePositionsPinned(object? sender, FreePositionsPinnedEventArgs e)
    {
        var settings = _settings.Current.Clone();
        var bar = Bar(settings);
        if (bar is null) return;
        int count = 0;
        foreach (var (id, offset) in e.Widgets)
        {
            if (bar.Widgets.FirstOrDefault(w => w.Id == id) is not { } entry) continue;
            SetFreeOffset(entry, offset);
            count++;
        }
        if (count == 0) return;
        Log.Info("Bar", $"{Name}: {count} Widget(s) mit festem Abstand zum Rand übernommen");
        _settings.Save(settings);
    }

    /// <summary>Fester Abstand zum linken und oberen Rand; die alten Anteile der Flächengröße sind damit abgelöst.</summary>
    private static void SetFreeOffset(WidgetEntry entry, System.Windows.Point offset)
    {
        entry.FreeOffsetLeft = Math.Round(offset.X, 1);
        entry.FreeOffsetTop = Math.Round(offset.Y, 1);
        entry.FreeX = null;
        entry.FreeLeft = null;
        entry.FreeY = null;
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

        // Fenster weg (Beenden, Leiste entfernt, Neuaufbau z. B. beim Profilwechsel fixiert → dynamisch): Mit ihm
        // endet die Reservierung – ein darunter eingepasstes Spiel bekommt den ganzen Monitor zurück.
        RestoreFittedWindow();

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
        _viewModel?.ApplyProfiles(settings);
        foreach (var widget in _widgets) widget.ApplySettings(settings, bar);

        if (_window is not null)
        {
            // "Position sperren" gilt in jedem Modus für die Widgets, im dynamischen Modus zusätzlich für das Fenster.
            _window.WidgetPanel.IsLocked = bar.Appearance.LockPosition;
            if (_window.IsDynamic)
            {
                _window.Layer = bar.Appearance.Layer;
                _window.IsLocked = bar.Appearance.LockPosition;
                _window.ApplyLayer();
            }
            else
            {
                _window.Topmost = bar.Appearance.AlwaysOnTop != false;
            }

            var appBar = _window.AppBar;
            if (appBar is not null && !ReservesScreenSpace(settings, bar) && appBar.IsReserving)
            {
                // Reservierung aufheben: neu registrieren (nur noch Benachrichtigungen, kein reservierter Bereich).
                appBar.Reset();
                Log.Info("AppBar", $"{bar.Name}: Reservierter Bildschirmbereich freigegeben");
                RestoreFittedWindow();
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
        _reservationDeferred = false;
        if (ReservesScreenSpace(settings, bar) && window.AppBar is { } appBar)
        {
            if (DefersReservation())
            {
                // Ein Spiel läuft (evtl. per Alt+Tab im Hintergrund), in dem die Leiste ausgeblendet wird: Den Arbeitsbereich
                // jetzt zu ändern würde sein Vollbildfenster unter die Leiste schieben. Erst nachholen, wenn es weg ist.
                _reservationDeferred = !appBar.IsReserving || force || monitorChanged;
                if (appBar.IsReserving) rect = appBar.ReservedRect;
            }
            else
            {
                rect = appBar.Reserve(rect, force || monitorChanged);
            }
        }
        _detector.ReservedArea = window.AppBar is { IsReserving: true } reserving ? reserving.ReservedRect : null;

        window.ApplyBounds(rect);
        KeepOnTop();
        if (window.IsDynamic) window.ApplyLayer();

        if (force || monitorChanged)
            Log.Info("Bar", $"{bar.Name}: positioniert auf {monitor.Description}: {rect}");
    }

    private void ApplyVisibility()
    {
        var state = _detector.State;
        bool hide = HidesFor(state.Kind, _settings.Current.Behavior);

        // Merken, solange es lebt – auch nach Alt+Tab auf den Desktop (siehe UpdatePlacement).
        if (state.Kind != FullscreenKind.None) (_fullscreenWindow, _fullscreenKind) = (state.Window, state.Kind);
        else _fitGivenUpWindow = IntPtr.Zero; // Vollbild verlassen → beim nächsten Mal wieder versuchen
        if (hide && state.Squeezed) RestoreSqueezedWindow(state.Window);

        if (hide == _hiddenForFullscreen)
        {
            if (!hide) FitGameBelowBar();
            return;
        }
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
            FitGameBelowBar();
        }
    }

    private static bool HidesFor(FullscreenKind kind, BehaviorSettings behavior) => kind switch
    {
        FullscreenKind.Exclusive => behavior.HideOnFullscreen,
        FullscreenKind.Borderless => behavior.HideOnBorderlessFullscreen,
        _ => false,
    };

    /// <summary>Reservierung aufschieben: Ein Spiel, in dem die Leiste ausgeblendet wird, läuft noch.</summary>
    private bool DefersReservation() =>
        FullscreenWindowAlive() && HidesFor(_fullscreenKind, _settings.Current.Behavior);

    /// <summary>
    /// Leiste im Spiel sichtbar und Bildschirmbereich reserviert: Randlose Spiele ignorieren den Arbeitsbereich und
    /// liegen sonst hinter der Leiste. Deshalb das Spielfenster selbst unter die Leiste legen
    /// (Monitorhöhe minus Leiste, z. B. 1920×1080 mit 40 px Leiste → 1920×1040).
    /// </summary>
    private void FitGameBelowBar()
    {
        var state = _detector.State;
        if (state.Kind != FullscreenKind.Borderless || _hiddenForFullscreen || _monitor is not { } monitor) return;
        if (_window?.AppBar is not { IsReserving: true } appBar) return;
        if (state.Window == _fitGivenUpWindow) return; // wehrt sich dauerhaft (z. B. Browser-Vollbild) – in Ruhe lassen

        var b = monitor.Bounds;
        var target = new RECT(b.Left, appBar.ReservedRect.Bottom, b.Right, b.Bottom);
        if (!NativeMethods.GetWindowRect(state.Window, out var current)) return;
        // Das Fenster deckt den Monitor nicht mehr ab → Windows hielte es nicht mehr für Vollbild und ließe die
        // Taskleiste darüber stehen. Deshalb ausdrücklich als Vollbild melden.
        MarkFullscreen(state.Window);
        // Immer merken – auch wenn es schon passt (z. B. nach einem Neustart von Veyra) –, damit es beim Ende
        // der Reservierung zuverlässig zurückbekommt, was ihm gehört.
        bool otherWindow = state.Window != _fittedWindow;
        _fittedWindow = state.Window;
        if (current.Equals(target)) return;

        // Setzt das Fenster seine Größe ständig zurück (3× in 10 Sekunden), nicht dagegen ankämpfen: Für dieses
        // Fenster aufgeben, bis es das Vollbild verlässt. Es liegt dann wie ohne Reservierung unter der Leiste.
        var now = Environment.TickCount64;
        if (otherWindow || now - _fitStart > 10_000) (_fitStart, _fitCount) = (now, 0);
        if (++_fitCount > 3)
        {
            _fitGivenUpWindow = state.Window;
            _fittedWindow = IntPtr.Zero;
            UnmarkFullscreen(); // deckt den Monitor selbst ab – Windows erkennt das Vollbild wieder allein
            Log.Warning("Bar", $"{Name}: Fenster setzt seine Größe immer wieder zurück – Einpassen für dieses Fenster aufgegeben: " +
                               WindowInspector.Describe(state.Window));
            return;
        }

        bool moved = NativeMethods.SetWindowPos(state.Window, IntPtr.Zero, target.Left, target.Top, target.Width, target.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        Log.Info("Bar", $"{Name}: Spielfenster unter die Fläche gelegt {target} – " +
                        $"{(moved ? "ok" : "fehlgeschlagen")}: {WindowInspector.Describe(state.Window)}");
        // Solange das Fenster den Monitor noch abdeckte, hielt die Taskleiste es ohnehin für Vollbild und hat die
        // Meldung verworfen. Nach dem Verkleinern prüft sie neu und käme wieder nach vorne → jetzt erneut melden.
        if (moved) ScheduleRemark();
    }

    /// <summary>Eingepasstes Spielfenster in Kürze erneut als Vollbild melden (nachdem die Taskleiste die Größenänderung verarbeitet hat).</summary>
    private void ScheduleRemark()
    {
        if (_markedWindow == IntPtr.Zero || _markedWindow != _fittedWindow) return;
        _remarkTimer.Stop();
        _remarkTimer.Start();
    }

    private void RemarkFittedWindow()
    {
        var hwnd = _markedWindow;
        if (hwnd == IntPtr.Zero || hwnd != _fittedWindow || !NativeMethods.IsWindow(hwnd)) return;

        // Höchstens 5× in 10 Sekunden, falls Taskleiste und Spiel sich gegenseitig immer wieder umstimmen.
        var now = Environment.TickCount64;
        if (now - _remarkStart > 10_000) (_remarkStart, _remarkCount) = (now, 0);
        if (++_remarkCount > 5) return;

        if (TaskbarList.MarkFullscreen(hwnd, true))
            Log.Debug("Bar", $"{Name}: Spielfenster erneut als Vollbild gemeldet: {WindowInspector.Describe(hwnd)}");
    }

    /// <summary>Spielfenster bei der Windows-Taskleiste als Vollbild melden (höchstens eins gleichzeitig).</summary>
    private void MarkFullscreen(IntPtr hwnd)
    {
        if (hwnd == _markedWindow) return;
        UnmarkFullscreen();
        if (TaskbarList.MarkFullscreen(hwnd, true))
        {
            _markedWindow = hwnd;
            Log.Info("Bar", $"{Name}: Spielfenster bei der Taskleiste als Vollbild gemeldet: {WindowInspector.Describe(hwnd)}");
        }
    }

    private void UnmarkFullscreen()
    {
        var hwnd = _markedWindow;
        _markedWindow = IntPtr.Zero;
        if (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd)) TaskbarList.MarkFullscreen(hwnd, false);
    }

    /// <summary>Ein von <see cref="FitGameBelowBar"/> verkleinertes Spielfenster wieder auf den ganzen Monitor legen.</summary>
    private void RestoreFittedWindow()
    {
        _remarkTimer.Stop();
        UnmarkFullscreen();
        var hwnd = _fittedWindow;
        _fittedWindow = IntPtr.Zero;
        if (hwnd == IntPtr.Zero || _monitor is not { } monitor || !NativeMethods.IsWindow(hwnd)) return;
        var b = monitor.Bounds;
        // Nur zurückgeben, solange es noch so liegt, wie die Leiste es hingelegt hat (volle Breite, unten bündig,
        // oben eingerückt) – ein inzwischen anders genutztes Fenster (z. B. Spiel im Fenstermodus) bleibt, wie es ist.
        if (!NativeMethods.GetWindowRect(hwnd, out var r) ||
            r.Left > b.Left || r.Right < b.Right || r.Bottom < b.Bottom || r.Top <= b.Top || r.Top > b.Top + b.Height / 4)
            return;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, b.Left, b.Top, b.Width, b.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        Log.Info("Bar", $"{Name}: Spielfenster wieder monitorfüllend: {WindowInspector.Describe(hwnd)}");
    }

    /// <summary>Lebt das zuletzt erkannte Vollbildfenster noch (sichtbar, nicht minimiert)?</summary>
    private bool FullscreenWindowAlive()
    {
        var hwnd = _fullscreenWindow;
        if (hwnd == IntPtr.Zero) return false;
        if (NativeMethods.IsWindow(hwnd) && NativeMethods.IsWindowVisible(hwnd) && !NativeMethods.IsIconic(hwnd)) return true;
        _fullscreenWindow = IntPtr.Zero;
        return false;
    }

    /// <summary>
    /// Unsere Reservierung hat das Vollbildfenster eines Spiels unter die Leiste geschoben. Da die Leiste ausgeblendet
    /// wird, Reservierung freigeben und das Fenster wieder auf den ganzen Monitor legen.
    /// </summary>
    private void RestoreSqueezedWindow(IntPtr hwnd)
    {
        if (_monitor is not { } monitor) return;
        if (hwnd == _fittedWindow) _fittedWindow = IntPtr.Zero;
        UnmarkFullscreen();
        if (_window?.AppBar is { IsReserving: true } appBar)
        {
            appBar.Reset();
            _detector.ReservedArea = null;
            _reservationDeferred = true;
        }
        var b = monitor.Bounds;
        bool moved = NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, b.Left, b.Top, b.Width, b.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        Log.Info("Bar", $"{Name}: Vollbildfenster war unter die Fläche geschoben – Reservierung freigegeben, " +
                        $"Fenster {(moved ? "wieder monitorfüllend" : "konnte nicht verschoben werden")}: {WindowInspector.Describe(hwnd)}");
    }

    private void KeepOnTop()
    {
        if (ShouldStayOnTop()) _window!.EnsureTopmost();
    }

    /// <summary>
    /// Die Windows-Taskleiste holt sich beim Anklicken selbst nach vorne, und zwar erst nach dem Vordergrundwechsel.
    /// Eine Leiste im Vordergrund, die über der Taskleiste liegt, würde dann verschwinden → wieder darüber legen.
    /// Dasselbe gilt für Spiele, die sich selbst als Topmost nach vorne holen, wenn die Leiste im Spiel sichtbar bleibt.
    /// </summary>
    private void StayAboveTaskbar()
    {
        if (!ShouldStayOnTop() || !_window!.IsVisible) return;
        var state = _detector.State;
        if (_window.IsBelowTaskbar())
        {
            _window.EnsureTopmost();
        }
        else if (state.Kind != FullscreenKind.None && _window.IsBelow(state.Window))
        {
            // Holt sich das Spiel jedes Mal sofort wieder nach vorne, nicht endlos dagegen ankämpfen (max. 5× pro Sekunde).
            var now = Environment.TickCount64;
            if (now - _raiseWindowStart > 1000) (_raiseWindowStart, _raiseCount) = (now, 0);
            if (++_raiseCount <= 5) _window.EnsureTopmost();
        }
    }

    private long _raiseWindowStart;
    private int _raiseCount;

    /// <summary>Leiste sichtbar und im Vordergrund gewünscht (fixiert: "Immer im Vordergrund", dynamisch: Ebene Vordergrund).</summary>
    private bool ShouldStayOnTop()
    {
        if (_window is null || _hiddenForFullscreen) return false;
        var settings = _settings.Current;
        var bar = Bar(settings);
        if (bar is null) return false;
        return _window.IsDynamic
            ? bar.Appearance.Layer == WindowLayer.Topmost
            : bar.Appearance.AlwaysOnTop != false;
    }

    /// <summary>Bildschirmbereich reservieren gibt es nur im fixierten Modus.</summary>
    private static bool ReservesScreenSpace(AppSettings settings, BarSettings bar) =>
        !bar.Appearance.IsDynamic && bar.Appearance.ReserveScreenSpace != false;

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

    /// <summary>Kontextmenü "Widget sperren/entsperren": nur dieses eine Widget.</summary>
    private void ToggleWidgetLock(string widgetId)
    {
        var settings = _settings.Current.Clone();
        var entry = Bar(settings)?.Widgets.FirstOrDefault(w => w.Id == widgetId);
        if (entry is null) return;
        entry.Locked = !entry.Locked;
        Log.Info("Bar", $"{Name}: Widget {entry.Type} ({entry.Id}) {(entry.Locked ? "gesperrt" : "entsperrt")}");
        _settings.Save(settings);
    }

    /// <summary>Name und Sperrzustand eines Widgets für das Kontextmenü; null = unbekannt.</summary>
    private WidgetMenuInfo? DescribeWidget(string widgetId)
    {
        var entry = Bar(_settings.Current)?.Widgets.FirstOrDefault(w => w.Id == widgetId);
        return entry is null ? null : new WidgetMenuInfo(SettingsUI.WidgetCatalog.For(entry.Type, entry.Volume).Name, entry.Locked);
    }

    private void OnShellRestarted()
    {
        // Explorer hat alle AppBars vergessen → neu registrieren und (falls aktiv) Bereich erneut reservieren.
        Log.Info("Bar", $"{Name}: Explorer wurde neu gestartet – Fläche wird neu registriert");
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
