using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Widgets.Content;

/// <summary>
/// Ein Bild aus einer Datei, z. B. ein Logo oder Foto. Die Datei wird einmal gelesen und in passender Größe
/// dekodiert (nicht in voller Auflösung im Speicher gehalten); danach läuft nichts mehr. Optional öffnet ein Klick
/// eine Datei, einen Ordner, ein Programm oder eine Webadresse.
/// </summary>
public sealed class ImageWidget : IBarWidget
{
    /// <summary>Platz für hohe Bildschirmskalierungen und Vergrößerung, ohne unscharf zu werden.</summary>
    private const double DecodeOversampling = 2.5;

    private readonly string _entryId;
    private readonly Border _view;
    private readonly TextBlock _placeholder;
    private string _loadedKey = "";
    private string _clickTarget = "";

    public ImageWidget(string entryId)
    {
        _entryId = entryId;
        _placeholder = new TextBlock
        {
            Text = "", // Foto
            FontFamily = (FontFamily)Application.Current.FindResource("WidgetIconFont"),
            FontSize = 16,
            Opacity = 0.5,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _view = new Border
        {
            Focusable = false,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 4, 0),
            Background = Brushes.Transparent, // auch ohne Bild greif- und klickbar
        };
        _view.MouseLeftButtonUp += OnClick;
    }

    public FrameworkElement View => _view;

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        var image = entry?.Image ?? new ImageSettings();
        _clickTarget = image.ClickTarget;
        _view.Cursor = _clickTarget.Length > 0 ? Cursors.Hand : null;
        _view.CornerRadius = new CornerRadius(image.CornerRadius);
        _view.Opacity = image.Opacity / 100;

        double scale = entry?.Scale ?? 1;
        string key = $"{image.Path}|{image.Height}|{image.Width}|{scale}|{File.GetLastWriteTimeUtc(image.Path).Ticks}";
        if (key == _loadedKey) return;
        _loadedKey = key;

        var bitmap = Load(image.Path, image.Height * scale * DecodeOversampling, out string? error);
        if (bitmap is null)
        {
            _placeholder.Foreground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
            _view.Child = _placeholder;
            _view.Background = Brushes.Transparent;
            _view.Width = Math.Max(image.Height, 24);
            _view.Height = image.Height;
            _view.ToolTip = error;
            return;
        }

        double width = image.Width > 0 ? image.Width : image.Height * bitmap.PixelWidth / Math.Max(1, bitmap.PixelHeight);
        _view.Child = null;
        // Als Hintergrund des Rahmens: so gelten die abgerundeten Ecken auch für das Bild.
        var brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        _view.Background = brush;
        _view.Width = Math.Round(width);
        _view.Height = image.Height;
        _view.ToolTip = null;
    }

    /// <summary>Liest und dekodiert die Datei höchstens in <paramref name="maxHeight"/> Pixeln Höhe. Die Datei bleibt nicht gesperrt.</summary>
    private static BitmapSource? Load(string path, double maxHeight, out string? error)
    {
        error = null;
        if (path.Length == 0)
        {
            error = "Noch kein Bild gewählt – in den Einstellungen unter Widgets auswählen.";
            return null;
        }
        if (!File.Exists(path))
        {
            error = $"Bild nicht gefunden:\n{path}";
            return null;
        }
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // Zuerst nur die Kopfdaten, um nicht über die Originalgröße hinaus zu dekodieren.
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            int sourceHeight = frame.PixelHeight;
            stream.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.StreamSource = stream;
            if (sourceHeight > maxHeight) bitmap.DecodePixelHeight = (int)Math.Ceiling(maxHeight);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Warning("Image", $"Bild '{path}' konnte nicht geladen werden: {ex.Message}");
            error = $"Bild konnte nicht geladen werden:\n{path}";
            return null;
        }
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (_clickTarget.Length == 0) return;
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(_clickTarget)) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warning("Image", $"'{_clickTarget}' konnte nicht geöffnet werden: {ex.Message}");
        }
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

    public void Dispose() => _view.MouseLeftButtonUp -= OnClick;
}
