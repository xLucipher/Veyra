using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Veyra.Widgets.Media;

/// <summary>
/// Einzeiliger Text, der durchläuft, wenn er nicht in die verfügbare Breite passt: kurz stehen, dann gleichmäßig nach
/// links wandern, bis die zweite Kopie dahinter wieder am Anfang steht – nahtlos, danach von vorn. Passt er, steht er
/// still. Schrift und Farbe erbt er wie ein TextBlock (TextElement.FontFamily, -FontSize, -Foreground …).
/// Sparsam: höchstens 30 Bilder je Sekunde und nur, solange der Text zu lang und sichtbar ist.
/// </summary>
public sealed class MarqueeText : FrameworkElement
{
    private const double Gap = 36;    // Abstand zwischen Ende und erneutem Anfang (px)
    private const double Speed = 32;  // px je Sekunde
    private static readonly TimeSpan Pause = TimeSpan.FromSeconds(2.5);

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(MarqueeText),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure, (d, e) => ((MarqueeText)d).OnTextChanged()));

    public static readonly DependencyProperty IsScrollEnabledProperty = DependencyProperty.Register(
        nameof(IsScrollEnabled), typeof(bool), typeof(MarqueeText),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure));

    private readonly TextBlock _first = new() { TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _second = new();
    private readonly TranslateTransform _shift = new();
    private double _animatedDistance;

    public MarqueeText()
    {
        _first.RenderTransform = _shift;
        _second.RenderTransform = _shift;
        AddVisualChild(_first);
        AddVisualChild(_second);
        ClipToBounds = true;
        // Fläche ausgeblendet (z. B. Vollbild): Animation anhalten, beim Einblenden neu starten.
        IsVisibleChanged += (_, _) => UpdateAnimation(_distance);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Aus: zu lange Texte enden mit „…“ statt durchzulaufen.</summary>
    public bool IsScrollEnabled
    {
        get => (bool)GetValue(IsScrollEnabledProperty);
        set => SetValue(IsScrollEnabledProperty, value);
    }

    protected override int VisualChildrenCount => 2;

    protected override Visual GetVisualChild(int index) => index == 0 ? _first : _second;

    private double _naturalWidth;
    private double _distance;

    private void OnTextChanged()
    {
        _first.Text = Text ?? "";
        _second.Text = Text ?? "";
        // Neuer Text: von vorn beginnen (auch wenn die Länge zufällig gleich ist).
        _animatedDistance = -1;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var unlimited = new Size(double.PositiveInfinity, availableSize.Height);
        _first.Measure(unlimited);
        _second.Measure(unlimited);
        _naturalWidth = _first.DesiredSize.Width;
        // Ohne Durchlaufen: mit der echten Breite messen – nur dann kürzt der TextBlock mit „…“.
        if (!IsScrollEnabled && _naturalWidth > availableSize.Width) _first.Measure(availableSize);
        double width = double.IsInfinity(availableSize.Width) ? _naturalWidth : Math.Min(_naturalWidth, availableSize.Width);
        return new Size(width, _first.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double height = finalSize.Height;
        bool overflow = IsScrollEnabled && _naturalWidth > finalSize.Width + 0.5;
        if (overflow)
        {
            _first.Arrange(new Rect(0, 0, _naturalWidth, height));
            _second.Arrange(new Rect(_naturalWidth + Gap, 0, _naturalWidth, height));
            _distance = _naturalWidth + Gap;
        }
        else
        {
            // Passt bzw. Durchlaufen aus: ein Text, bei Bedarf mit „…“ gekürzt.
            _first.Arrange(new Rect(0, 0, finalSize.Width, height));
            _second.Arrange(new Rect(-10000, 0, 0, height));
            _distance = 0;
        }
        UpdateAnimation(_distance);
        return finalSize;
    }

    /// <summary>Animation starten, anpassen oder anhalten – nur bei Änderung, damit sie nicht bei jedem Layout neu beginnt.</summary>
    private void UpdateAnimation(double distance)
    {
        if (!IsVisible) distance = 0;
        if (Math.Abs(distance - _animatedDistance) < 0.5) return;
        _animatedDistance = distance;

        if (distance <= 0)
        {
            _shift.BeginAnimation(TranslateTransform.XProperty, null);
            _shift.X = 0;
            return;
        }

        // Stehen → wandern, bis die Kopie genau dort steht, wo der Anfang stand → Sprung zurück (unsichtbar) → stehen …
        var move = TimeSpan.FromSeconds(distance / Speed);
        var animation = new DoubleAnimationUsingKeyFrames
        {
            Duration = Pause + move,
            RepeatBehavior = RepeatBehavior.Forever,
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(Pause)),
                new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(Pause + move)),
            },
        };
        Timeline.SetDesiredFrameRate(animation, 30);
        _shift.BeginAnimation(TranslateTransform.XProperty, animation);
    }
}
