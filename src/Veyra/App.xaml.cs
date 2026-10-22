using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Veyra.Bar;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.SettingsUI;
using Veyra.Updates;

namespace Veyra;

/// <summary>Einstiegspunkt: Einzelinstanz, Logging, Konfiguration und Leiste verdrahten.</summary>
public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private SettingsService? _settings;
    private BarManager? _bar;
    private UpdateManager? _updates;
    private SettingsWindow? _settingsWindow;
    private bool _cleanedUp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Erhöhter Hilfsschritt für die FPS-Messung (Button bzw. Installer): nur Konto aufnehmen, nichts sonst starten.
        int grant = Array.FindIndex(e.Args, a => a.Equals(Gaming.FpsPermission.GrantArgument, StringComparison.OrdinalIgnoreCase));
        if (grant >= 0)
        {
            Shutdown(Gaming.FpsPermission.GrantFromCommandLine(e.Args.ElementAtOrDefault(grant + 1)));
            return;
        }

        bool exitRequested = e.Args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase));
        _singleInstance = new SingleInstance();
        if (exitRequested || !_singleInstance.TryAcquire())
        {
            // "--exit" → laufende Instanz sauber beenden; sonst läuft sie bereits → dort die Einstellungen öffnen.
            if (exitRequested) SingleInstance.SignalExit();
            else SingleInstance.SignalExistingInstance();
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown(0);
            return;
        }

        Log.Initialize(Path.Combine(SettingsService.DirectoryPath, "logs"));
        RegisterExceptionHandlers();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        Log.Info("App", $"Veyra {version} startet (PID {Environment.ProcessId}, {RuntimeInformation.FrameworkDescription})");

        _settings = new SettingsService();
        _settings.Load();
        Log.Verbose = _settings.Current.System.EnableLogging;

        if (AutoStart.AdoptExistingEntry() && !_settings.Current.System.StartWithWindows)
        {
            var adopted = _settings.Current.Clone();
            adopted.System.StartWithWindows = true;
            _settings.Save(adopted);
        }

        if (_settings.Current.System.UseSoftwareRendering)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        _bar = new BarManager(_settings, ShowSettings, ShowProfileSettings, ExitApplication);
        _bar.Start();

        // Update-Prüfung über GitHub Releases – nie während eines Vollbildspiels.
        _updates = new UpdateManager(_settings, () => _bar.IsHiddenForFullscreen, ExitApplication);
        _updates.Start();

        _singleInstance.ListenForActivation(
            () => Dispatcher.BeginInvoke(() => ShowSettings(null)),
            () => Dispatcher.BeginInvoke(ExitApplication));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    /// <param name="barId">Diese Leiste in den Einstellungen auswählen (Rechtsklick auf eine Leiste); null = unverändert.</param>
    private void ShowSettings(string? barId)
    {
        if (_settings is null) return;

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(new SettingsViewModel(_settings, ExitApplication,
                () => _updates?.CheckNowAsync() ?? Task.FromResult("Update-Dienst nicht verfügbar."),
                () => _bar?.Profiles.ShowOverlay()));
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show(); // nicht modal – die Fläche läuft unabhängig weiter
        }

        if (barId is not null) _settingsWindow.SelectBar(barId);
        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    /// <summary>Einstellungen auf der Seite „Profile“ öffnen (Profilauswahl → „Profile verwalten …“).</summary>
    private void ShowProfileSettings()
    {
        ShowSettings(null);
        _settingsWindow?.ShowProfilesPage();
    }

    private void ExitApplication()
    {
        Log.Info("App", "Beenden angefordert");
        Cleanup();
        Shutdown(0);
    }

    private void Cleanup()
    {
        if (_cleanedUp) return;
        _cleanedUp = true;

        try
        {
            _settingsWindow?.Close();
            _updates?.Dispose();
            _bar?.Dispose();
            Notifier.Shutdown();
            _settings?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error("App", "Fehler beim Aufräumen", ex);
        }
        finally
        {
            _singleInstance?.Dispose();
            if (_bar is not null) Log.Info("App", "Beendet");
            Log.Shutdown();
        }
    }

    private void RegisterExceptionHandlers()
    {
        // Stabilität vor allem: UI-Fehler protokollieren und weiterlaufen statt abzustürzen.
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("App", "Unbehandelte Ausnahme im UI-Thread", args.Exception);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Log.Error("App", $"Unbehandelte Ausnahme (Abbruch: {args.IsTerminating})", args.ExceptionObject as Exception);
            if (args.IsTerminating)
            {
                // Reservierten Bildschirmbereich nicht verwaist zurücklassen.
                _bar?.EmergencyCleanup();
                Log.Shutdown();
            }
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("App", "Unbeobachtete Task-Ausnahme", args.Exception);
            args.SetObserved();
        };
    }
}
