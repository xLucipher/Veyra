using Veyra.Audio;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Bar;

/// <summary>
/// Verwaltet alle Leisten: je <see cref="BarSettings"/> ein <see cref="BarController"/> mit eigenem Fenster.
/// Kommen Leisten in den Einstellungen hinzu oder fallen weg, werden die Fenster passend erzeugt bzw. geschlossen.
/// Gemeinsam genutzt werden nur Einstellungen und der Audio-Dienst.
/// </summary>
public sealed class BarManager : IDisposable
{
    private readonly SettingsService _settings;
    private readonly Action<string?> _openSettings;
    private readonly Action _exit;
    private readonly List<BarController> _bars = [];

    private AudioDeviceService? _audioService;
    private bool _autoHideWarningLogged;
    private bool _disposed;

    /// <param name="openSettings">Öffnet die Einstellungen, optional mit der angegebenen Leiste ausgewählt.</param>
    public BarManager(SettingsService settings, Action<string?> openSettings, Action exit)
    {
        _settings = settings;
        _openSettings = openSettings;
        _exit = exit;
    }

    /// <summary>True, solange eine Leiste wegen einer Vollbildanwendung ausgeblendet ist (dann nicht stören).</summary>
    public bool IsHiddenForFullscreen => _bars.Any(b => b.IsHiddenForFullscreen);

    public void Start()
    {
        _settings.Changed += OnSettingsChanged;
        ApplyGlobalSettings(_settings.Current);
        SyncBars(_settings.Current);
    }

    /// <summary>Kontextmenü "Leiste neu laden": Konfiguration neu einlesen und alle Leisten neu aufbauen.</summary>
    public void Reload()
    {
        Log.Info("Bar", "Leisten werden neu geladen");
        _settings.ReloadFromDisk();
        foreach (var bar in _bars) bar.Rebuild();
    }

    /// <summary>Notfall-Aufräumen aus dem Absturz-Handler (beliebiger Thread): reservierte Bildschirmbereiche freigeben.</summary>
    public void EmergencyCleanup()
    {
        foreach (var bar in _bars.ToArray()) bar.EmergencyCleanup();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        foreach (var bar in _bars) bar.Dispose();
        _bars.Clear();
        _audioService?.Dispose();
    }

    private AudioDeviceService AudioService => _audioService ??= new AudioDeviceService();

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        ApplyGlobalSettings(settings);
        SyncBars(settings);
    }

    /// <summary>Leisten-Fenster an die Liste in den Einstellungen anpassen; bestehende übernehmen die Änderungen.</summary>
    private void SyncBars(AppSettings settings)
    {
        foreach (var removed in _bars.Where(c => settings.FindBar(c.BarId) is null).ToList())
        {
            Log.Info("Bar", $"Leiste {removed.BarId} entfernt");
            removed.Dispose();
            _bars.Remove(removed);
        }

        foreach (var bar in settings.Bars)
        {
            var controller = _bars.FirstOrDefault(c => c.BarId == bar.Id);
            if (controller is not null)
            {
                controller.OnSettingsChanged(settings);
                continue;
            }

            Log.Info("Bar", $"Leiste „{bar.Name}“ ({bar.Id}) wird erzeugt");
            controller = new BarController(_settings, bar.Id, () => AudioService, id => _openSettings(id), Reload, _exit);
            _bars.Add(controller);
            controller.Start();
        }
    }

    private void ApplyGlobalSettings(AppSettings settings)
    {
        Log.Verbose = settings.System.EnableLogging;
        AutoStart.Apply(settings.System.StartWithWindows);

        if (settings.Behavior.AutoHide && !_autoHideWarningLogged)
        {
            Log.Warning("Bar", "'AutoHide' ist noch nicht implementiert – die Leiste bleibt sichtbar.");
            _autoHideWarningLogged = true;
        }
    }
}
