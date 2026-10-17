using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using CustomTopBar.Core;

namespace CustomTopBar.Configuration;

/// <summary>
/// Zentrale Konfigurationsverwaltung: Laden, Validieren, atomares Speichern und
/// Live-Neuladen bei externen Änderungen an settings.json.
/// </summary>
public sealed class SettingsService : IDisposable
{
    public static readonly string DirectoryPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CustomTopBar");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _reloadDebounce;
    private FileSystemWatcher? _watcher;
    private string? _lastKnownContent;

    public SettingsService()
    {
        _reloadDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _reloadDebounce.Tick += (_, _) =>
        {
            _reloadDebounce.Stop();
            ReloadFromDisk(fromWatcher: true);
        };
    }

    public string FilePath { get; } = Path.Combine(DirectoryPath, "settings.json");

    public AppSettings Current { get; private set; } = new();

    /// <summary>Wird auf dem UI-Thread ausgelöst, wenn sich die wirksamen Einstellungen geändert haben.</summary>
    public event EventHandler<AppSettings>? Changed;

    public static string Serialize(AppSettings settings) => JsonSerializer.Serialize(settings, JsonOptions);

    public static AppSettings? Deserialize(string json) => JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);

    public void Load()
    {
        Directory.CreateDirectory(DirectoryPath);

        if (!File.Exists(FilePath))
        {
            Log.Info("Settings", $"Keine Konfiguration gefunden – Standardwerte werden nach {FilePath} geschrieben");
            Current = CreateDefaults();
            Write(Current);
        }
        else
        {
            var text = ReadText();
            string? error = "Datei nicht lesbar";
            if (text is not null && TryParse(text, out var settings, out error))
            {
                Current = settings;
                _lastKnownContent = text;
                PersistGeneratedIds(text, settings);
                Log.Info("Settings", "Konfiguration geladen");
            }
            else
            {
                // Fehlerhafte Datei sichern statt sie stillschweigend zu verlieren, dann mit Standardwerten weiter.
                var backup = $"{FilePath}.invalid-{DateTime.Now:yyyyMMdd-HHmmss}";
                try
                {
                    File.Copy(FilePath, backup, overwrite: true);
                }
                catch (Exception ex)
                {
                    Log.Error("Settings", "Sicherung der fehlerhaften Konfiguration fehlgeschlagen", ex);
                }

                Log.Error("Settings", $"settings.json ist ungültig ({error}). Sicherung: {backup}. Standardwerte werden verwendet.");
                Current = CreateDefaults();
                Write(Current);
            }
        }

        StartWatching();
    }

    /// <summary>Validiert, speichert atomar und übernimmt die Einstellungen sofort.</summary>
    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Write(settings);
        Current = settings;
        Log.Info("Settings", "Konfiguration gespeichert");
        Changed?.Invoke(this, settings);
    }

    /// <summary>Liest settings.json erneut ein. Bei Fehlern bleiben die aktuellen Einstellungen aktiv.</summary>
    public bool ReloadFromDisk(bool fromWatcher = false)
    {
        var text = ReadText();
        if (text is null) return false;
        if (fromWatcher && text == _lastKnownContent) return false; // eigene Schreibvorgänge ignorieren

        if (!TryParse(text, out var settings, out var error))
        {
            Log.Warning("Settings", $"settings.json ist ungültig, bisherige Einstellungen bleiben aktiv: {error}");
            return false;
        }

        _lastKnownContent = text;
        Current = settings;
        PersistGeneratedIds(text, settings);
        Log.Info("Settings", fromWatcher ? "Externe Änderung an settings.json übernommen" : "Konfiguration neu geladen");
        Changed?.Invoke(this, settings);
        return true;
    }

    /// <summary>
    /// Widgets ohne ID (ältere oder von Hand bearbeitete Dateien) erhalten beim Laden eine neue ID. Diese muss
    /// gespeichert werden, sonst wechselt sie bei jedem Neuladen und Drag-&amp;-Drop-Positionen gingen verloren.
    /// </summary>
    private void PersistGeneratedIds(string text, AppSettings settings)
    {
        if (settings.Widgets.All(w => text.Contains($"\"{w.Id}\"", StringComparison.Ordinal))) return;
        Log.Info("Settings", "Neue Widget-IDs vergeben und gespeichert");
        Write(settings);
    }

    public void Dispose()
    {
        _reloadDebounce.Stop();
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private static AppSettings CreateDefaults()
    {
        var settings = new AppSettings();
        settings.Normalize();
        return settings;
    }

    private static bool TryParse(string text, out AppSettings settings, out string? error)
    {
        try
        {
            var parsed = Deserialize(text);
            if (parsed is null)
            {
                settings = CreateDefaults();
                error = "leeres Dokument";
                return false;
            }

            parsed.Normalize();
            settings = parsed;
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            settings = CreateDefaults();
            error = ex.Message;
            return false;
        }
    }

    private string? ReadText()
    {
        // Editoren halten die Datei beim Speichern kurz gesperrt – einige kurze Wiederholungen genügen.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return File.ReadAllText(FilePath, Encoding.UTF8);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (IOException)
            {
                Thread.Sleep(60);
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Error("Settings", "Kein Zugriff auf settings.json", ex);
                return null;
            }
        }
        Log.Warning("Settings", "settings.json konnte nicht gelesen werden (gesperrt)");
        return null;
    }

    private void Write(AppSettings settings)
    {
        var json = Serialize(settings);
        var tempPath = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));
            File.Move(tempPath, FilePath, overwrite: true);
            _lastKnownContent = json;
        }
        catch (Exception ex)
        {
            Log.Error("Settings", "Konfiguration konnte nicht gespeichert werden", ex);
        }
    }

    private void StartWatching()
    {
        try
        {
            _watcher = new FileSystemWatcher(DirectoryPath, "settings.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            FileSystemEventHandler onChange = (_, _) => ScheduleReload();
            _watcher.Changed += onChange;
            _watcher.Created += onChange;
            _watcher.Renamed += (_, _) => ScheduleReload();
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex)
        {
            Log.Error("Settings", "Dateiüberwachung für settings.json konnte nicht gestartet werden", ex);
        }
    }

    private void ScheduleReload() =>
        _dispatcher.BeginInvoke(() =>
        {
            _reloadDebounce.Stop();
            _reloadDebounce.Start();
        });
}
