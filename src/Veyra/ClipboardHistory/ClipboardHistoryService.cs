using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.ClipboardHistory;

public enum ClipboardItemKind
{
    Text,
    Image,
    Files,
}

/// <summary>Ein Eintrag im Verlauf. Bilder liegen nur im Arbeitsspeicher.</summary>
public sealed class ClipboardItem(ClipboardItemKind kind, string? text, BitmapSource? image, IReadOnlyList<string>? files)
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public ClipboardItemKind Kind { get; } = kind;
    public string? Text { get; } = text;
    public BitmapSource? Image { get; } = image;
    public IReadOnlyList<string> Files { get; } = files ?? [];
    public DateTime Time { get; set; } = DateTime.Now;

    /// <summary>Nur Bilder: Prüfsumme der Farbwerte (ohne Alpha), siehe <see cref="ImageHash"/>.</summary>
    private readonly string? _imageHash = image is null ? null : ImageHash(image);

    /// <summary>Gleicher Inhalt: Texte, Dateilisten und Bilder (gleiche Größe und Farbwerte).</summary>
    public bool SameContent(ClipboardItem other) => Kind == other.Kind && Kind switch
    {
        ClipboardItemKind.Text => string.Equals(Text, other.Text, StringComparison.Ordinal),
        ClipboardItemKind.Files => Files.SequenceEqual(other.Files, StringComparer.OrdinalIgnoreCase),
        _ => _imageHash == other._imageHash,
    };

    /// <summary>
    /// Größe und SHA-256 der Farbwerte. Alpha zählt nicht: Dasselbe Bildschirmfoto kommt als PNG (deckend) und als DIB
    /// (ohne Alpha) an – Programme wie das Snipping Tool legen beides nacheinander ab und melden jedes Mal eine Änderung.
    /// </summary>
    private static string ImageHash(BitmapSource image)
    {
        var bgr = new FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgr32, null, 0);
        int stride = bgr.PixelWidth * 4;
        var pixels = new byte[stride * bgr.PixelHeight];
        bgr.CopyPixels(pixels, stride, 0);
        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 0;
        return $"{bgr.PixelWidth}x{bgr.PixelHeight}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels))}";
    }
}

/// <summary>
/// Verlauf der Zwischenablage für alle Verlaufs-Widgets. Ereignisbasiert über <c>AddClipboardFormatListener</c>
/// (<c>WM_CLIPBOARDUPDATE</c>) an einem unsichtbaren Nachrichtenfenster, kein Abfragen. Läuft, solange mindestens
/// ein Widget existiert – auch während die Fläche bei Vollbild angehalten ist, damit dort Kopiertes nicht fehlt.
/// Inhalte, die Passwortmanager ausdrücklich vom Verlauf ausnehmen, werden übersprungen.
/// </summary>
public sealed class ClipboardHistoryService : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;

    private readonly List<ClipboardItem> _items = [];
    private readonly uint _excludeFormat = RegisterClipboardFormatW("ExcludeClipboardContentFromMonitorProcessing");
    private readonly uint _historyFormat = RegisterClipboardFormatW("CanIncludeInClipboardHistory");
    private readonly uint _ignoreFormat = RegisterClipboardFormatW("Clipboard Viewer Ignore");
    private HwndSource? _window;
    private readonly Dictionary<object, (int MaxItems, bool KeepImages)> _users = [];
    private DispatcherTimer? _retry;
    private int _maxItems = 25;
    private bool _keepImages = true;
    private string? _ownText;
    private int _retries;

    /// <summary>Verlauf geändert (neuer Eintrag, gelöscht, geleert).</summary>
    public event EventHandler? Changed;

    /// <summary>Neueste zuerst.</summary>
    public IReadOnlyList<ClipboardItem> Items => _items;

    /// <summary>Widget meldet sich an bzw. ändert seine Wünsche (Länge, Bilder). Der Verlauf erfüllt den größten Wunsch.</summary>
    public void Acquire(object owner, int maxItems, bool keepImages)
    {
        _users[owner] = (maxItems, keepImages);
        UpdateLimits();
        if (_window is not null) return;
        _window = new HwndSource(new HwndSourceParameters("VeyraClipboard") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _window.AddHook(WndProc);
        if (!AddClipboardFormatListener(_window.Handle))
            Log.Warning("Clipboard", $"Zwischenablage kann nicht überwacht werden (Fehler {Marshal.GetLastWin32Error()})");
        else
            Log.Info("Clipboard", "Verlauf der Zwischenablage gestartet");
    }

    public void Release(object owner)
    {
        if (!_users.Remove(owner)) return;
        if (_users.Count > 0)
        {
            UpdateLimits();
            return;
        }
        _retry?.Stop();
        if (_window is not null)
        {
            RemoveClipboardFormatListener(_window.Handle);
            _window.Dispose();
            _window = null;
        }
        // Verlauf behalten: Ändern sich die Widgets einer Fläche, werden alle kurz neu erzeugt – danach soll er noch da
        // sein. Er liegt ohnehin nur im Arbeitsspeicher und endet mit Veyra.
        Log.Info("Clipboard", "Verlauf der Zwischenablage beendet");
    }

    /// <summary>Größte gewünschte Länge aller Widgets; Bilder, solange ein Widget sie behalten will.</summary>
    private void UpdateLimits()
    {
        _maxItems = _users.Values.Max(u => u.MaxItems);
        _keepImages = _users.Values.Any(u => u.KeepImages);
        Trim();
    }

    public void Remove(ClipboardItem item)
    {
        if (_items.Remove(item)) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (_items.Count == 0) return;
        _items.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Eintrag zurück in die Zwischenablage legen; er rückt im Verlauf nach oben.</summary>
    public bool Copy(ClipboardItem item)
    {
        bool ok = item.Kind switch
        {
            ClipboardItemKind.Text => CopyText(item.Text ?? ""),
            ClipboardItemKind.Image => Try(() => System.Windows.Clipboard.SetImage(item.Image!)),
            _ => Try(() =>
            {
                var list = new StringCollection();
                list.AddRange(item.Files.ToArray());
                System.Windows.Clipboard.SetFileDropList(list);
            }),
        };
        if (ok && _items.Remove(item))
        {
            item.Time = DateTime.Now;
            _items.Insert(0, item);
            Changed?.Invoke(this, EventArgs.Empty);
        }
        return ok;
    }

    /// <summary>Text (z. B. einen angehefteten) in die Zwischenablage legen – ohne doppelten Verlaufseintrag.</summary>
    public bool CopyText(string text)
    {
        _ownText = text;
        return Try(() => System.Windows.Clipboard.SetDataObject(text, copy: true));
    }

    private static bool Try(Action action)
    {
        // Die Zwischenablage ist kurz gesperrt, wenn ein anderes Programm sie gerade öffnet → ein paar Mal versuchen.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                action();
                return true;
            }
            catch (COMException) when (attempt < 4)
            {
                Thread.Sleep(30);
            }
            catch (Exception ex)
            {
                Log.Warning("Clipboard", $"Zwischenablage konnte nicht gesetzt werden: {ex.Message}");
                return false;
            }
        }
        return false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_CLIPBOARDUPDATE) return IntPtr.Zero;
        handled = true;
        _retries = 0;
        Capture();
        return IntPtr.Zero;
    }

    private void Capture()
    {
        if (IsExcluded()) return;
        ClipboardItem? item;
        try
        {
            item = Read();
        }
        catch (COMException)
        {
            // Das kopierende Programm hält die Zwischenablage noch offen → gleich noch einmal.
            if (_retries++ < 5) ScheduleRetry();
            return;
        }
        catch (Exception ex)
        {
            Log.Debug("Clipboard", $"Inhalt nicht lesbar: {ex.Message}");
            return;
        }
        if (item is null) return;

        if (item.Kind == ClipboardItemKind.Text && item.Text == _ownText)
        {
            // Selbst gesetzt (z. B. angehefteter Text): nicht noch einmal aufnehmen.
            _ownText = null;
            return;
        }
        _ownText = null;

        int existing = _items.FindIndex(i => i.SameContent(item));
        if (existing >= 0) _items.RemoveAt(existing);
        _items.Insert(0, item);
        Trim();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void ScheduleRetry()
    {
        _retry ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _retry.Tick -= OnRetry;
        _retry.Tick += OnRetry;
        _retry.Stop();
        _retry.Start();
    }

    private void OnRetry(object? sender, EventArgs e)
    {
        _retry!.Stop();
        if (_window is not null) Capture();
    }

    private ClipboardItem? Read()
    {
        if (System.Windows.Clipboard.ContainsText())
        {
            string text = System.Windows.Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (text.Length > ClipboardSettings.MaxTextLength) text = text[..ClipboardSettings.MaxTextLength];
            return new ClipboardItem(ClipboardItemKind.Text, text, null, null);
        }
        // Bild vor Dateien: Das Snipping Tool legt neben dem Bild auch die automatisch gespeicherte Datei ab – das ist
        // ein Bildschirmfoto, kein Dateiverweis. Im Explorer kopierte Dateien bringen dagegen kein Bild mit.
        if (_keepImages && ReadImage() is { } image)
            return new ClipboardItem(ClipboardItemKind.Image, null, image, null);
        if (System.Windows.Clipboard.ContainsFileDropList())
        {
            var files = System.Windows.Clipboard.GetFileDropList().Cast<string>().Where(f => !string.IsNullOrEmpty(f)).ToList();
            return files.Count == 0 ? null : new ClipboardItem(ClipboardItemKind.Files, null, null, files);
        }
        return null;
    }

    /// <summary>
    /// Bild aus der Zwischenablage, eingefroren und vom Zwischenablage-Objekt gelöst. Bevorzugt das Format „PNG“
    /// (Snipping Tool, Browser) – es behält echte Transparenz. Sonst die DIB über WPF: Bildschirmfotos haben dort
    /// keinen Alphakanal, WPF liest ihn aber als Bgra32 mit Alpha 0 – das Bild wäre unsichtbar. Dann als Bgr32 übernehmen.
    /// </summary>
    private static BitmapSource? ReadImage()
    {
        if (System.Windows.Clipboard.GetDataObject() is not { } data) return null;
        if (data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream png)
        {
            try
            {
                var frame = new PngBitmapDecoder(png, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                frame.Freeze();
                return frame;
            }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or ArgumentException)
            {
                // Kaputtes PNG – weiter mit der DIB.
            }
        }
        if (!System.Windows.Clipboard.ContainsImage() || System.Windows.Clipboard.GetImage() is not { } image) return null;

        BitmapSource source = image.Format == PixelFormats.Bgra32 || image.Format == PixelFormats.Bgr32
            ? image
            : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);
        bool hasAlpha = false;
        if (source.Format == PixelFormats.Bgra32)
        {
            for (int i = 3; i < pixels.Length && !hasAlpha; i += 4) hasAlpha = pixels[i] != 0;
        }
        double dpiX = source.DpiX > 1 ? source.DpiX : 96, dpiY = source.DpiY > 1 ? source.DpiY : 96;
        var copy = BitmapSource.Create(source.PixelWidth, source.PixelHeight, dpiX, dpiY,
            hasAlpha ? PixelFormats.Bgra32 : PixelFormats.Bgr32, null, pixels, stride);
        copy.Freeze();
        return copy;
    }

    /// <summary>Passwortmanager & Co. markieren Inhalte, die in keinem Verlauf landen sollen.</summary>
    private bool IsExcluded()
    {
        if (IsClipboardFormatAvailable(_excludeFormat) || IsClipboardFormatAvailable(_ignoreFormat)) return true;
        if (!IsClipboardFormatAvailable(_historyFormat)) return false;
        if (_window is null || !OpenClipboard(_window.Handle)) return true; // im Zweifel nicht aufnehmen
        try
        {
            var handle = GetClipboardData(_historyFormat);
            var data = handle == IntPtr.Zero ? IntPtr.Zero : GlobalLock(handle);
            if (data == IntPtr.Zero) return false;
            try
            {
                return Marshal.ReadInt32(data) == 0;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private void Trim()
    {
        bool changed = false;
        if (!_keepImages) changed |= _items.RemoveAll(i => i.Kind == ClipboardItemKind.Image) > 0;
        // Bilder brauchen viel Speicher: nur die neuesten behalten.
        foreach (var old in _items.Where(i => i.Kind == ClipboardItemKind.Image).Skip(ClipboardSettings.MaxImages).ToList())
            changed |= _items.Remove(old);
        if (_items.Count > _maxItems)
        {
            _items.RemoveRange(_maxItems, _items.Count - _maxItems);
            changed = true;
        }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        foreach (var owner in _users.Keys.ToList()) Release(owner);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);
}
