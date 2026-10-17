using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Veyra.Core;

namespace Veyra.Updates;

public sealed record UpdateInfo(
    Version Version,
    string Tag,
    string Title,
    string Notes,
    string HtmlUrl,
    string AssetName,
    string AssetUrl,
    long AssetSize,
    string? Sha256);

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    NoRelease,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update = null, string? Message = null);

/// <summary>
/// Updates über GitHub Releases (öffentliches Repository → keine Anmeldung, kein Token nötig).
///
/// Ablauf: neuestes Release abfragen → Version mit der laufenden vergleichen → ZIP-Asset herunterladen und
/// prüfen (Größe + SHA-256, sofern GitHub sie liefert) → entpacken → ein kleines PowerShell-Skript wartet,
/// bis die Leiste beendet ist, kopiert die neuen Dateien in den Installationsordner und startet sie neu.
/// Die Einstellungen liegen in %AppData% und bleiben unberührt.
/// </summary>
public static class UpdateService
{
    public const string Repository = "xLucipher/Veyra";
    public const string ReleasesPage = $"https://github.com/{Repository}/releases";
    private const string GitHubLatestReleaseApi = $"https://api.github.com/repos/{Repository}/releases/latest";

    // Nur in Debug-Builds: lokaler Testserver statt GitHub (Release-Builds fragen ausschließlich GitHub).
    private static string LatestReleaseApi =>
#if DEBUG
        Environment.GetEnvironmentVariable("CUSTOMTOPBAR_UPDATE_API") ??
#endif
        GitHubLatestReleaseApi;

    private static readonly HttpClient Http = CreateClient();

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string WorkDirectory => Path.Combine(Path.GetTempPath(), "Veyra-update");

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancel = default)
    {
        try
        {
            using var response = await Http.GetAsync(LatestReleaseApi, cancel).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateCheckResult(UpdateCheckStatus.NoRelease, Message: "Auf GitHub ist noch keine Version veröffentlicht.");
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult(UpdateCheckStatus.Failed, Message: $"GitHub antwortet mit {(int)response.StatusCode} {response.ReasonPhrase}.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancel).ConfigureAwait(false);
            var root = json.RootElement;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!TryParseVersion(tag, out var version))
                return new UpdateCheckResult(UpdateCheckStatus.Failed, Message: $"Unbekanntes Versionsformat '{tag}'.");

            if (version <= CurrentVersion)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate);

            var asset = FindZipAsset(root);
            if (asset is null)
                return new UpdateCheckResult(UpdateCheckStatus.Failed, Message: $"Release {tag} enthält kein passendes ZIP-Paket.");

            string title = root.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name ? name : tag;
            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            string html = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;

            var info = new UpdateInfo(version, tag, title, notes.Trim(), html,
                asset.Value.Name, asset.Value.Url, asset.Value.Size, asset.Value.Sha256);
            Log.Info("Update", $"Update verfügbar: {CurrentVersion} → {version} ({asset.Value.Name})");
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, info);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            Log.Warning("Update", $"Update-Prüfung fehlgeschlagen: {ex.Message}");
            return new UpdateCheckResult(UpdateCheckStatus.Failed, Message: "Keine Verbindung zu GitHub.");
        }
    }

    /// <summary>Lädt das Paket herunter, prüft es und entpackt es. Liefert den Ordner mit den neuen Dateien.</summary>
    public static async Task<string> DownloadAndExtractAsync(UpdateInfo update, IProgress<double> progress, CancellationToken cancel)
    {
        Directory.CreateDirectory(WorkDirectory);
        string zipPath = Path.Combine(WorkDirectory, update.AssetName);
        string staging = Path.Combine(WorkDirectory, $"v{update.Version}");

        using (var response = await Http.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? update.AssetSize;
            await using var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false);
            await using var target = File.Create(zipPath);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                done += read;
                if (total > 0) progress.Report((double)done / total);
            }
        }

        // Integrität prüfen: Größe und – wenn GitHub einen Digest liefert – SHA-256.
        var file = new FileInfo(zipPath);
        if (update.AssetSize > 0 && file.Length != update.AssetSize)
            throw new InvalidDataException($"Download unvollständig ({file.Length} von {update.AssetSize} Bytes).");
        if (!string.IsNullOrEmpty(update.Sha256))
        {
            await using var stream = File.OpenRead(zipPath);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancel).ConfigureAwait(false));
            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Prüfsumme des Downloads stimmt nicht – das Paket wurde nicht installiert.");
        }

        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        ZipFile.ExtractToDirectory(zipPath, staging);

        // ZIP kann die Dateien direkt oder in einem Unterordner enthalten.
        string root = File.Exists(Path.Combine(staging, "Veyra.exe"))
            ? staging
            : Directory.GetDirectories(staging).FirstOrDefault(d => File.Exists(Path.Combine(d, "Veyra.exe")))
              ?? throw new InvalidDataException("Das Paket enthält keine Veyra.exe.");

        Log.Info("Update", $"Paket geprüft und entpackt: {root}");
        return root;
    }

    /// <summary>Installationsordner beschreibbar? (Sonst wären Adminrechte nötig – z. B. unter "Programme".)</summary>
    public static bool CanWriteInstallDirectory(out string installDirectory)
    {
        installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        try
        {
            string probe = Path.Combine(installDirectory, $".update-probe-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Startet das Austausch-Skript. Danach muss sich die Anwendung beenden; das Skript wartet darauf,
    /// kopiert die neuen Dateien (vorhandene werden überschrieben, nichts wird gelöscht) und startet neu.
    /// </summary>
    public static void LaunchInstaller(string sourceDirectory)
    {
        CanWriteInstallDirectory(out string installDirectory);
        string exe = Environment.ProcessPath ?? Path.Combine(installDirectory, "Veyra.exe");
        string script = Path.Combine(WorkDirectory, "install-update.ps1");
        string logFile = Path.Combine(WorkDirectory, "install-update.log");

        File.WriteAllText(script, InstallerScript, new UTF8Encoding(true));

        var start = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        foreach (var arg in new[]
                 {
                     "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-File", script,
                     "-ProcessId", Environment.ProcessId.ToString(),
                     "-Source", sourceDirectory,
                     "-Target", installDirectory,
                     "-Exe", exe,
                     "-LogFile", logFile,
                 })
        {
            start.ArgumentList.Add(arg);
        }

        Process.Start(start);
        Log.Info("Update", $"Installation gestartet: {sourceDirectory} → {installDirectory}");
    }

    public static void OpenReleasePage(string? url = null)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url ?? ReleasesPage) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning("Update", $"Release-Seite konnte nicht geöffnet werden: {ex.Message}");
        }
    }

    internal static bool TryParseVersion(string tag, out Version version)
    {
        string text = tag.Trim().TrimStart('v', 'V');
        int dash = text.IndexOfAny(['-', '+']); // z. B. "1.2.0-beta"
        if (dash > 0) text = text[..dash];
        if (Version.TryParse(text, out var parsed))
        {
            version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    private static (string Name, string Url, long Size, string? Sha256)? FindZipAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets)) return null;
        (string, string, long, string?)? fallback = null;
        foreach (var asset in assets.EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            long size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            string? digest = asset.TryGetProperty("digest", out var d) && d.GetString() is { } dg && dg.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? dg["sha256:".Length..]
                : null;

            var candidate = (name, url, size, digest);
            if (name.Contains("win-x64", StringComparison.OrdinalIgnoreCase)) return candidate;
            fallback ??= candidate;
        }
        return fallback;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Veyra", CurrentVersion.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    // Läuft nach dem Beenden der Leiste: warten, Dateien kopieren, neu starten. Fehler landen im Log.
    private const string InstallerScript = """
        param([int]$ProcessId, [string]$Source, [string]$Target, [string]$Exe, [string]$LogFile)
        function Write-Log([string]$Text) { Add-Content -Path $LogFile -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $Text) }
        Write-Log "Update gestartet: $Source -> $Target"
        try { Wait-Process -Id $ProcessId -Timeout 30 -ErrorAction Stop } catch { }
        if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) { Write-Log "Leiste beendet sich nicht - Abbruch"; exit 1 }
        Start-Sleep -Milliseconds 500
        $ok = $false
        for ($i = 0; $i -lt 5 -and -not $ok; $i++) {
            robocopy "$Source" "$Target" /E /R:3 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -lt 8) { $ok = $true } else { Write-Log "robocopy Code $LASTEXITCODE - neuer Versuch"; Start-Sleep -Seconds 1 }
        }
        if ($ok) { Write-Log "Dateien aktualisiert" } else { Write-Log "Kopieren fehlgeschlagen - alte Version wird gestartet" }
        Start-Process -FilePath "$Exe"
        Write-Log "Neu gestartet"
        """;
}
