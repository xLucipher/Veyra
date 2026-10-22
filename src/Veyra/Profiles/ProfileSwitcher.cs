using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Profiles;

/// <summary>
/// Profilwechsel für die ganze Anwendung: globales Tastenkürzel, Overlay zur Auswahl und das eigentliche Umschalten.
/// Wird vom <see cref="Bar.BarManager"/> gehalten und von Kontextmenü und Einstellungen mitbenutzt.
/// </summary>
public sealed class ProfileSwitcher : IDisposable
{
    private readonly SettingsService _settings;
    private readonly Action _openProfileSettings;
    private readonly ProfileHotkeyHost _hotkey = new();
    private ProfileOverlayWindow? _overlay;
    private string? _registeredHotkey;
    private bool _suspended;

    /// <param name="openProfileSettings">Öffnet die Einstellungen auf der Seite „Profile“.</param>
    public ProfileSwitcher(SettingsService settings, Action openProfileSettings)
    {
        _settings = settings;
        _openProfileSettings = openProfileSettings;
        _hotkey.Pressed += (_, _) => ToggleOverlay();
    }

    /// <summary>Das Tastenkürzel konnte nicht angemeldet werden (von einem anderen Programm belegt).</summary>
    public bool HotkeyUnavailable { get; private set; }

    /// <summary>Tastenkürzel nach einer Einstellungsänderung neu anmelden; während einer Kürzel-Eingabe abgemeldet.</summary>
    public void RefreshHotkey(bool suspended)
    {
        string? text = _settings.Current.ProfileHotkey;
        if (suspended == _suspended && text == _registeredHotkey && !suspended) return;
        _suspended = suspended;
        _registeredHotkey = text;
        if (suspended)
        {
            _hotkey.Unregister();
            return;
        }
        HotkeyUnavailable = !_hotkey.Register(text);
    }

    public void ToggleOverlay()
    {
        if (_overlay is not null)
        {
            _overlay.Close();
            return;
        }
        ShowOverlay();
    }

    public void ShowOverlay()
    {
        if (_overlay is not null) return;
        var settings = _settings.Current;
        string? hotkeyText = Hotkey.TryParse(settings.ProfileHotkey, out var hotkey) ? hotkey.DisplayText : null;
        _overlay = new ProfileOverlayWindow(new ProfileOverlayViewModel(settings, hotkeyText), SwitchTo, _openProfileSettings);
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.ShowAndActivate();
    }

    public void SwitchTo(string profileId)
    {
        try
        {
            ProfileOperations.Switch(_settings, profileId);
        }
        catch (Exception ex)
        {
            Log.Error("Profile", "Profilwechsel fehlgeschlagen", ex);
        }
    }

    public void Dispose()
    {
        _overlay?.Close();
        _hotkey.Dispose();
    }
}
