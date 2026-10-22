using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace Veyra.Widgets.Draw;

/// <summary>
/// Werkzeugleiste beim Zeichnen: Werkzeuge, Farben, Stärken und Aktionen, jeweils im 2er-Raster – senkrecht in zwei
/// Spalten, waagerecht in zwei Zeilen. Mit der Maus am Rand bzw. Griff verschiebbar.
/// </summary>
internal sealed class DrawToolbar : Border
{
    private static readonly Brush CardBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xF2, 0x1E, 0x1F, 0x26)));
    private static readonly Brush CardBorderBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush HoverBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush SelectedBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x55, 0x3D, 0xAE, 0xE9)));
    private static readonly Brush DividerBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush IconBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xEE, 0xEE, 0xF2)));
    private static readonly Brush DangerBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)));
    private static readonly Brush DangerBackBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x40, 0xDA, 0x44, 0x53)));

    private const double ButtonSize = 34;

    private readonly DrawOverlay _overlay;
    private readonly bool _horizontal;
    private readonly List<(ToolButton Button, Func<bool> IsSelected)> _selectable = [];
    private Point? _dragStart;
    private Point _dragOrigin;

    public DrawToolbar(DrawOverlay overlay)
    {
        _overlay = overlay;
        _horizontal = DrawingSession.ToolbarHorizontal;
        Background = CardBrush;
        BorderBrush = CardBorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(4);
        Cursor = Cursors.SizeAll;
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.5 };

        var panel = new StackPanel { Orientation = _horizontal ? Orientation.Horizontal : Orientation.Vertical };
        panel.Children.Add(Header());
        panel.Children.Add(Section(
            Tool(DrawTool.Pen, Glyph(""), "Stift"),
            Tool(DrawTool.Marker, Glyph(""), "Textmarker"),
            Tool(DrawTool.Line, Shape("M4,20 L20,4"), "Linie (Umschalt: in 45°-Schritten)"),
            Tool(DrawTool.Arrow, Shape("M4,20 L19,5 M10,5 L19,5 L19,14"), "Pfeil (Umschalt: in 45°-Schritten)"),
            Tool(DrawTool.Rectangle, Shape("M4,6 L20,6 L20,18 L4,18 Z"), "Rechteck (Umschalt: Quadrat)"),
            Tool(DrawTool.Ellipse, Shape("M3,12 A9,7 0 1 1 21,12 A9,7 0 1 1 3,12 Z"), "Ellipse (Umschalt: Kreis)"),
            Tool(DrawTool.Eraser, Glyph(""), "Radierer – ganze Striche entfernen"),
            Action(Glyph(""), "Rückgängig (Strg + Z)", () => _overlay.Undo())));
        panel.Children.Add(Section(DrawingSession.Palette.Select(ColorSwatch).ToArray()));
        panel.Children.Add(Section(DrawingSession.Thicknesses.Select(SizeButton).ToArray()));
        var clear = Action(Glyph("", DangerBrush), "Alles löschen (Entf) – der Bildschirm ist wieder normal", () => DrawingSession.ClearAll());
        clear.Background = DangerBackBrush;
        clear.RestingBrush = DangerBackBrush;
        panel.Children.Add(Section(
            clear,
            Action(Glyph(""), "Fertig (Esc) – das Gezeichnete bleibt stehen", () => DrawingSession.Stop())));
        Child = panel;
        Refresh();
    }

    /// <summary>Auswahl (Werkzeug, Farbe, Stärke) hervorheben.</summary>
    public void Refresh()
    {
        foreach (var (button, isSelected) in _selectable) button.IsSelected = isSelected();
    }

    /// <summary>Griff zum Verschieben und Umschalten zwischen senkrecht und waagerecht.</summary>
    private FrameworkElement Header()
    {
        var grip = new Path
        {
            Data = Geometry.Parse(_horizontal
                ? "M0,0 h2 v2 h-2 Z M4,0 h2 v2 h-2 Z M0,4 h2 v2 h-2 Z M4,4 h2 v2 h-2 Z M0,8 h2 v2 h-2 Z M4,8 h2 v2 h-2 Z"
                : "M0,0 h2 v2 h-2 Z M0,4 h2 v2 h-2 Z M4,0 h2 v2 h-2 Z M4,4 h2 v2 h-2 Z M8,0 h2 v2 h-2 Z M8,4 h2 v2 h-2 Z"),
            Fill = IconBrush,
            Opacity = 0.45,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var rotate = Action(Glyph("", size: 13), _horizontal ? "Werkzeugleiste senkrecht" : "Werkzeugleiste waagerecht",
            DrawingSession.ToggleOrientation);
        rotate.Width = rotate.Height = 26;
        var header = new DockPanel { Background = Brushes.Transparent, ToolTip = "Ziehen zum Verschieben" };
        DockPanel.SetDock(rotate, _horizontal ? Dock.Bottom : Dock.Right);
        header.Children.Add(rotate);
        header.Children.Add(grip);
        if (_horizontal) header.Width = ButtonSize - 6;
        else header.Height = 26;
        header.Margin = _horizontal ? new Thickness(0, 0, 4, 0) : new Thickness(0, 0, 0, 4);
        return header;
    }

    /// <summary>Eine Gruppe im 2er-Raster, durch eine feine Linie von der vorigen getrennt.</summary>
    private FrameworkElement Section(params UIElement[] items)
    {
        var grid = _horizontal ? new UniformGrid { Rows = 2 } : new UniformGrid { Columns = 2 };
        foreach (var item in items) grid.Children.Add(item);
        return new Border
        {
            BorderBrush = DividerBrush,
            BorderThickness = _horizontal ? new Thickness(1, 0, 0, 0) : new Thickness(0, 1, 0, 0),
            Padding = _horizontal ? new Thickness(4, 0, 4, 0) : new Thickness(0, 4, 0, 4),
            Child = grid,
        };
    }

    private ToolButton Tool(DrawTool tool, UIElement icon, string toolTip)
    {
        var button = Action(icon, toolTip, () => DrawingSession.Tool = tool);
        _selectable.Add((button, () => DrawingSession.Tool == tool));
        return button;
    }

    private ToolButton ColorSwatch(Color color)
    {
        var swatch = new Ellipse
        {
            Width = 18,
            Height = 18,
            Fill = Freeze(new SolidColorBrush(color)),
            Stroke = Freeze(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF))),
            StrokeThickness = 1,
        };
        var button = Action(swatch, "Farbe", () => DrawingSession.Color = color);
        _selectable.Add((button, () => DrawingSession.Color == color));
        return button;
    }

    private ToolButton SizeButton(double thickness)
    {
        var dot = new Ellipse { Width = thickness + 2, Height = thickness + 2, Fill = IconBrush };
        var button = Action(dot, $"Stärke {thickness:0} px", () => DrawingSession.Thickness = thickness);
        _selectable.Add((button, () => Math.Abs(DrawingSession.Thickness - thickness) < 0.01));
        return button;
    }

    private ToolButton Action(UIElement icon, string toolTip, System.Action click)
    {
        var button = new ToolButton { Child = icon, ToolTip = toolTip };
        button.MouseLeftButtonUp += (_, e) =>
        {
            if (!button.IsPressed) return;
            button.IsPressed = false;
            e.Handled = true;
            click();
            Refresh();
            _overlay.ApplyTool();
        };
        return button;
    }

    private static TextBlock Glyph(string glyph, Brush? brush = null, double size = 16) => new()
    {
        Text = glyph,
        FontFamily = (FontFamily)Application.Current.Resources["WidgetIconFont"],
        FontSize = size,
        Foreground = brush ?? IconBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static Path Shape(string data) => new()
    {
        Data = Geometry.Parse(data),
        Stroke = IconBrush,
        StrokeThickness = 1.8,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Width = 18,
        Height = 18,
        Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    // ---- Verschieben -----------------------------------------------------------------------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (e.Handled) return;
        _dragStart = e.GetPosition(_overlay);
        _dragOrigin = _overlay.ToolbarOrigin;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is not { } start) return;
        _overlay.MoveToolbar(_dragOrigin + (e.GetPosition(_overlay) - start), remember: true);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragStart is null) return;
        _dragStart = null;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    private static T Freeze<T>(T brush) where T : Freezable
    {
        brush.Freeze();
        return brush;
    }

    /// <summary>Quadratische Schaltfläche mit Hover- und Auswahlzustand.</summary>
    private sealed class ToolButton : Border
    {
        private bool _selected;

        public ToolButton()
        {
            Width = ButtonSize;
            Height = ButtonSize;
            Margin = new Thickness(1);
            CornerRadius = new CornerRadius(7);
            Background = Brushes.Transparent;
            Cursor = Cursors.Hand;
        }

        public Brush RestingBrush { get; set; } = Brushes.Transparent;

        public bool IsPressed { get; set; }

        public bool IsSelected
        {
            get => _selected;
            set
            {
                _selected = value;
                UpdateBackground();
            }
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            IsPressed = true;
            e.Handled = true; // nicht die Leiste verschieben
        }

        protected override void OnMouseEnter(MouseEventArgs e)
        {
            base.OnMouseEnter(e);
            UpdateBackground();
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            IsPressed = false;
            UpdateBackground();
        }

        private void UpdateBackground() =>
            Background = _selected ? SelectedBrush : IsMouseOver ? HoverBrush : RestingBrush;
    }
}
