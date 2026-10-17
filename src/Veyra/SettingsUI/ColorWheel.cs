using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Veyra.Core;

namespace Veyra.SettingsUI;

/// <summary>
/// Kreisförmiges Farbspektrum (HSV): Winkel = Farbton, Abstand zur Mitte = Sättigung,
/// Regler darunter = Helligkeit. Klicken oder gedrückt halten und ziehen wählt die Farbe.
/// </summary>
public sealed class ColorWheel : UserControl
{
    private const int WheelSize = 220;
    private const double Radius = WheelSize / 2.0;
    private static readonly BitmapSource WheelBitmap = CreateWheelBitmap();

    public static readonly DependencyProperty HexProperty = DependencyProperty.Register(
        nameof(Hex), typeof(string), typeof(ColorWheel),
        new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnHexChanged));

    private readonly Grid _wheel;
    private readonly Ellipse _shade;
    private readonly Canvas _markerLayer;
    private readonly Grid _marker;
    private readonly Slider _brightness;
    private double _hue;
    private double _saturation;
    private double _value = 1;
    private byte _alpha = 255;
    private bool _updating;

    public ColorWheel()
    {
        _wheel = new Grid { Width = WheelSize, Height = WheelSize, Background = Brushes.Transparent, Cursor = Cursors.Cross };
        _wheel.Children.Add(new Image { Source = WheelBitmap, Width = WheelSize, Height = WheelSize });

        // Dunkle Ebene simuliert die Helligkeit, ohne die Bitmap neu zu berechnen.
        _shade = new Ellipse { Fill = Brushes.Black, Opacity = 0, IsHitTestVisible = false };
        _wheel.Children.Add(_shade);

        _marker = new Grid { Width = 16, Height = 16, IsHitTestVisible = false };
        _marker.Children.Add(new Ellipse { Stroke = Brushes.Black, StrokeThickness = 1 });
        _marker.Children.Add(new Ellipse { Stroke = Brushes.White, StrokeThickness = 2, Margin = new Thickness(1) });
        _markerLayer = new Canvas { IsHitTestVisible = false };
        _markerLayer.Children.Add(_marker);
        _wheel.Children.Add(_markerLayer);

        _wheel.MouseLeftButtonDown += (_, e) =>
        {
            _wheel.CaptureMouse();
            Pick(e.GetPosition(_wheel));
            e.Handled = true;
        };
        _wheel.MouseMove += (_, e) =>
        {
            if (_wheel.IsMouseCaptured) Pick(e.GetPosition(_wheel));
        };
        _wheel.MouseLeftButtonUp += (_, _) => _wheel.ReleaseMouseCapture();

        _brightness = new Slider { Minimum = 0, Maximum = 1, Value = 1, Width = WheelSize, SmallChange = 0.01, LargeChange = 0.1 };
        _brightness.ValueChanged += (_, e) =>
        {
            if (_updating) return;
            _value = e.NewValue;
            Commit();
        };

        var panel = new StackPanel();
        panel.Children.Add(_wheel);
        panel.Children.Add(new TextBlock { Text = "Helligkeit", Margin = new Thickness(0, 12, 0, 2), Opacity = 0.75 });
        panel.Children.Add(_brightness);
        Content = panel;

        LoadFromHex(Hex);
    }

    public string Hex
    {
        get => (string)GetValue(HexProperty);
        set => SetValue(HexProperty, value);
    }

    private static void OnHexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var wheel = (ColorWheel)d;
        if (!wheel._updating) wheel.LoadFromHex(e.NewValue as string);
    }

    private void Pick(Point position)
    {
        double dx = position.X - Radius;
        double dy = position.Y - Radius;
        _saturation = Math.Min(Math.Sqrt(dx * dx + dy * dy) / Radius, 1.0);
        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        // Aus Schwarz heraus soll ein Klick in den Kreis sichtbar eine Farbe ergeben.
        if (_value < 0.1)
        {
            _value = 1;
        }
        Commit();
    }

    private void Commit()
    {
        UpdateVisuals();
        var (r, g, b) = HsvToRgb(_hue, _saturation, _value);
        _updating = true;
        Hex = _alpha == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{_alpha:X2}{r:X2}{g:X2}{b:X2}";
        _updating = false;
    }

    private void LoadFromHex(string? hex)
    {
        if (!ColorUtil.TryParse(hex, out var color)) return;
        _alpha = color.A;
        (_hue, _saturation, _value) = RgbToHsv(color.R, color.G, color.B);
        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        _updating = true;
        _brightness.Value = _value;
        _updating = false;
        _shade.Opacity = 1 - _value;

        double angle = _hue * Math.PI / 180;
        Canvas.SetLeft(_marker, Radius + Math.Cos(angle) * _saturation * Radius - _marker.Width / 2);
        Canvas.SetTop(_marker, Radius + Math.Sin(angle) * _saturation * Radius - _marker.Height / 2);
    }

    private static BitmapSource CreateWheelBitmap()
    {
        var pixels = new byte[WheelSize * WheelSize * 4];
        for (int y = 0; y < WheelSize; y++)
        {
            for (int x = 0; x < WheelSize; x++)
            {
                double dx = x + 0.5 - Radius;
                double dy = y + 0.5 - Radius;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                double alpha = Math.Clamp(Radius - distance, 0, 1); // 1 px weicher Rand
                if (alpha <= 0) continue;

                double hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                var (r, g, b) = HsvToRgb(hue, Math.Min(distance / Radius, 1), 1);
                int i = (y * WheelSize + x) * 4;
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = (byte)(alpha * 255);
            }
        }

        var bitmap = BitmapSource.Create(WheelSize, WheelSize, 96, 96, PixelFormats.Bgra32, null, pixels, WheelSize * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = (int)(h / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return ((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    private static (double H, double S, double V) RgbToHsv(byte red, byte green, byte blue)
    {
        double r = red / 255.0, g = green / 255.0, b = blue / 255.0;
        double max = Math.Max(r, Math.Max(g, b));
        double min = Math.Min(r, Math.Min(g, b));
        double delta = max - min;

        double h = 0;
        if (delta > 0)
        {
            if (max == r) h = 60 * ((g - b) / delta % 6);
            else if (max == g) h = 60 * ((b - r) / delta + 2);
            else h = 60 * ((r - g) / delta + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : delta / max, max);
    }
}
