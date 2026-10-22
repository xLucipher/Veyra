using Veyra.Configuration;
using Veyra.Core;
using Veyra.SettingsUI;

namespace Veyra.Profiles;

/// <summary>Inhalt der Profilauswahl: eine Karte je Profil mit Nummer, Name, Umfang und Vorschau der Leisten.</summary>
public sealed class ProfileOverlayViewModel : ObservableObject
{
    private int _selectedIndex;

    public ProfileOverlayViewModel(AppSettings settings, string? hotkeyText)
    {
        Cards = settings.Profiles.Select((p, i) => new ProfileCard(settings, p, i)).ToList();
        _selectedIndex = Math.Max(0, Cards.ToList().FindIndex(c => c.IsActive));
        Cards[_selectedIndex].IsSelected = true;
        HotkeyHint = string.IsNullOrEmpty(hotkeyText)
            ? "← → auswählen · Enter wechseln · 1–9 direkt · Esc schließen"
            : $"← → auswählen · Enter wechseln · 1–9 direkt · Esc oder {hotkeyText} schließen";
    }

    public IReadOnlyList<ProfileCard> Cards { get; }

    public string HotkeyHint { get; }

    public ProfileCard Selected => Cards[_selectedIndex];

    public void Select(int index)
    {
        if (Cards.Count == 0) return;
        index = (index % Cards.Count + Cards.Count) % Cards.Count; // ringsum
        Cards[_selectedIndex].IsSelected = false;
        _selectedIndex = index;
        Cards[_selectedIndex].IsSelected = true;
    }

    public void Move(int delta) => Select(_selectedIndex + delta);
}

/// <summary>Eine Profilkarte im Overlay.</summary>
public sealed class ProfileCard : ObservableObject
{
    private bool _isSelected;

    public ProfileCard(AppSettings settings, LayoutProfile profile, int index)
    {
        Id = profile.Id;
        Name = profile.Name;
        Number = index < 9 ? (index + 1).ToString() : "";
        IsActive = profile.Id == settings.ActiveProfileId;
        Summary = ProfileOperations.Summary(settings, profile);
        // Vorschau: je Leiste (höchstens drei) die Symbole ihrer aktiven Widgets in Leistenreihenfolge.
        var bars = ProfileOperations.BarsOf(settings, profile);
        Bars = bars.Take(3)
            .Select(b => b.Widgets.Where(w => w.Enabled)
                .OrderBy(w => w.IsFixed ? (w.Slot switch { WidgetSlot.Left => 0.0, WidgetSlot.Center => 0.5, _ => 1.0 }) : w.Position)
                .Select(w => WidgetCatalog.Get(w.Type)).Take(10).ToList())
            .ToList();
        MoreBars = bars.Count > 3 ? $"+ {bars.Count - 3} weitere" : "";
    }

    public string Id { get; }
    public string Name { get; }
    public string Number { get; }
    public bool IsActive { get; }
    public string Summary { get; }
    public IReadOnlyList<IReadOnlyList<WidgetInfo>> Bars { get; }
    public string MoreBars { get; }
    public bool HasNumber => Number.Length > 0;
    public bool HasMoreBars => MoreBars.Length > 0;

    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}
