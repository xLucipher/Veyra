using Veyra.Core;

namespace Veyra.Configuration;

/// <summary>
/// Layout-Profile anlegen, wechseln, umbenennen und löschen. Jede Operation speichert sofort über den
/// <see cref="SettingsService"/>; die Leisten passen sich wie bei jeder anderen Änderung live an.
/// </summary>
public static class ProfileOperations
{
    /// <summary>Wechselt zum Profil <paramref name="profileId"/>. False, wenn es fehlt oder schon aktiv ist.</summary>
    public static bool Switch(SettingsService service, string profileId)
    {
        var settings = service.Current.Clone();
        var target = settings.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (target is null || target.Id == settings.ActiveProfileId) return false;

        // Aktuelle Leisten in das bisher aktive Profil zurücklegen, die des Zielprofils übernehmen.
        settings.ActiveProfile.Bars = settings.Bars;
        settings.Bars = target.Bars;
        target.Bars = [];
        settings.ActiveProfileId = target.Id;
        service.Save(settings);
        Log.Info("Profile", $"Profil gewechselt: „{target.Name}“");
        return true;
    }

    /// <summary>
    /// Neues Profil als Kopie der aktuellen Ansicht (gleiche Leisten und Widgets – so lässt es sich gleich anpassen).
    /// Gleiche IDs sind gewollt: Beim Wechsel bleiben Leistenfenster bestehen und laufende Timer erhalten.
    /// </summary>
    public static LayoutProfile Create(SettingsService service, string? name = null, string? copyFromId = null)
    {
        var settings = service.Current.Clone();
        var source = settings.Profiles.FirstOrDefault(p => p.Id == copyFromId) ?? settings.ActiveProfile;
        var bars = source.Id == settings.ActiveProfileId ? settings.Bars : source.Bars;
        var profile = new LayoutProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? UniqueName(settings, source.Name) : name.Trim(),
            // Tiefe Kopie über die Serialisierung, damit die Profile nichts teilen.
            Bars = SettingsService.Deserialize(SettingsService.Serialize(new AppSettings { Bars = bars }))?.Bars ?? [],
        };
        settings.Profiles.Insert(settings.Profiles.IndexOf(source) + 1, profile);
        service.Save(settings);
        Log.Info("Profile", $"Profil „{profile.Name}“ angelegt (Kopie von „{source.Name}“)");
        return profile;
    }

    public static void Rename(SettingsService service, string profileId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var settings = service.Current.Clone();
        var profile = settings.Profiles.FirstOrDefault(p => p.Id == profileId);
        if (profile is null || profile.Name == name.Trim()) return;
        profile.Name = name.Trim();
        service.Save(settings);
    }

    /// <summary>Verschiebt ein Profil in der Liste (Reihenfolge = Nummer im Overlay).</summary>
    public static void Move(SettingsService service, string profileId, int delta)
    {
        var settings = service.Current.Clone();
        int index = settings.Profiles.FindIndex(p => p.Id == profileId);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= settings.Profiles.Count) return;
        var profile = settings.Profiles[index];
        settings.Profiles.RemoveAt(index);
        settings.Profiles.Insert(target, profile);
        service.Save(settings);
    }

    /// <summary>Löscht ein Profil. Das letzte bleibt immer bestehen; ist es aktiv, wird vorher zum Nachbarn gewechselt.</summary>
    public static bool Delete(SettingsService service, string profileId)
    {
        var current = service.Current;
        if (current.Profiles.Count <= 1) return false;
        int index = current.Profiles.FindIndex(p => p.Id == profileId);
        if (index < 0) return false;

        if (profileId == current.ActiveProfileId)
            Switch(service, current.Profiles[index == 0 ? 1 : index - 1].Id);

        var settings = service.Current.Clone();
        var name = settings.Profiles.First(p => p.Id == profileId).Name;
        settings.Profiles.RemoveAll(p => p.Id == profileId);
        service.Save(settings);
        Log.Info("Profile", $"Profil „{name}“ gelöscht");
        return true;
    }

    /// <summary>Kurzbeschreibung für Listen und Overlay, z. B. "2 Leisten · 7 Widgets".</summary>
    public static string Summary(AppSettings settings, LayoutProfile profile)
    {
        var bars = BarsOf(settings, profile);
        int widgets = bars.Sum(b => b.Widgets.Count(w => w.Enabled));
        return $"{bars.Count} {(bars.Count == 1 ? "Fläche" : "Flächen")} · {widgets} {(widgets == 1 ? "Widget" : "Widgets")}";
    }

    public static IReadOnlyList<BarSettings> BarsOf(AppSettings settings, LayoutProfile profile) =>
        profile.Id == settings.ActiveProfileId ? settings.Bars : profile.Bars;

    private static string UniqueName(AppSettings settings, string baseName)
    {
        for (int n = 2; ; n++)
        {
            string candidate = $"{baseName} {n}";
            if (settings.Profiles.All(p => !string.Equals(p.Name, candidate, StringComparison.CurrentCultureIgnoreCase)))
                return candidate;
        }
    }
}
