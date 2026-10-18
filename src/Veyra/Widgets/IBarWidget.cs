using System.Windows;
using Veyra.Configuration;

namespace Veyra.Widgets;

/// <summary>
/// Vertrag für Leisten-Widgets (Uhr, Audiogeräte, später CPU, RAM, Medien …).
/// Position und Reihenfolge kommen aus der Konfiguration (<see cref="BarSettings.Widgets"/>).
/// Widgets werden angehalten, solange die Leiste ausgeblendet ist (z. B. während eines Vollbildspiels),
/// damit im Hintergrund keinerlei Timer oder UI-Updates laufen.
/// </summary>
public interface IBarWidget : IDisposable
{
    FrameworkElement View { get; }

    /// <param name="bar">Die Leiste, auf der das Widget sitzt (Schrift, Farben, Widget-Eintrag).</param>
    void ApplySettings(AppSettings settings, BarSettings bar);

    /// <summary>Aktualisierungen starten bzw. fortsetzen.</summary>
    void Start();

    /// <summary>Alle Timer/Abfragen anhalten.</summary>
    void Stop();

    /// <summary>Sofort neu einlesen (z. B. nach Änderung der Systemzeit oder Zeitzone).</summary>
    void Refresh();
}

/// <summary>Widget mit Aktionen für globale Tastenkürzel (<see cref="WidgetHotkeys"/>). Wird auch aufgerufen, während das Widget angehalten ist.</summary>
public interface IHotkeyTarget
{
    string EntryId { get; }

    void OnHotkey(string action);
}
