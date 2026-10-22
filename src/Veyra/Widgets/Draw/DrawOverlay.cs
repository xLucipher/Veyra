using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Veyra.Interop;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Widgets.Draw;

/// <summary>
/// Durchsichtiges Fenster über einem ganzen Monitor, auf dem gezeichnet wird. Beim Zeichnen nimmt es die Maus an
/// (fast unsichtbarer Hintergrund) und zeigt die Werkzeugleiste; sonst lässt es alle Klicks durch und zeigt nur das
/// Gezeichnete. Linien, Pfeile, Rechtecke und Ellipsen sind ebenfalls Striche – so wirken Radierer und Rückgängig auf alles gleich.
/// </summary>
internal sealed class DrawOverlay : Window
{
    /// <summary>Alpha 1: für das Auge unsichtbar, aber Windows schickt die Mausklicks an das Fenster statt hindurch.</summary>
    private static readonly Brush HitBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x01, 0, 0, 0)));

    private readonly InkCanvas _ink;
    private readonly Canvas _layer;
    private DrawToolbar _toolbar;
    private readonly Stack<(StrokeCollection Added, StrokeCollection Removed)> _history = new();
    private bool _recording = true;
    private bool _interactive;
    private Stroke? _shape;
    private Point _shapeStart;
    private readonly System.Windows.Threading.DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public DrawOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Veyra – Zeichnen";

        _hideTimer.Tick += OnHideTimer;
        _ink = new InkCanvas { Background = Brushes.Transparent, EditingMode = InkCanvasEditingMode.None };
        _ink.Strokes.StrokesChanged += OnStrokesChanged;
        _ink.PreviewMouseLeftButtonDown += OnShapeDown;
        _ink.PreviewMouseMove += OnShapeMove;
        _ink.PreviewMouseLeftButtonUp += OnShapeUp;
        _toolbar = new DrawToolbar(this);
        _layer = new Canvas { Children = { _toolbar } };
        Content = new Grid { Children = { _ink, _layer } };
        _layer.SizeChanged += (_, _) => PlaceToolbar();
        _toolbar.SizeChanged += (_, _) => PlaceToolbar();
    }

    public bool HasStrokes => _ink.Strokes.Count > 0;

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyExStyle();
    }

    /// <summary>Über den ganzen Monitor legen (physische Pixel) und zeigen, ohne den Fokus zu nehmen.</summary>
    public void ShowOn(RECT bounds)
    {
        _hideTimer.Stop(); // noch sichtbar und schon leer gezeichnet – einfach weiterverwenden
        if (!IsVisible) Show();
        SetWindowPos(Handle, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE);
        // Monitor mit anderer Skalierung: WPF passt die Größe beim Wechsel an – dann noch einmal genau einpassen.
        if (GetWindowRect(Handle, out var rect) && !rect.Equals(bounds))
            SetWindowPos(Handle, HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height, SWP_NOACTIVATE);
    }

    /// <summary>
    /// Leeres Overlay verstecken – aber erst, nachdem es leer gezeichnet wurde. Ein Layered-Fenster behält sein letztes
    /// Bild; sofort versteckt, blitzte das alte Gezeichnete beim nächsten Einblenden kurz auf.
    /// </summary>
    public void HideWhenEmpty()
    {
        if (!IsVisible) return;
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void OnHideTimer(object? sender, EventArgs e)
    {
        _hideTimer.Stop();
        if (_interactive || HasStrokes) return;
        Hide();
        DrawingSession.UpdateTimer();
    }

    public void BringToTop() =>
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public void ActivateForDrawing()
    {
        Activate();
        SetForegroundWindow(Handle);
        Focus();
    }

    /// <summary>Zeichnen (Maus annehmen, Werkzeugleiste) oder nur anzeigen (Klicks gehen hindurch).</summary>
    public void SetInteractive(bool interactive)
    {
        _interactive = interactive;
        CancelShape();
        _ink.Background = interactive ? HitBrush : Brushes.Transparent;
        _ink.IsHitTestVisible = interactive;
        _toolbar.Visibility = interactive ? Visibility.Visible : Visibility.Collapsed;
        if (interactive) _toolbar.Refresh();
        ApplyTool();
        ApplyExStyle();
    }

    private void ApplyExStyle()
    {
        var handle = Handle;
        if (handle == IntPtr.Zero) return;
        // Nie in Alt+Tab – und für die Vollbilderkennung kein Vollbildfenster.
        long exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW;
        exStyle = _interactive
            ? exStyle & ~(WS_EX_NOACTIVATE | WS_EX_TRANSPARENT)
            : exStyle | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    /// <summary>Gewähltes Werkzeug, Farbe und Stärke übernehmen.</summary>
    public void ApplyTool()
    {
        if (!_interactive)
        {
            _ink.EditingMode = InkCanvasEditingMode.None;
            return;
        }
        var tool = DrawingSession.Tool;
        _ink.EditingMode = tool switch
        {
            DrawTool.Pen or DrawTool.Marker => InkCanvasEditingMode.Ink,
            DrawTool.Eraser => InkCanvasEditingMode.EraseByStroke,
            _ => InkCanvasEditingMode.None,
        };
        _ink.DefaultDrawingAttributes = Attributes(tool);
        _ink.UseCustomCursor = _ink.EditingMode == InkCanvasEditingMode.None;
        _ink.Cursor = Cursors.Cross;
    }

    private static DrawingAttributes Attributes(DrawTool tool)
    {
        double size = DrawingSession.Thickness;
        bool marker = tool == DrawTool.Marker;
        if (marker) size = Math.Max(12, size * 3.5);
        return new DrawingAttributes
        {
            Color = DrawingSession.Color,
            Width = size,
            Height = size,
            IsHighlighter = marker,
            IgnorePressure = true,
            // Freihand geglättet, Formen mit exakten Ecken.
            FitToCurve = tool == DrawTool.Pen,
            StylusTip = StylusTip.Ellipse,
        };
    }

    // ---- Formen: Linie, Pfeil, Rechteck, Ellipse ------------------------------------------

    private static bool IsShape(DrawTool tool) => tool is DrawTool.Line or DrawTool.Arrow or DrawTool.Rectangle or DrawTool.Ellipse;

    private void OnShapeDown(object sender, MouseButtonEventArgs e)
    {
        if (!_interactive || !IsShape(DrawingSession.Tool)) return;
        _shapeStart = e.GetPosition(_ink);
        _shape = new Stroke(new StylusPointCollection(ShapePoints(DrawingSession.Tool, _shapeStart, _shapeStart, false)),
            Attributes(DrawingSession.Tool));
        _recording = false;
        _ink.Strokes.Add(_shape);
        _recording = true;
        _ink.CaptureMouse();
        e.Handled = true;
    }

    private void OnShapeMove(object sender, MouseEventArgs e)
    {
        if (_shape is null) return;
        bool constrain = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        _shape.StylusPoints = new StylusPointCollection(ShapePoints(DrawingSession.Tool, _shapeStart, e.GetPosition(_ink), constrain));
        e.Handled = true;
    }

    private void OnShapeUp(object sender, MouseButtonEventArgs e)
    {
        if (_shape is null) return;
        var shape = _shape;
        _shape = null;
        _ink.ReleaseMouseCapture();
        e.Handled = true;
        // Nur geklickt, nicht gezogen: nichts zeichnen.
        if ((e.GetPosition(_ink) - _shapeStart).Length < 3)
        {
            _recording = false;
            _ink.Strokes.Remove(shape);
            _recording = true;
            return;
        }
        _history.Push(([shape], []));
        DrawingSession.NotifyChanged();
    }

    private void CancelShape()
    {
        if (_shape is null) return;
        _recording = false;
        _ink.Strokes.Remove(_shape);
        _recording = true;
        _shape = null;
        _ink.ReleaseMouseCapture();
    }

    /// <param name="constrain">Umschalt: Linie in 45°-Schritten, Quadrat bzw. Kreis.</param>
    private IEnumerable<StylusPoint> ShapePoints(DrawTool tool, Point a, Point b, bool constrain)
    {
        var d = b - a;
        if (constrain)
        {
            if (tool is DrawTool.Line or DrawTool.Arrow)
            {
                double angle = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 4)) * (Math.PI / 4);
                b = a + new Vector(Math.Cos(angle), Math.Sin(angle)) * d.Length;
            }
            else
            {
                double side = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
                b = a + new Vector(Math.Sign(d.X == 0 ? 1 : d.X) * side, Math.Sign(d.Y == 0 ? 1 : d.Y) * side);
            }
        }

        switch (tool)
        {
            case DrawTool.Arrow:
            {
                // Linie, dann die Spitze als ein Zug: Ende → Flügel → Ende → Flügel.
                var direction = b - a;
                double length = Math.Max(12, DrawingSession.Thickness * 4);
                if (direction.Length < 0.01) return [P(a), P(b)];
                direction.Normalize();
                var back = -direction * length;
                var wing1 = Rotate(back, 28);
                var wing2 = Rotate(back, -28);
                return [P(a), P(b), P(b + wing1), P(b), P(b + wing2)];
            }
            case DrawTool.Rectangle:
                return [P(a), P(new Point(b.X, a.Y)), P(b), P(new Point(a.X, b.Y)), P(a)];
            case DrawTool.Ellipse:
            {
                var center = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                double rx = Math.Abs(b.X - a.X) / 2, ry = Math.Abs(b.Y - a.Y) / 2;
                return Enumerable.Range(0, 73).Select(i =>
                {
                    double t = i * Math.PI * 2 / 72;
                    return P(new Point(center.X + rx * Math.Cos(t), center.Y + ry * Math.Sin(t)));
                }).ToList();
            }
            default:
                return [P(a), P(b)];
        }

        static StylusPoint P(Point p) => new(p.X, p.Y);

        static Vector Rotate(Vector v, double degrees)
        {
            double r = degrees * Math.PI / 180, cos = Math.Cos(r), sin = Math.Sin(r);
            return new Vector(v.X * cos - v.Y * sin, v.X * sin + v.Y * cos);
        }
    }

    // ---- Verlauf ---------------------------------------------------------------------------

    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (!_recording) return;
        _history.Push(([.. e.Added], [.. e.Removed]));
        DrawingSession.NotifyChanged();
    }

    public void Undo()
    {
        if (_history.Count == 0) return;
        var (added, removed) = _history.Pop();
        _recording = false;
        foreach (var stroke in added) _ink.Strokes.Remove(stroke);
        _ink.Strokes.Add(removed);
        _recording = true;
        DrawingSession.NotifyChanged();
    }

    public void Clear()
    {
        CancelShape();
        _recording = false;
        _ink.Strokes.Clear();
        _recording = true;
        _history.Clear();
    }

    // ---- Werkzeugleiste --------------------------------------------------------------------

    /// <summary>Ausrichtung gewechselt: Leiste neu aufbauen und an ihren Standardplatz setzen.</summary>
    public void RebuildToolbar()
    {
        _layer.Children.Remove(_toolbar);
        _toolbar = new DrawToolbar(this) { Visibility = _interactive ? Visibility.Visible : Visibility.Collapsed };
        _toolbar.SizeChanged += (_, _) => PlaceToolbar();
        _layer.Children.Add(_toolbar);
    }

    /// <summary>Gespeicherte Position (Anteil des Monitors) oder Standardplatz: senkrecht links mittig, waagerecht oben mittig.</summary>
    private void PlaceToolbar()
    {
        double width = _layer.ActualWidth, height = _layer.ActualHeight;
        double w = _toolbar.ActualWidth, h = _toolbar.ActualHeight;
        if (width <= 0 || height <= 0 || w <= 0) return;
        var position = DrawingSession.ToolbarPosition is { } p
            ? new Point(p.X * width, p.Y * height)
            : DrawingSession.ToolbarHorizontal
                ? new Point((width - w) / 2, height * 0.08)
                : new Point(16, (height - h) / 2);
        MoveToolbar(position);
    }

    /// <summary>Werkzeugleiste verschieben, immer ganz auf dem Monitor.</summary>
    internal void MoveToolbar(Point position, bool remember = false)
    {
        double width = _layer.ActualWidth, height = _layer.ActualHeight;
        double x = Math.Clamp(position.X, 0, Math.Max(0, width - _toolbar.ActualWidth));
        double y = Math.Clamp(position.Y, 0, Math.Max(0, height - _toolbar.ActualHeight));
        Canvas.SetLeft(_toolbar, x);
        Canvas.SetTop(_toolbar, y);
        if (remember && width > 0 && height > 0) DrawingSession.ToolbarPosition = new Point(x / width, y / height);
    }

    internal Point ToolbarOrigin => new(Canvas.GetLeft(_toolbar) is var x && double.IsFinite(x) ? x : 0,
        Canvas.GetTop(_toolbar) is var y && double.IsFinite(y) ? y : 0);

    // ---- Tastatur und Fokus ----------------------------------------------------------------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!_interactive) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // Alt+F4 würde das Fenster schließen – wie Esc behandeln.
        if (key == Key.Escape || (key == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))) DrawingSession.Stop();
        else if (key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Undo();
        else if (key == Key.Delete) DrawingSession.ClearAll();
        else return;
        e.Handled = true;
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // Alt+Tab, Windows-Taste o. Ä.: Zeichnen beenden, damit die Maus wieder normal funktioniert. Fokus bleibt dort.
        if (_interactive) DrawingSession.Stop(restoreFocus: false);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        DrawingSession.Forget(this);
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
