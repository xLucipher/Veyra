using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Veyra.ClipboardHistory;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Interop;

namespace Veyra.Widgets.ClipboardHistory;

/// <summary>Eine Zeile im Verlaufs-Popup: angehefteter Text oder Eintrag des Verlaufs.</summary>
public sealed class ClipboardRow : ObservableObject
{
    private bool _isSelected;

    public ClipboardRow(ClipboardItem? item, string? pinnedText, ClipboardWidget owner)
    {
        Item = item;
        PinnedText = pinnedText;
        Text = pinnedText ?? item?.Text;
        SelectCommand = new RelayCommand(() => owner.Select(this));
        TogglePinCommand = new RelayCommand(() => owner.TogglePin(this));
        RemoveCommand = new RelayCommand(() => owner.Remove(this));
    }

    public ClipboardItem? Item { get; }

    /// <summary>Gesetzt bei angehefteten Texten.</summary>
    public string? PinnedText { get; }

    public string? Text { get; }
    public bool IsPinned => PinnedText is not null;
    public bool IsImage => Item?.Kind == ClipboardItemKind.Image;
    public BitmapSource? Image => Item?.Image;

    /// <summary>Höchstens drei Zeilen, ohne führende Leerzeilen; Dateien als Liste der Namen.</summary>
    public string Preview
    {
        get
        {
            if (Item?.Kind == ClipboardItemKind.Files)
                return string.Join("\n", Item.Files.Take(3).Select(System.IO.Path.GetFileName)) +
                       (Item.Files.Count > 3 ? $"\n… und {Item.Files.Count - 3} weitere" : "");
            var lines = (Text ?? "").Replace("\r", "").Split('\n').SkipWhile(l => l.Trim().Length == 0).Take(4).ToList();
            string preview = string.Join("\n", lines.Take(3).Select(l => l.Length > 160 ? l[..160] + " …" : l));
            return lines.Count > 3 ? preview + " …" : preview;
        }
    }

    public string Meta
    {
        get
        {
            if (IsPinned) return "Angeheftet";
            if (Item is null) return "";
            string age = Ago(Item.Time);
            return Item.Kind switch
            {
                ClipboardItemKind.Image => $"Bild · {Item.Image?.PixelWidth} × {Item.Image?.PixelHeight} · {age}",
                ClipboardItemKind.Files => (Item.Files.Count == 1 ? "1 Datei" : $"{Item.Files.Count} Dateien") + " · " + age,
                _ => (Text?.Length > 200 ? $"{Text.Length.ToString("N0", CultureInfo.CurrentCulture)} Zeichen · " : "") + age,
            };
        }
    }

    public Visibility ImageVisibility => IsImage ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TextVisibility => IsImage ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Nur Texte lassen sich anheften (Bilder und Dateien würden nach einem Neustart fehlen).</summary>
    public Visibility PinVisibility => Text is not null ? Visibility.Visible : Visibility.Collapsed;

    public string PinGlyph => IsPinned ? "" : "";
    public string PinToolTip => IsPinned ? "Nicht mehr anheften" : "Anheften – bleibt auch nach einem Neustart erhalten";
    public string RemoveToolTip => IsPinned ? "Angehefteten Text entfernen" : "Aus dem Verlauf entfernen";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value)) OnPropertyChanged(nameof(Background));
        }
    }

    public Brush Background => _isSelected ? SelectedBrush : NormalBrush;

    public ICommand SelectCommand { get; }
    public ICommand TogglePinCommand { get; }
    public ICommand RemoveCommand { get; }

    public bool Matches(string search) => search.Length == 0 ||
        (Text?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
        (Item?.Files.Any(f => f.Contains(search, StringComparison.CurrentCultureIgnoreCase)) ?? false);

    private static readonly Brush NormalBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush SelectedBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0x3D, 0xAE, 0xE9)));

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private static string Ago(DateTime time)
    {
        var age = DateTime.Now - time;
        if (age.TotalSeconds < 60) return "gerade eben";
        if (age.TotalMinutes < 60) return $"vor {(int)age.TotalMinutes} Min.";
        if (age.TotalHours < 24) return $"vor {(int)age.TotalHours} Std.";
        return time.ToString("d. MMM, HH:mm", CultureInfo.CurrentCulture);
    }
}

/// <summary>
/// Verlauf der Zwischenablage: Symbol auf der Fläche, Klick (oder Tastenkürzel) öffnet die Liste der zuletzt
/// kopierten Texte, Bilder und Dateien. Auswahl legt den Eintrag zurück in die Zwischenablage und fügt ihn auf Wunsch
/// gleich in das vorher aktive Fenster ein. Texte lassen sich anheften – nur diese werden gespeichert.
/// </summary>
public sealed class ClipboardWidget : ObservableObject, IBarWidget, IHotkeyTarget
{
    private readonly ClipboardHistoryService _service;
    private readonly Action<string, Action<WidgetEntry>> _updateEntry;
    private readonly Border _view;
    private readonly TextBlock _icon;
    private readonly TextBlock _count;
    private ClipboardSettings _clipboard = new();
    private ClipboardFlyout? _flyout;
    private IntPtr _pasteTarget;
    private string _search = "";

    public ClipboardWidget(string entryId, ClipboardHistoryService service, Action<string, Action<WidgetEntry>> updateEntry)
    {
        EntryId = entryId;
        _service = service;
        _updateEntry = updateEntry;
        _icon = new TextBlock { Text = "", FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        _icon.SetResourceReference(TextBlock.FontFamilyProperty, "WidgetIconFont");
        _count = new TextBlock { FontSize = 12, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        Typography.SetNumeralAlignment(_count, FontNumeralAlignment.Tabular);
        _view = new Border { Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _icon, _count } } };
        _view.SetResourceReference(FrameworkElement.StyleProperty, "WidgetHoverArea");
        _view.MouseLeftButtonUp += (_, _) => ToggleFlyout();
        ClearCommand = new RelayCommand(() => _service.Clear());
        _service.Changed += OnHistoryChanged;
    }

    public string EntryId { get; }
    public FrameworkElement View => _view;

    public ObservableCollection<ClipboardRow> Rows { get; } = [];
    public ICommand ClearCommand { get; }

    public string Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value ?? "")) RebuildRows();
        }
    }

    public string Subtitle => _service.Items.Count == 0 && _clipboard.Pinned.Count == 0
        ? "Noch nichts kopiert – alles, was du ab jetzt kopierst, erscheint hier."
        : _clipboard.PasteOnSelect
            ? "Klick oder Enter fügt in das vorherige Fenster ein."
            : "Klick oder Enter kopiert den Eintrag in die Zwischenablage.";

    public Visibility EmptyVisibility => Rows.Count == 0 && _search.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == EntryId);
        _clipboard = entry?.Clipboard ?? new ClipboardSettings();
        _service.Acquire(this, _clipboard.MaxItems, _clipboard.KeepImages);

        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        _icon.Foreground = entry?.IconColor is { } icon ? ColorUtil.ToBrush(icon, Colors.White) : barForeground;
        _count.Foreground = entry?.LabelColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        _count.FontFamily = new FontFamily(bar.Appearance.FontFamily);
        UpdateView();
        if (_flyout is not null) RebuildRows();
    }

    public void Start()
    {
    }

    public void Stop()
    {
        // Der Verlauf läuft weiter: Auch im Spiel Kopiertes soll danach in der Liste stehen.
    }

    public void Refresh()
    {
    }

    public void OnHotkey(string action)
    {
        if (action == WidgetHotkeys.ShowClipboard && _view.IsVisible && _flyout is null) ToggleFlyout();
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        UpdateView();
        if (_flyout is not null) RebuildRows();
    }

    private void UpdateView()
    {
        int count = _service.Items.Count;
        _count.Text = count.ToString(CultureInfo.CurrentCulture);
        _count.Visibility = _clipboard.ShowCount ? Visibility.Visible : Visibility.Collapsed;
        _view.ToolTip = (count == 0 ? "Zwischenablage-Verlauf – noch leer" : $"Zwischenablage-Verlauf: {count} {(count == 1 ? "Eintrag" : "Einträge")}") +
                        (_clipboard.Pinned.Count > 0 ? $", {_clipboard.Pinned.Count} angeheftet" : "") + "\nKlicken zum Öffnen";
    }

    private void ToggleFlyout()
    {
        // Die Fläche wird nie aktiv – das Vordergrundfenster ist also das, in das eingefügt werden soll.
        var foreground = NativeMethods.GetForegroundWindow();
        _search = "";
        OnPropertyChanged(nameof(Search));
        RebuildRows();
        _flyout = FlyoutWindow.Toggle(_view, () => new ClipboardFlyout { DataContext = this }) as ClipboardFlyout;
        if (_flyout is null) return;
        _pasteTarget = foreground;
        _flyout.Closed += (_, _) =>
        {
            _flyout = null;
            Rows.Clear(); // Bilder nicht unnötig an die Liste binden
        };
    }

    private void RebuildRows()
    {
        var selectedId = Rows.FirstOrDefault(r => r.IsSelected) is { } selected ? Key(selected) : null;
        Rows.Clear();
        foreach (var text in _clipboard.Pinned)
            Rows.Add(new ClipboardRow(null, text, this));
        foreach (var item in _service.Items.Take(_clipboard.MaxItems))
        {
            if (!_clipboard.KeepImages && item.Kind == ClipboardItemKind.Image) continue;
            if (item.Kind == ClipboardItemKind.Text && _clipboard.Pinned.Contains(item.Text!)) continue;
            Rows.Add(new ClipboardRow(item, null, this));
        }
        foreach (var hidden in Rows.Where(r => !r.Matches(_search)).ToList()) Rows.Remove(hidden);
        var keep = Rows.FirstOrDefault(r => Key(r) == selectedId) ?? Rows.FirstOrDefault();
        if (keep is not null) keep.IsSelected = true;
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(EmptyVisibility));
    }

    private static string Key(ClipboardRow row) => row.Item?.Id ?? "pin:" + row.PinnedText;

    /// <summary>Pfeiltasten im Popup.</summary>
    internal void MoveSelection(int delta)
    {
        if (Rows.Count == 0) return;
        int index = Math.Max(0, Rows.IndexOf(Rows.FirstOrDefault(r => r.IsSelected)!));
        int next = Math.Clamp(index + delta, 0, Rows.Count - 1);
        foreach (var row in Rows) row.IsSelected = false;
        Rows[next].IsSelected = true;
        _flyout?.ScrollIntoView(next);
    }

    internal void SelectCurrent()
    {
        if (Rows.FirstOrDefault(r => r.IsSelected) is { } row) Select(row);
    }

    internal void RemoveCurrent()
    {
        if (Rows.FirstOrDefault(r => r.IsSelected) is { } row) Remove(row);
    }

    internal void Select(ClipboardRow row)
    {
        bool ok = row.Item is { } item ? _service.Copy(item) : _service.CopyText(row.PinnedText!);
        var target = _pasteTarget;
        _flyout?.Close();
        if (ok && _clipboard.PasteOnSelect) PasteHelper.PasteInto(target);
    }

    internal void TogglePin(ClipboardRow row)
    {
        if (row.Text is not { } text) return;
        bool pin = !row.IsPinned;
        _updateEntry(EntryId, entry =>
        {
            entry.Clipboard ??= new ClipboardSettings();
            entry.Clipboard.Pinned.Remove(text);
            if (pin) entry.Clipboard.Pinned.Insert(0, text);
        });
    }

    internal void Remove(ClipboardRow row)
    {
        if (row.IsPinned)
        {
            TogglePin(row);
            return;
        }
        if (row.Item is { } item) _service.Remove(item);
    }

    public void Dispose()
    {
        FlyoutWindow.CloseFor(_view);
        _service.Changed -= OnHistoryChanged;
        _service.Release(this);
    }
}
