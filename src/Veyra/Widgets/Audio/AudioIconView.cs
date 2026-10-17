using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using Veyra.Audio;

namespace Veyra.Widgets.Audio;

/// <summary>
/// Zeigt ein <see cref="AudioIcon"/> an – Schriftzeichen oder Vektorpfad, beide in derselben Größe
/// und in der geerbten Vordergrundfarbe. Wird in der Leiste und im Einstellungsfenster verwendet.
/// </summary>
public sealed class AudioIconView : UserControl
{
    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(AudioIconView), new PropertyMetadata(null, (d, _) => ((AudioIconView)d).Rebuild()));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(AudioIconView), new PropertyMetadata(16.0, (d, _) => ((AudioIconView)d).Rebuild()));

    public AudioIconView()
    {
        Focusable = false;
        IsTabStop = false;
        Rebuild();
    }

    public string? IconKey
    {
        get => (string?)GetValue(IconKeyProperty);
        set => SetValue(IconKeyProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private void Rebuild()
    {
        var icon = AudioIcons.Get(IconKey);
        Width = Size;
        Height = Size;

        var geometry = AudioIcons.GetGeometry(icon);
        if (geometry is null)
        {
            Content = new TextBlock
            {
                Text = icon.Glyph,
                FontFamily = new FontFamily(AudioIcons.FontFamily),
                FontSize = Size,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return;
        }

        // 24×24-Raster in einer Viewbox: Linienstärke skaliert mit und passt zur Strichstärke der Schriftsymbole.
        var path = new Path
        {
            Data = geometry,
            StrokeThickness = 1.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        };
        path.SetBinding(Shape.StrokeProperty, new Binding(nameof(Foreground)) { Source = this });

        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        Content = new Viewbox { Child = canvas, Width = Size, Height = Size, Stretch = Stretch.Uniform };
    }
}
