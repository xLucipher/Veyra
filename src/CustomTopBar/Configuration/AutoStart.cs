using CustomTopBar.Core;
using Microsoft.Win32;

namespace CustomTopBar.Configuration;

/// <summary>Autostart über HKCU\...\Run – benötigt keine Administratorrechte.</summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CustomTopBar";

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
