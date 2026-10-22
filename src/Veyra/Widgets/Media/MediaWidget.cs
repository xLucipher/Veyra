using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Veyra.Configuration;
using Veyra.Core;
using Veyra.Media;

namespace Veyra.Widgets.Media;

/// <summary>
/// Gerade gespielte Medien: Cover, Titel, Interpret und Schaltflächen Zurück/Wiedergabe/Weiter. Quelle ist die
/// Windows-Mediensteuerung (<see cref="MediaSessionService"/>), gemeinsam für alle Medien-Widgets; abgefragt wird
/// nur, solange ein Widget läuft. Ein Klick auf Cover oder Titel öffnet die Auswahl aller Wiedergaben (Spotify,
/// Browser-Tabs …); die dort gewählte bleibt angezeigt, bis sie endet oder eine andere gewählt wird.
/// </summary>
public sealed class MediaWidget : ObservableObject, IBarWidget, IHotkeyTarget
{
    private const int CoverDecodeSize = 96;

    private readonly string _entryId;
    private readonly MediaSessionService _service;
    private MediaView? _view;
    private MediaSettings _media = new();
    private bool _running;
    private byte[]? _coverSource;

    private string _title = "";
    private string _artist = "";
    private string _toolTipText = "";
    private string _playPauseGlyph = "";
    private ImageSource? _cover;
    private Brush _foreground = Brushes.White;
    private Brush _iconBrush = Brushes.White;
    private FontFamily _fontFamily = new("Segoe UI");
    private Visibility _visibility = Visibility.Visible;
    private Visibility _coverVisibility = Visibility.Collapsed;
    private Visibility _artistVisibility = Visibility.Collapsed;
    private Visibility _controlsVisibility = Visibility.Visible;
    private double _maxTextWidth = 200;
    private double _textOpacity = 1;
    private bool _canPrevious;
    private bool _canNext;
    private bool _canPlayPause;

    public MediaWidget(string entryId, MediaSessionService service)
    {
        _entryId = entryId;
        _service = service;
    }

    public string EntryId => _entryId;

    public FrameworkElement View => _view ??= new MediaView { DataContext = this };

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string Artist { get => _artist; private set => SetProperty(ref _artist, value); }
    public string ToolTipText { get => _toolTipText; private set => SetProperty(ref _toolTipText, value); }
    public string PlayPauseGlyph { get => _playPauseGlyph; private set => SetProperty(ref _playPauseGlyph, value); }
    public ImageSource? Cover { get => _cover; private set => SetProperty(ref _cover, value); }
    public Brush Foreground { get => _foreground; private set => SetProperty(ref _foreground, value); }
    public Brush IconBrush { get => _iconBrush; private set => SetProperty(ref _iconBrush, value); }
    public FontFamily FontFamily { get => _fontFamily; private set => SetProperty(ref _fontFamily, value); }

    /// <summary>Ganzes Widget; „Ohne Wiedergabe ausblenden“ klappt es zusammen.</summary>
    public Visibility Visibility { get => _visibility; private set => SetProperty(ref _visibility, value); }

    public Visibility CoverVisibility { get => _coverVisibility; private set => SetProperty(ref _coverVisibility, value); }
    public Visibility ArtistVisibility { get => _artistVisibility; private set => SetProperty(ref _artistVisibility, value); }
    public Visibility ControlsVisibility { get => _controlsVisibility; private set => SetProperty(ref _controlsVisibility, value); }
    public double MaxTextWidth { get => _maxTextWidth; private set => SetProperty(ref _maxTextWidth, value); }

    /// <summary>Feste Breite: genau <see cref="MaxTextWidth"/>; sonst NaN (= nach Inhalt, höchstens <see cref="MaxTextWidth"/>).</summary>
    public double TextWidth { get => _textWidth; private set => SetProperty(ref _textWidth, value); }

    private double _textWidth = double.NaN;

    /// <summary>Zu lange Texte laufen durch statt mit … zu enden.</summary>
    public bool ScrollText { get => _scrollText; private set => SetProperty(ref _scrollText, value); }

    private bool _scrollText = true;

    /// <summary>Feste Breite ohne Cover: Platzhalter an seiner Stelle, damit die Schaltflächen nicht wandern.</summary>
    public Visibility CoverPlaceholderVisibility { get => _coverPlaceholderVisibility; private set => SetProperty(ref _coverPlaceholderVisibility, value); }

    private Visibility _coverPlaceholderVisibility = Visibility.Collapsed;

    /// <summary>Pausiert bzw. keine Wiedergabe: Text abgeschwächt.</summary>
    public double TextOpacity { get => _textOpacity; private set => SetProperty(ref _textOpacity, value); }

    public bool CanPrevious { get => _canPrevious; private set => SetProperty(ref _canPrevious, value); }
    public bool CanNext { get => _canNext; private set => SetProperty(ref _canNext, value); }
    public bool CanPlayPause { get => _canPlayPause; private set => SetProperty(ref _canPlayPause, value); }

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _media = entry?.Media ?? new MediaSettings();
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        Foreground = entry?.LabelColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        IconBrush = entry?.IconColor is { } icon ? ColorUtil.ToBrush(icon, Colors.White) : barForeground;
        FontFamily = new FontFamily(bar.Appearance.FontFamily);
        MaxTextWidth = _media.MaxTextWidth;
        TextWidth = _media.FixedTextWidth ? _media.MaxTextWidth : double.NaN;
        ScrollText = _media.ScrollLongText;
        ControlsVisibility = _media.ShowControls ? Visibility.Visible : Visibility.Collapsed;
        Update();
        UpdateCoverVisibility(); // Einstellungen geändert, Cover gleich: SetCover kehrt früh zurück
    }

    public void Start()
    {
        if (_running) return;
        _running = true;
        _service.Changed += OnChanged;
        _service.Acquire();
        Update();
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        _service.Changed -= OnChanged;
        _service.Release();
    }

    public void Refresh() => Update();

    public void Dispose()
    {
        if (_view is not null) FlyoutWindow.CloseFor(_view);
        Stop();
    }

    /// <summary>Tastenkürzel – wirken auch bei ausgeblendeter Fläche.</summary>
    public void OnHotkey(string action)
    {
        switch (action)
        {
            case WidgetHotkeys.PlayPause: _service.PlayPause(); break;
            case WidgetHotkeys.NextTrack: _service.Next(); break;
            case WidgetHotkeys.PreviousTrack: _service.Previous(); break;
        }
    }

    internal void PlayPause() => _service.PlayPause();
    internal void Next() => _service.Next();
    internal void Previous() => _service.Previous();

    // ---- Auswahl der Wiedergabe (Popup) -----------------------------------------------------------
    private MediaFlyout? _flyout;

    /// <summary>Alle Wiedergaben, solange die Auswahl offen ist.</summary>
    public System.Collections.ObjectModel.ObservableCollection<MediaSourceRow> Sources { get; } = [];

    public Visibility NoSourcesVisibility => Sources.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    internal void ToggleFlyout(FrameworkElement anchor)
    {
        _flyout = FlyoutWindow.Toggle(anchor, () => new MediaFlyout { DataContext = this }) as MediaFlyout;
        if (_flyout is null) return;
        _service.AcquireList();
        SyncSources();
        _flyout.Closed += (_, _) =>
        {
            _flyout = null;
            _service.ReleaseList();
            Sources.Clear();
        };
    }

    private void SelectSource(MediaSourceRow row)
    {
        _service.Select(row.Key);
        foreach (var r in Sources) r.IsSelected = r == row;
    }

    /// <summary>Liste an den Stand des Dienstes angleichen – Zeilen per Schlüssel wiederverwenden (kein Flackern).</summary>
    private void SyncSources()
    {
        var states = _service.Sessions;
        for (int i = Sources.Count - 1; i >= 0; i--)
            if (states.All(s => s.Key != Sources[i].Key)) Sources.RemoveAt(i);
        for (int i = 0; i < states.Count; i++)
        {
            var state = states[i];
            int index = IndexOf(state.Key);
            if (index < 0)
            {
                Sources.Insert(Math.Min(i, Sources.Count), new MediaSourceRow(_service, state, SelectSource));
                continue;
            }
            if (index != i && i < Sources.Count) Sources.Move(index, i);
            Sources[i].Update(state);
        }
        foreach (var row in Sources) row.IsSelected = row.Key == _service.Current.Key;
        OnPropertyChanged(nameof(NoSourcesVisibility));
    }

    private int IndexOf(string key)
    {
        for (int i = 0; i < Sources.Count; i++)
            if (Sources[i].Key == key) return i;
        return -1;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        Update();
        if (_flyout is not null) SyncSources();
    }

    private void Update()
    {
        var state = _service.Current;
        Visibility = !state.HasSession && _media.HideWhenIdle ? Visibility.Collapsed : Visibility.Visible;

        if (!state.HasSession)
        {
            Title = "Keine Wiedergabe";
            Artist = "";
            ToolTipText = "Keine Wiedergabe – z. B. Spotify, ein Browser-Video oder die Medienwiedergabe starten.";
            TextOpacity = 0.6;
            PlayPauseGlyph = "";
            CanPrevious = CanNext = CanPlayPause = false;
            SetCover(null);
            ArtistVisibility = Visibility.Collapsed;
            return;
        }

        Title = state.Title.Length > 0 ? state.Title : state.Source;
        Artist = state.Artist;
        ArtistVisibility = _media.ShowArtist && Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        TextOpacity = state.IsPlaying ? 1 : 0.7;
        PlayPauseGlyph = state.IsPlaying ? "" : "";
        CanPrevious = state.CanPrevious;
        CanNext = state.CanNext;
        CanPlayPause = state.CanPlayPause;
        SetCover(_media.ShowCover ? state.Thumbnail : null);

        var lines = new List<string> { Title };
        if (state.Artist.Length > 0) lines.Add(state.Artist);
        if (state.Album.Length > 0 && state.Album != state.Title) lines.Add(state.Album);
        lines.Add($"{(state.IsPlaying ? "Wiedergabe" : state.Playback == MediaPlayback.Paused ? "Pausiert" : "Angehalten")} · {state.Source}");
        lines.Add("Klicken: Wiedergabe wählen");
        ToolTipText = string.Join("\n", lines);
    }

    /// <summary>Cover nur bei neuen Bilddaten dekodieren (klein, für die Fläche reicht das).</summary>
    private void SetCover(byte[]? data)
    {
        if (ReferenceEquals(data, _coverSource)) return;
        _coverSource = data;
        Cover = null;
        if (data is not null)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelHeight = CoverDecodeSize;
                bitmap.StreamSource = new MemoryStream(data);
                bitmap.EndInit();
                bitmap.Freeze();
                Cover = bitmap;
            }
            catch (Exception ex)
            {
                Log.Debug("Media", $"Cover nicht lesbar: {ex.Message}");
            }
        }
        UpdateCoverVisibility();
    }

    private void UpdateCoverVisibility()
    {
        CoverVisibility = Cover is null ? Visibility.Collapsed : Visibility.Visible;
        CoverPlaceholderVisibility = Cover is null && _media.ShowCover && _media.FixedTextWidth ? Visibility.Visible : Visibility.Collapsed;
    }
}
