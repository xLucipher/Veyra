using Veyra.Core;
using Microsoft.Win32;

namespace Veyra.Configuration;

/// <summary>Autostart über HKCU\...\Run – benötigt keine Administratorrechte.</summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Veyra";
    private const string LegacyValueName = "CustomTopBar";

    /// <summary>
    /// Beim Start: Ein vorhandener Autostart-Eintrag (z. B. vom Installer gesetzt oder unter dem früheren Namen
    /// "CustomTopBar") gilt als Wunsch des Benutzers und wird in die Einstellungen übernommen.
    /// Liefert true, wenn die Einstellung dadurch eingeschaltet werden muss.
    /// </summary>
    public static bool AdoptExistingEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            bool legacy = key.GetValue(LegacyValueName) is not null;
            if (legacy)
            {
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
                Log.Info("AutoStart", "Alten Autostart-Eintrag 'CustomTopBar' entfernt");
            }
            return legacy || key.GetValue(ValueName) is not null;
        }
        catch (Exception ex)
        {
            Log.Warning("AutoStart", $"Autostart-Eintrag konnte nicht gelesen werden: {ex.Message}");
            return false;
        }
    }

    public static void Apply(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            var current = key.GetValue(ValueName) as string;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                var desired = $"\"{exe}\"";
                // Einen gültigen Eintrag (z. B. vom Installer) nicht durch eine andere Kopie überschreiben –
                // sonst würde jeder Start eines Test-Builds den Autostart auf sich umbiegen.
                if (current is not null && File.Exists(current.Trim().Trim('"')))
                    return;
                if (!string.Equals(current, desired, StringComparison.OrdinalIgnoreCase))
                {
                    key.SetValue(ValueName, desired);
                    Log.Info("AutoStart", $"Autostart aktiviert: {desired}");
                }
            }
            else if (current is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("AutoStart", "Autostart deaktiviert");
            }
        }
        catch (Exception ex)
        {
            Log.Error("AutoStart", "Autostart-Eintrag konnte nicht gesetzt werden", ex);
        }
    }
}
