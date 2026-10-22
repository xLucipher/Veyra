using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Widgets.Content;

/// <summary>Trennlinie, Punkt oder leerer Abstand zwischen Widgets – reine Gestaltung, ohne Laufzeitkosten.</summary>
public sealed class SeparatorWidget : IBarWidget
{
    private readonly string _entryId;
    private readonly Border _view;

    public SeparatorWidget(string entryId)
    {
        _entryId = entryId;
        // Durchsichtiger Hintergrund: auch ein reiner Abstand bleibt greifbar (Drag & Drop, Rechtsklick).
        _view = new Border { Focusable = false, Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center };
    }

    public FrameworkElement View => _view;

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        var separator = entry?.Separator ?? new SeparatorSettings();
        // Ohne eigene Farbe: Schriftfarbe der Fläche, deutlich zurückgenommen.
        var brush = entry?.IconColor is { } color
            ? ColorUtil.ToBrush(color, Colors.White)
            : ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White, 0.35);

        double along = separator.Style == SeparatorStyle.Dot ? Math.Max(2, separator.Thickness) : separator.Length;
        double across = separator.Style == SeparatorStyle.Dot ? Math.Max(2, separator.Thickness) : separator.Thickness;
        FrameworkElement? shape = separator.Style switch
        {
            SeparatorStyle.Line => new Rectangle { Fill = brush, RadiusX = across / 2, RadiusY = across / 2 },
            SeparatorStyle.Dot => new Ellipse { Fill = brush },
            _ => null,
        };
        double width = separator.Horizontal ? along : across;
        double height = separator.Horizontal ? across : along;
        if (shape is not null)
        {
            shape.Width = width;
            shape.Height = height;
            shape.HorizontalAlignment = HorizontalAlignment.Center;
            shape.VerticalAlignment = VerticalAlignment.Center;
        }
        _view.Child = shape;
        // Ein reiner Abstand ist so lang wie eingestellt – senkrecht als Breite, waagerecht als Höhe.
        bool space = separator.Style == SeparatorStyle.Space;
        _view.MinWidth = space && !separator.Horizontal ? separator.Length : 0;
        _view.MinHeight = space ? (separator.Horizontal ? separator.Length : 4) : height;
        _view.Padding = separator.Horizontal
            ? new Thickness(0, separator.Padding, 0, separator.Padding)
            : new Thickness(separator.Padding, 0, separator.Padding, 0);
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
    }
}
