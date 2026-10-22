using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Veyra.Core;
using Veyra.Media;

namespace Veyra.Widgets.Media;

/// <summary>
/// Eine Wiedergabequelle in der Auswahl des Medien-Widgets: Quelle, Titel, Interpret, Cover und eigene Schaltflächen.
/// Wird bei jeder Aktualisierung über <see cref="MediaState.Key"/> wiederverwendet, damit die Liste nicht flackert.
/// </summary>
public sealed class MediaSourceRow : ObservableObject
{
    private readonly MediaSessionService _service;
    private MediaState _state = MediaState.None;
    private byte[]? _coverSource;
    private ImageSource? _cover;
    private bool _isSelected;

    public MediaSourceRow(MediaSessionService service, MediaState state, Action<MediaSourceRow> select)
    {
        _service = service;
        Key = state.Key;
        SelectCommand = new RelayCommand(() => select(this));
        PlayPauseCommand = new RelayCommand(() => _service.PlayPause(Key));
        NextCommand = new RelayCommand(() => _service.Next(Key));
        PreviousCommand = new RelayCommand(() => _service.Previous(Key));
        Update(state);
    }

    public string Key { get; }
    public ICommand SelectCommand { get; }
    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }

    public string Source => _state.Source;
    public string Title => _state.Title.Length > 0 ? _state.Title : "Ohne Titel";
    public string Artist => _state.Artist;
    public Visibility ArtistVisibility => _state.Artist.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool CanPrevious => _state.CanPrevious;
    public bool CanNext => _state.CanNext;
    public bool CanPlayPause => _state.CanPlayPause;
    public string PlayPauseGlyph => _state.IsPlaying ? "" : "";
    public string StateText => _state.IsPlaying ? "Wiedergabe" : _state.Playback == MediaPlayback.Paused ? "Pausiert" : "Angehalten";
    public double TextOpacity => _state.IsPlaying ? 1 : 0.7;

    public ImageSource? Cover { get => _cover; private set => SetProperty(ref _cover, value); }
    public Visibility CoverVisibility => _cover is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PlaceholderVisibility => _cover is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Diese Quelle zeigt das Widget gerade.</summary>
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

    public void Update(MediaState state)
    {
        _state = state;
        OnPropertyChanged(string.Empty); // alle abgeleiteten Werte neu lesen
        SetCover(state.Thumbnail);
    }

    private void SetCover(byte[]? data)
    {
        if (ReferenceEquals(data, _coverSource)) return;
        _coverSource = data;
        Cover = Decode(data);
        OnPropertyChanged(nameof(CoverVisibility));
        OnPropertyChanged(nameof(PlaceholderVisibility));
    }

    /// <summary>Cover klein dekodieren (für die Liste reichen 96 px).</summary>
    internal static ImageSource? Decode(byte[]? data)
    {
        if (data is null) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelHeight = 96;
            bitmap.StreamSource = new MemoryStream(data);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Debug("Media", $"Cover nicht lesbar: {ex.Message}");
            return null;
        }
    }
}
