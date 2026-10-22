using System.Windows;
using System.Windows.Media;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Widgets.Content;

/// <summary>
/// Freier Text als Notiz. Ein Klick öffnet ein Popup zum Bearbeiten; gespeichert wird beim Schließen, und nur
/// wenn sich der Text geändert hat (jedes Speichern legt eine Sicherung an). Sonst läuft nichts.
/// </summary>
public sealed class NoteWidget : ObservableObject, IBarWidget
{

    private readonly string _entryId;
    private readonly Action<string, Action<WidgetEntry>> _updateEntry;
    private NoteView? _view;
    private NoteSettings _note = new();

    private string _displayText = "";
    private Brush _foreground = Brushes.White;
    private Brush _background = Brushes.Transparent;
    private FontFamily _fontFamily = new("Segoe UI");
    private double _fontSize = 13;
    private FontWeight _fontWeight = FontWeights.Normal;
    private double _maxTextWidth = double.PositiveInfinity;
    private TextWrapping _wrapping = TextWrapping.Wrap;
    private TextAlignment _alignment = TextAlignment.Left;
    private Thickness _padding = new(4, 2, 4, 2);
    private double _textOpacity = 1;
    private string _editText = "";

    public NoteWidget(string entryId, Action<string, Action<WidgetEntry>> updateEntry)
    {
        _entryId = entryId;
        _updateEntry = updateEntry;
    }

    public FrameworkElement View => _view ??= new NoteView { DataContext = this };

    public string DisplayText { get => _displayText; private set => SetProperty(ref _displayText, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public Brush Background { get => _background; private set => SetProperty(ref _background, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }
    public double FontSize { get => _fontSize; private set => SetProperty(ref _fontSize, value); }
    public FontWeight FontWeight { get => _fontWeight; private set => SetProperty(ref _fontWeight, value); }
    public double MaxTextWidth { get => _maxTextWidth; private set => SetProperty(ref _maxTextWidth, value); }
    public TextWrapping Wrapping { get => _wrapping; private set => SetProperty(ref _wrapping, value); }
    public TextAlignment Alignment { get => _alignment; private set => SetProperty(ref _alignment, value); }
    public Thickness Padding { get => _padding; private set => SetProperty(ref _padding, value); }

    /// <summary>Leere Notiz: Platzhalter abgeschwächt, damit sie sichtbar und anklickbar bleibt.</summary>
    public double TextOpacity { get => _textOpacity; private set => SetProperty(ref _textOpacity, value); }

    public bool EditOnClick => _note.EditOnClick;

    /// <summary>Text im Bearbeiten-Popup; wird beim Schließen übernommen.</summary>
    public string EditText { get => _editText; set => SetProperty(ref _editText, value ?? ""); }

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _note = entry?.Note ?? new NoteSettings();

        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        Foreground = entry?.LabelColor is { } color ? ColorUtil.ToBrush(color, Colors.White) : barForeground;
        Background = _note.Background is { } background ? ColorUtil.ToBrush(background, Colors.Transparent) : Brushes.Transparent;
        Padding = _note.Background is null ? new Thickness(4, 2, 4, 2) : new Thickness(10, 6, 10, 6);
        FontFamily = new FontFamily(_note.FontFamily.Length > 0 ? _note.FontFamily : bar.Appearance.FontFamily);
        FontSize = _note.FontSize;
        FontWeight = _note.Bold ? FontWeights.SemiBold : FontWeights.Normal;
        MaxTextWidth = _note.MaxWidth > 0 ? _note.MaxWidth : double.PositiveInfinity;
        Wrapping = _note.MaxWidth > 0 ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Alignment = _note.Alignment;

        bool empty = string.IsNullOrWhiteSpace(_note.Text);
        DisplayText = empty ? (_note.EditOnClick ? "Notiz – klicken zum Schreiben" : "Notiz") : _note.Text;
        TextOpacity = empty ? 0.5 : 1;
        OnPropertyChanged(nameof(EditOnClick));
    }

    internal void ToggleFlyout(FrameworkElement anchor)
    {
        if (!_note.EditOnClick) return;
        EditText = _note.Text;
        FlyoutWindow.Toggle(anchor, () => new NoteFlyout(this));
    }

    /// <summary>Popup geschlossen: geänderten Text dauerhaft speichern.</summary>
    internal void CommitEdit()
    {
        string text = EditText.Replace("\r\n", "\n");
        if (text == _note.Text) return;
        // Sofort anzeigen; die gespeicherte Konfiguration kommt gleich über ApplySettings zurück.
        DisplayText = text.Trim().Length > 0 ? text : DisplayText;
        TextOpacity = text.Trim().Length > 0 ? 1 : TextOpacity;
        _updateEntry(_entryId, entry =>
        {
            entry.Note ??= new NoteSettings();
            entry.Note.Text = text;
        });
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Refresh()
    {
    }

    public void Dispose()
    {
        if (_view is not null) FlyoutWindow.CloseFor(_view);
    }
}
