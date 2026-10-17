using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CustomTopBar.Bar;
using CustomTopBar.Configuration;
using CustomTopBar.Core;
using CustomTopBar.SettingsUI;

namespace CustomTopBar;

/// <summary>Einstiegspunkt: Einzelinstanz, Logging, Konfiguration und Leiste verdrahten.</summary>
public partial class App : Application
{
    private SingleInstance? _singleInstance;
    private SettingsService? _settings;
    private BarManager? _bar;
    private SettingsWindow? _settingsWindow;
    private bool _cleanedUp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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
        Log.Info("App", $"CustomTopBar {version} startet (PID {Environment.ProcessId}, {RuntimeInformation.FrameworkDescription})");

        _settings = new SettingsService();
        _settings.Load();
        Log.Verbose = _settings.Current.System.EnableLogging;

        if (_settings.Current.System.UseSoftwareRendering)
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        _bar = new BarManager(_settings, ShowSettings, ExitApplication);
        _bar.Start();

        _singleInstance.ListenForActivation(
            () => Dispatcher.BeginInvoke(ShowSettings),
            () => Dispatcher.BeginInvoke(ExitApplication));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    private void ShowSettings()
    {
        if (_settings is null) return;

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(new SettingsViewModel(_settings, ExitApplication));
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show(); // nicht modal – die Leiste läuft unabhängig weiter
        }

        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
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
            _bar?.Dispose();
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
