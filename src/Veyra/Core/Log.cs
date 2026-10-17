using System.Text;
using System.Threading.Channels;

namespace Veyra.Core;

/// <summary>
/// Minimaler, threadsicherer Datei-Logger. Schreibt asynchron über einen Hintergrund-Task,
/// damit der UI-Thread nie auf Datei-I/O wartet. Fehler und Warnungen werden immer geschrieben,
/// Info/Debug nur bei <see cref="Verbose"/> (Einstellung System.EnableLogging).
/// </summary>
public static class Log
{
    private const long MaxFileSizeBytes = 20 * 1024 * 1024;
    private const int RetentionDays = 7;

    private static readonly Channel<string> Queue =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    private static Task? _worker;

    public static string? LogDirectory { get; private set; }

    public static bool Verbose { get; set; } = true;

    public static void Initialize(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            LogDirectory = directory;
            DeleteOldFiles(directory);
            _worker = Task.Run(WriteLoopAsync);
        }
        catch
        {
            // Ohne Log-Verzeichnis läuft die Anwendung trotzdem weiter.
            LogDirectory = null;
        }
    }

    public static void Debug(string category, string message)
    {
        if (Verbose) Enqueue("DBG", category, message);
    }

    public static void Info(string category, string message)
    {
        if (Verbose) Enqueue("INF", category, message);
    }

    public static void Warning(string category, string message) => Enqueue("WRN", category, message);

    public static void Error(string category, string message, Exception? ex = null) =>
        Enqueue("ERR", category, ex is null ? message : $"{message}{Environment.NewLine}{ex}");

    public static void Shutdown()
    {
        Queue.Writer.TryComplete();
        try
        {
            _worker?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Beim Beenden ignorieren.
        }
    }

    private static void Enqueue(string level, string category, string message)
    {
        if (LogDirectory is null) return;
        Queue.Writer.TryWrite($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{category}] {message}");
    }

    private static async Task WriteLoopAsync()
    {
        var reader = Queue.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            try
            {
                var path = Path.Combine(LogDirectory!, $"topbar-{DateTime.Now:yyyyMMdd}.log");
                var info = new FileInfo(path);
                bool full = info.Exists && info.Length > MaxFileSizeBytes;

                // UTF-8 mit BOM: Editor und PowerShell 5 erkennen Umlaute dann korrekt (BOM nur bei neuer Datei).
                using var writer = full ? null : new StreamWriter(path, append: true, new UTF8Encoding(true));
                while (reader.TryRead(out var line))
                    writer?.WriteLine(line);
            }
            catch
            {
                // Logging darf die Anwendung niemals beeinträchtigen.
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }

    private static void DeleteOldFiles(string directory)
    {
        var limit = DateTime.Now.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(directory, "topbar-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < limit) File.Delete(file);
            }
            catch
            {
                // Gesperrte Dateien beim nächsten Start erneut versuchen.
            }
        }
    }
}
