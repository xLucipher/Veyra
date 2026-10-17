using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Updates;

/// <summary>
/// Entscheidet, wann nach Updates gesucht und gefragt wird:
///  • erste Prüfung 20 s nach dem Start (der Start bleibt schnell), danach alle 6 Stunden;
///  • nie während die Leiste wegen eines Vollbildspiels ausgeblendet ist (dann später erneut);
///  • "Später" → frühestens in 24 Stunden wieder fragen; "Nein" → diese Version nicht mehr anbieten;
///  • eine manuelle Suche in den Einstellungen fragt immer, auch bei übersprungener Version.
/// </summary>
public sealed class UpdateManager : IDisposable
{
    private static readonly TimeSpan StartupDelay =
#if DEBUG
        int.TryParse(Environment.GetEnvironmentVariable("CUSTOMTOPBAR_UPDATE_DELAY_S"), out var s) ? TimeSpan.FromSeconds(s) :
#endif
        TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan BusyRetry = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RemindLater = TimeSpan.FromHours(24);

    private readonly SettingsService _settings;
    private readonly Func<bool> _isBusy;
    private readonly Action _exitApplication;
    private readonly DispatcherTimer _timer;
    private UpdateWindow? _window;
    private bool _checking;

    public UpdateManager(SettingsService settings, Func<bool> isBusy, Action exitApplication)
    {
        _settings = settings;
        _isBusy = isBusy;
        _exitApplication = exitApplication;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = StartupDelay };
        _timer.Tick += async (_, _) => await OnTimerAsync();
    }

    public void Start() => _timer.Start();

    public void Dispose()
    {
        _timer.Stop();
        _window?.Close();
    }

    /// <summary>Manuelle Suche ("Jetzt nach Updates suchen"). Liefert einen Text für die Statusanzeige.</summary>
    public async Task<string> CheckNowAsync()
    {
        var result = await CheckAsync();
        switch (result.Status)
        {
            case UpdateCheckStatus.UpdateAvailable:
                ShowWindow(result.Update!);
                return $"Version {result.Update!.Version} ist verfügbar.";
            case UpdateCheckStatus.UpToDate:
                return $"Du hast die neueste Version ({UpdateService.CurrentVersion}).";
            default:
                return result.Message ?? "Prüfung fehlgeschlagen.";
        }
    }

    private async Task OnTimerAsync()
    {
        _timer.Stop();
        try
        {
            var updates = _settings.Current.Updates;
            if (!updates.AutoCheck)
            {
                _timer.Interval = CheckInterval;
                return;
            }
            if (_isBusy() || _window is not null)
            {
                // Vollbildspiel o. Ä.: nicht stören, bald erneut versuchen.
                _timer.Interval = BusyRetry;
                return;
            }
            if (updates.RemindAfterUtc is { } remind && remind > DateTime.UtcNow)
            {
                _timer.Interval = Min(remind - DateTime.UtcNow + TimeSpan.FromMinutes(1), CheckInterval);
                return;
            }

            var result = await CheckAsync();
            if (result.Status == UpdateCheckStatus.UpdateAvailable &&
                result.Update!.Version.ToString() != _settings.Current.Updates.SkippedVersion &&
                !_isBusy())
            {
                ShowWindow(result.Update);
            }
            _timer.Interval = CheckInterval;
        }
        catch (Exception ex)
        {
            Log.Error("Update", "Fehler bei der automatischen Update-Prüfung", ex);
            _timer.Interval = CheckInterval;
        }
        finally
        {
            _timer.Start();
        }
    }

    private async Task<UpdateCheckResult> CheckAsync()
    {
        if (_checking) return new UpdateCheckResult(UpdateCheckStatus.Failed, Message: "Prüfung läuft bereits.");
        _checking = true;
        try
        {
            var result = await UpdateService.CheckAsync();
            SaveUpdateState(s => s.LastCheckUtc = DateTime.UtcNow);
            Log.Info("Update", $"Update-Prüfung: {result.Status}{(result.Message is null ? "" : $" – {result.Message}")}");
            return result;
        }
        finally
        {
            _checking = false;
        }
    }

    private void ShowWindow(UpdateInfo update)
    {
        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        _window = new UpdateWindow(update, _exitApplication);
        _window.Closed += (_, _) =>
        {
            var decision = _window.Decision;
            _window = null;
            switch (decision)
            {
                case UpdateDecision.Later:
                    SaveUpdateState(s => s.RemindAfterUtc = DateTime.UtcNow + RemindLater);
                    Log.Info("Update", $"Update {update.Version}: später erinnern");
                    break;
                case UpdateDecision.Skip:
                    SaveUpdateState(s =>
                    {
                        s.SkippedVersion = update.Version.ToString();
                        s.RemindAfterUtc = null;
                    });
                    Log.Info("Update", $"Update {update.Version}: übersprungen");
                    break;
            }
        };
        _window.Show();
        _window.Activate();
    }

    private void SaveUpdateState(Action<UpdateSettings> change)
    {
        var settings = _settings.Current.Clone();
        change(settings.Updates);
        _settings.Save(settings);
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
