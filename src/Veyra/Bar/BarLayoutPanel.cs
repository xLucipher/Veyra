using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Veyra.Configuration;

namespace Veyra.Bar;

public sealed class WidgetMovedEventArgs(string widgetId, double position, Point? offset = null) : EventArgs
{
    public string WidgetId { get; } = widgetId;

    /// <summary>Neue Position der Widget-Mitte als Anteil der Flächenbreite (0..1).</summary>
    public double Position { get; } = position;

    /// <summary>Nur bei freier Platzierung: Abstand der linken Kante (X) und der Oberkante (Y) zum Flächenrand in px.</summary>
    public Point? Offset { get; } = offset;
}

/// <summary>Freie Platzierung: Widgets ohne festen Abstand wurden dort festgehalten, wo sie gerade stehen (px).</summary>
public sealed class FreePositionsPinnedEventArgs(IReadOnlyList<(string WidgetId, Point Offset)> widgets) : EventArgs
{
    public IReadOnlyList<(string WidgetId, Point Offset)> Widgets { get; } = widgets;
}

/// <summary>
/// Layout der Leiste.
///
/// 1. Feste Widgets (Uhr) werden zuerst in ihren Bereich Links/Mitte/Rechts gesetzt. Ihr Platz plus
///    Mindestabstand ist gesperrt – kein freies Widget kann ihn je belegen oder das feste Widget verschieben.
/// 2. Freie Widgets werden in der Reihenfolge ihrer Wunschposition in die jeweils nächstgelegene freie Lücke
///    gesetzt, die groß genug ist. Dadurch können sie sich nie überlappen; wächst ein Widget (neues Gerät),
///    rückt es in die nächste passende Lücke. Passt ein Widget nirgends hin, rückt es in die nächste Zeile, sofern
///    Zeilenumbruch erlaubt und die Fläche hoch genug ist (<see cref="AllowWrap"/>); sonst wird es ausgeblendet.
/// 3. Drag &amp; Drop: Ein freies Widget wird ab einer kleinen Mindestbewegung gezogen. Es wird zuletzt platziert,
///    weicht also allen anderen aus und schiebt niemanden weg. Beim Loslassen wird die tatsächlich erreichte
///    Position gemeldet (<see cref="WidgetMoved"/>), ein Klick darauf (z. B. Gerätewechsel) wird dann unterdrückt.
///
/// Freie Platzierung (<see cref="FreePlacement"/>): Jedes Widget – auch die Uhr – steht dort, wo es abgelegt wurde,
/// mit festem Abstand zum linken und oberen Rand (<see cref="FreeOffsetLeftProperty"/>/<see cref="FreeOffsetTopProperty"/>,
/// in px). Wird die Fläche größer oder kleiner, bleiben die Widgets also stehen. Sie lassen sich in alle Richtungen
/// ziehen und dürfen sich überlappen; das zuletzt gezogene liegt oben. Widgets ohne festen Abstand (neu, oder aus
/// älteren Konfigurationen mit Anteilen der Flächengröße) werden einmalig dort festgehalten, wo sie gerade stehen
/// (<see cref="FreePositionsPinned"/>).
/// </summary>
public sealed class BarLayoutPanel : Panel
{
    private const double FreeGap = 6;      // Mindestabstand zwischen freien Widgets und zum Flächenrand
    private const double DragThreshold = 6;

    public static readonly DependencyProperty WidgetIdProperty = DependencyProperty.RegisterAttached(
        "WidgetId", typeof(string), typeof(BarLayoutPanel), new PropertyMetadata(""));

    /// <summary>null = freies Widget, sonst Bereich des festen Widgets.</summary>
    public static readonly DependencyProperty FixedSlotProperty = DependencyProperty.RegisterAttached(
        "FixedSlot", typeof(WidgetSlot?), typeof(BarLayoutPanel),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty OuterMarginProperty = DependencyProperty.RegisterAttached(
        "OuterMargin", typeof(double), typeof(BarLayoutPanel),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty InnerSpacingProperty = DependencyProperty.RegisterAttached(
        "InnerSpacing", typeof(double), typeof(BarLayoutPanel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty PositionProperty = DependencyProperty.RegisterAttached(
        "Position", typeof(double), typeof(BarLayoutPanel),
        new FrameworkPropertyMetadata(0.5, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>
    /// Freie Platzierung (alte Konfigurationen): Widget-Mitte als Anteil der Breite (NaN = keine eigene Position).
    /// Wird beim ersten Anordnen in <see cref="FreeLeftProperty"/> umgerechnet.
    /// </summary>
    public static readonly DependencyProperty FreeXProperty = DependencyProperty.RegisterAttached(
        "FreeX", typeof(double), typeof(BarLayoutPanel),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>
    /// Freie Platzierung: linke Kante als Anteil der Breite (NaN = keine). Wird das Widget breiter (z. B. laufender
    /// Timer), wächst es nach rechts – sein Symbol bleibt stehen.
    /// </summary>
    public static readonly DependencyProperty FreeLeftProperty = DependencyProperty.RegisterAttached(
        "FreeLeft", typeof(double), typeof(BarLayoutPanel),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>Freie Platzierung: Widget-Mitte als Anteil der Höhe (NaN = keine eigene Position).</summary>
    public static readonly DependencyProperty FreeYProperty = DependencyProperty.RegisterAttached(
        "FreeY", typeof(double), typeof(BarLayoutPanel),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>Freie Platzierung: Abstand der linken Kante zum linken Flächenrand in px (NaN = keiner).</summary>
    public static readonly DependencyProperty FreeOffsetLeftProperty = DependencyProperty.RegisterAttached(
        "FreeOffsetLeft", typeof(double), typeof(BarLayoutPanel),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>Freie Platzierung: Abstand der Oberkante zum oberen Flächenrand in px (NaN = keiner).</summary>
    public static readonly DependencyProperty FreeOffsetTopProperty = DependencyProperty.RegisterAttached(
        "FreeOffsetTop", typeof(double), typeof(BarLayoutPanel),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    /// <summary>Einzeln gesperrtes Widget (Kontextmenü): lässt sich nicht ziehen, Klicks bleiben möglich.</summary>
    public static readonly DependencyProperty IsWidgetLockedProperty = DependencyProperty.RegisterAttached(
        "IsWidgetLocked", typeof(bool), typeof(BarLayoutPanel), new PropertyMetadata(false));

    public static readonly DependencyProperty FreePlacementProperty = DependencyProperty.Register(
        nameof(FreePlacement), typeof(bool), typeof(BarLayoutPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty AllowWrapProperty = DependencyProperty.Register(
        nameof(AllowWrap), typeof(bool), typeof(BarLayoutPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>
    /// Zeilenumbruch: Passt ein freies Widget in keine Lücke mehr, rückt es in die nächste Zeile – sofern die Fläche
    /// hoch genug für eine weitere Zeile ist. Sonst wird es wie bisher ausgeblendet.
    /// </summary>
    public bool AllowWrap
    {
        get => (bool)GetValue(AllowWrapProperty);
        set => SetValue(AllowWrapProperty, value);
    }

    private const double RowGap = 4; // Abstand zwischen zwei Zeilen

    /// <summary>"Position sperren": Widgets lassen sich nicht ziehen (in jedem Modus). Klicks auf Widgets bleiben möglich.</summary>
    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            _isLocked = value;
            if (value && _dragging is not null) EndDrag();
            if (value) _pressed = null;
        }
    }

    private bool _isLocked;
    private readonly Dictionary<UIElement, Rect> _arranged = [];
    private UIElement? _pressed;
    private Point _pressPoint;
    private Vector _grabOffset;
    private UIElement? _dragging;
    private Point _dragTopLeft;
    private int _topZ;

    public BarLayoutPanel()
    {
        ClipToBounds = true;
        Background = System.Windows.Media.Brushes.Transparent;
    }

    public event EventHandler<WidgetMovedEventArgs>? WidgetMoved;

    public bool FreePlacement
    {
        get => (bool)GetValue(FreePlacementProperty);
        set => SetValue(FreePlacementProperty, value);
    }

    public static string GetWidgetId(UIElement e) => (string)e.GetValue(WidgetIdProperty);
    public static void SetWidgetId(UIElement e, string value) => e.SetValue(WidgetIdProperty, value);
    public static WidgetSlot? GetFixedSlot(UIElement e) => (WidgetSlot?)e.GetValue(FixedSlotProperty);
    public static void SetFixedSlot(UIElement e, WidgetSlot? value) => e.SetValue(FixedSlotProperty, value);
    public static double GetOuterMargin(UIElement e) => (double)e.GetValue(OuterMarginProperty);
    public static void SetOuterMargin(UIElement e, double value) => e.SetValue(OuterMarginProperty, value);
    public static double GetInnerSpacing(UIElement e) => (double)e.GetValue(InnerSpacingProperty);
    public static void SetInnerSpacing(UIElement e, double value) => e.SetValue(InnerSpacingProperty, value);
    public static double GetPosition(UIElement e) => (double)e.GetValue(PositionProperty);
    public static void SetPosition(UIElement e, double value) => e.SetValue(PositionProperty, value);
    public static double GetFreeX(UIElement e) => (double)e.GetValue(FreeXProperty);
    public static void SetFreeX(UIElement e, double value) => e.SetValue(FreeXProperty, value);
    public static double GetFreeLeft(UIElement e) => (double)e.GetValue(FreeLeftProperty);
    public static void SetFreeLeft(UIElement e, double value) => e.SetValue(FreeLeftProperty, value);
    public static double GetFreeY(UIElement e) => (double)e.GetValue(FreeYProperty);
    public static void SetFreeY(UIElement e, double value) => e.SetValue(FreeYProperty, value);
    public static double GetFreeOffsetLeft(UIElement e) => (double)e.GetValue(FreeOffsetLeftProperty);
    public static void SetFreeOffsetLeft(UIElement e, double value) => e.SetValue(FreeOffsetLeftProperty, value);
    public static double GetFreeOffsetTop(UIElement e) => (double)e.GetValue(FreeOffsetTopProperty);
    public static void SetFreeOffsetTop(UIElement e, double value) => e.SetValue(FreeOffsetTopProperty, value);
    public static bool GetIsWidgetLocked(UIElement e) => (bool)e.GetValue(IsWidgetLockedProperty);
    public static void SetIsWidgetLocked(UIElement e, bool value) => e.SetValue(IsWidgetLockedProperty, value);

    private static bool HasOffset(UIElement e) => !double.IsNaN(GetFreeOffsetLeft(e)) && !double.IsNaN(GetFreeOffsetTop(e));

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0, width = 0;
        foreach (UIElement child in InternalChildren)
        {
            // Breite begrenzen: In schmalen dynamischen Leisten (z. B. 200 px) brechen Texte wie das Datum um, statt abgeschnitten zu werden.
            double maxWidth = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - 2 * FreeGap);
            child.Measure(new Size(maxWidth, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            width += child.DesiredSize.Width;
        }
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _arranged.Clear();
        ArrangeInSlots(finalSize);
        if (FreePlacement) ArrangeFree(finalSize);
        return finalSize;
    }

    /// <summary>Normale Anordnung: feste Bereiche, freie Widgets ohne Überlappung nebeneinander.</summary>
    private void ArrangeInSlots(Size finalSize)
    {
        double width = finalSize.Width;
        var blocked = new List<(double Start, double End)>
        {
            (double.NegativeInfinity, FreeGap),
            (width - FreeGap, double.PositiveInfinity),
        };
        var dragging = FreePlacement ? null : _dragging;

        // 1) Feste Widgets je Bereich als Gruppe (mehrere im selben Bereich stehen nebeneinander, nie übereinander).
        foreach (var slot in new[] { WidgetSlot.Left, WidgetSlot.Center, WidgetSlot.Right })
        {
            var group = InternalChildren.Cast<UIElement>().Where(c => GetFixedSlot(c) == slot).ToList();
            if (group.Count == 0) continue;

            double groupWidth = group.Sum(c => c.DesiredSize.Width) + (group.Count - 1) * FreeGap;
            double margin = GetOuterMargin(group[0]);
            double x = slot switch
            {
                WidgetSlot.Left => margin,
                WidgetSlot.Right => width - margin - groupWidth,
                _ => (width - groupWidth) / 2,
            };

            foreach (var child in group)
            {
                double w = child.DesiredSize.Width;
                Place(child, x, w, finalSize.Height);
                double spacing = GetInnerSpacing(child);
                blocked.Add((x - spacing, x + w + spacing));
                x += w + FreeGap;
            }
        }

        // 2) Freie Widgets nach Wunschposition; das gezogene zuletzt (weicht allen aus, verdrängt niemanden).
        var free = InternalChildren.Cast<UIElement>()
            .Select((child, index) => (child, index))
            .Where(t => GetFixedSlot(t.child) is null && t.child != dragging)
            .OrderBy(t => DesiredLeft(t.child, width))
            .ThenBy(t => t.index)
            .Select(t => t.child)
            .ToList();
        if (dragging is not null) free.Add(dragging);

        // Zeilen: Feste Widgets sperren ihren Platz in jeder Zeile (sie stehen mittig über die ganze Höhe).
        double rowHeight = free.Count == 0 ? 0 : free.Max(c => c.DesiredSize.Height);
        int capacity = AllowWrap && rowHeight > 0 ? Math.Max(1, (int)((finalSize.Height + RowGap) / (rowHeight + RowGap))) : 1;
        var rows = Enumerable.Range(0, capacity).Select(_ => new List<(double Start, double End)>(blocked)).ToList();
        var placed = new List<(UIElement Child, int Row, double X)>();

        foreach (var child in free)
        {
            double w = child.DesiredSize.Width;
            double desired = child == dragging ? _dragTopLeft.X : DesiredLeft(child, width);
            if (w <= 0)
            {
                Place(child, desired, 0, finalSize.Height);
                continue;
            }

            // Erste Zeile mit passender Lücke – die nächste Zeile erst, wenn in den vorherigen kein Platz mehr ist.
            int row = -1;
            double? x = null;
            for (int r = 0; r < capacity && x is null; r++)
            {
                x = FindNearestGap(rows[r], desired, w);
                row = r;
            }
            if (x is null)
            {
                // Kein Platz mehr: außerhalb des sichtbaren Bereichs ablegen (Panel schneidet ab).
                child.Arrange(new Rect(-10000, 0, w, finalSize.Height));
                _arranged.Remove(child);
                continue;
            }

            placed.Add((child, row, x.Value));
            rows[row].Add((x.Value - FreeGap, x.Value + w + FreeGap));
        }

        // Eine Zeile: wie bisher mittig über die ganze Höhe. Mehrere: Zeilen gleicher Höhe, als Block vertikal zentriert.
        int used = placed.Count == 0 ? 1 : placed.Max(p => p.Row) + 1;
        double top = (finalSize.Height - (used * rowHeight + (used - 1) * RowGap)) / 2;
        foreach (var (child, row, x) in placed)
        {
            if (used == 1) Place(child, x, child.DesiredSize.Width, finalSize.Height);
            else Place(child, x, child.DesiredSize.Width, rowHeight, top + row * (rowHeight + RowGap));
        }
    }

    /// <summary>Freie Platzierung: eigene Positionen überschreiben die normale Anordnung.</summary>
    private void ArrangeFree(Size finalSize)
    {
        bool unpinned = false;
        foreach (UIElement child in InternalChildren)
        {
            var size = new Size(child.DesiredSize.Width, Math.Min(child.DesiredSize.Height, finalSize.Height));
            Point topLeft;
            if (child == _dragging)
                topLeft = _dragTopLeft;
            else if (HasOffset(child))
                topLeft = new Point(GetFreeOffsetLeft(child), GetFreeOffsetTop(child)); // fester Abstand: bleibt beim Ändern der Größe stehen
            else
            {
                unpinned = true;
                if (!double.IsNaN(GetFreeY(child)) && (!double.IsNaN(GetFreeLeft(child)) || !double.IsNaN(GetFreeX(child))))
                {
                    // Ältere Konfiguration: Anteile der Flächengröße – bis zur Übernahme als fester Abstand.
                    double left = !double.IsNaN(GetFreeLeft(child))
                        ? GetFreeLeft(child) * finalSize.Width
                        : GetFreeX(child) * finalSize.Width - size.Width / 2;
                    topLeft = new Point(left, GetFreeY(child) * finalSize.Height - size.Height / 2);
                }
                else if (_arranged.ContainsKey(child))
                    continue; // steht an seinem Platz aus der normalen Anordnung
                else
                    topLeft = new Point((finalSize.Width - size.Width) / 2, (finalSize.Height - size.Height) / 2);
            }

            var rect = new Rect(ClampTopLeft(topLeft, size, finalSize), size);
            child.Arrange(rect);
            _arranged[child] = rect;
        }

        if (unpinned && !_pinScheduled)
        {
            // Erst wenn das Layout zur Ruhe gekommen ist (Fenster in endgültiger Größe), dann einmal gesammelt speichern.
            _pinScheduled = true;
            Dispatcher.BeginInvoke(PinUnplacedWidgets, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    private bool _pinScheduled;

    /// <summary>Hält Widgets ohne festen Abstand dort fest, wo sie gerade stehen, und meldet das gesammelt.</summary>
    private void PinUnplacedWidgets()
    {
        _pinScheduled = false;
        // Unsichtbar (z. B. bei Vollbild ausgeblendet) oder gerade beim Ziehen: beim nächsten Anordnen erneut versuchen.
        if (!FreePlacement || !IsVisible || ActualWidth <= 0 || ActualHeight <= 0 || _dragging is not null) return;

        var pinned = new List<(string, Point)>();
        foreach (UIElement child in InternalChildren)
        {
            if (HasOffset(child) || !_arranged.TryGetValue(child, out var rect) || rect.X < -1000) continue;
            var offset = new Point(Math.Round(rect.X, 1), Math.Round(rect.Y, 1));
            SetFreeOffsetLeft(child, offset.X);
            SetFreeOffsetTop(child, offset.Y);
            pinned.Add((GetWidgetId(child), offset));
        }
        if (pinned.Count > 0) FreePositionsPinned?.Invoke(this, new FreePositionsPinnedEventArgs(pinned));
    }

    public event EventHandler<FreePositionsPinnedEventArgs>? FreePositionsPinned;

    /// <summary>Hält das Widget vollständig innerhalb der Leiste (ist es größer, bündig links/oben).</summary>
    private static Point ClampTopLeft(Point p, Size size, Size bounds) => new(
        Math.Max(0, Math.Min(p.X, bounds.Width - size.Width)),
        Math.Max(0, Math.Min(p.Y, bounds.Height - size.Height)));

    /// <summary>Widget in einem waagerechten Band (ganze Höhe oder eine Zeile ab <paramref name="top"/>) vertikal zentriert.</summary>
    private void Place(UIElement child, double x, double w, double height, double top = 0)
    {
        double h = Math.Min(child.DesiredSize.Height, height);
        var rect = new Rect(x, top + (height - h) / 2, w, h);
        child.Arrange(rect);
        _arranged[child] = rect;
    }

    private static double DesiredLeft(UIElement child, double width) =>
        GetPosition(child) * width - child.DesiredSize.Width / 2;

    /// <summary>Linke Kante in der freien Lücke, die der Wunschposition am nächsten liegt (oder null).</summary>
    private static double? FindNearestGap(List<(double Start, double End)> blocked, double desired, double w)
    {
        var merged = new List<(double Start, double End)>();
        foreach (var interval in blocked.OrderBy(b => b.Start))
        {
            if (merged.Count > 0 && interval.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, interval.End));
            else
                merged.Add(interval);
        }

        double? best = null;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < merged.Count - 1; i++)
        {
            double gapStart = merged[i].End;
            double gapEnd = merged[i + 1].Start;
            if (gapEnd - gapStart < w) continue;
            double candidate = Math.Clamp(desired, gapStart, gapEnd - w);
            double distance = Math.Abs(candidate - desired);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>Lässt sich grundsätzlich verschieben (freies Widget oder freie Platzierung) – ohne Rücksicht auf Sperren.</summary>
    private bool IsMovable(UIElement child) => FreePlacement || GetFixedSlot(child) is null;

    private bool IsDraggable(UIElement child) => !IsLocked && !GetIsWidgetLocked(child) && IsMovable(child);

    /// <summary>Oberstes Widget an dieser Stelle (Panel-Koordinaten), das <paramref name="filter"/> erfüllt, oder null.</summary>
    private UIElement? WidgetAt(Point point, Func<UIElement, bool> filter) => _arranged
        .Where(kv => filter(kv.Key) && kv.Value.Contains(point))
        .Select(kv => kv.Key)
        .OrderByDescending(GetZIndex)
        .ThenByDescending(InternalChildren.IndexOf)
        .FirstOrDefault();

    private UIElement? DraggableAt(Point point) => WidgetAt(point, IsDraggable);

    /// <summary>Liegt an dieser Stelle (Panel-Koordinaten) ein Widget, das das Panel selbst verschiebt?</summary>
    public bool IsDraggableWidgetAt(Point point) => DraggableAt(point) is not null;

    /// <summary>Liegt an dieser Stelle ein einzeln gesperrtes Widget? Dann verschiebt ein Ziehen auch nicht die Fläche.</summary>
    public bool IsLockedWidgetAt(Point point) => WidgetAt(point, c => GetIsWidgetLocked(c) && IsMovable(c)) is not null;

    /// <summary>Verschiebbares Widget an dieser Stelle (auch wenn gesperrt) – für „Widget sperren“ im Kontextmenü.</summary>
    public string? MovableWidgetIdAt(Point point) => WidgetAt(point, IsMovable) is { } child ? GetWidgetId(child) : null;

    /// <summary>Strg gedrückt: Das Fenster wird verschoben, nicht das Widget.</summary>
    public static bool IsWindowMoveModifier => (Keyboard.Modifiers & ModifierKeys.Control) != 0;

    // ---- Drag & Drop ----------------------------------------------------------------------

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        _pressed = IsWindowMoveModifier ? null : DraggableAt(e.GetPosition(this));
        if (_pressed is null) return;
        _pressPoint = e.GetPosition(this);
        _grabOffset = _pressPoint - _arranged[_pressed].TopLeft;
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (_pressed is null) return;
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            _pressed = null;
            return;
        }

        var point = e.GetPosition(this);
        if (_dragging is null)
        {
            bool moved = FreePlacement
                ? (point - _pressPoint).Length >= DragThreshold
                : Math.Abs(point.X - _pressPoint.X) >= DragThreshold;
            if (!moved) return;
            // Ab hier ist es ein Ziehen: Maus übernehmen, damit der Button darunter keinen Klick auslöst.
            _dragging = _pressed;
            _dragging.Opacity = 0.7;
            if (FreePlacement) SetZIndex(_dragging, ++_topZ); // gezogenes Widget liegt oben
            CaptureMouse();
            Cursor = FreePlacement ? Cursors.SizeAll : Cursors.SizeWE;
        }

        _dragTopLeft = point - _grabOffset;
        InvalidateArrange();
        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        _pressed = null;
        if (_dragging is null) return;

        var dropped = _dragging;
        EndDrag();
        UpdateLayout();
        if (_arranged.TryGetValue(dropped, out var rect) && ActualWidth > 0 && ActualHeight > 0)
        {
            double position = Math.Clamp((rect.X + rect.Width / 2) / ActualWidth, 0, 1);
            Point? offset = FreePlacement ? new Point(GetFreeOffsetLeft(dropped), GetFreeOffsetTop(dropped)) : null;
            WidgetMoved?.Invoke(this, new WidgetMovedEventArgs(GetWidgetId(dropped), position, offset));
        }
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragging is not null && !IsMouseCaptured)
        {
            // Abgebrochen (z. B. Fokuswechsel): Widget kehrt an seine gespeicherte Position zurück.
            _dragging.Opacity = 1;
            _dragging = null;
            Cursor = null;
            InvalidateArrange();
        }
    }

    private void EndDrag()
    {
        var dragged = _dragging;
        if (dragged is not null)
        {
            // Wunschposition = erreichte Position, damit das Widget beim Freigeben nicht springt.
            if (_arranged.TryGetValue(dragged, out var rect) && ActualWidth > 0 && ActualHeight > 0)
            {
                if (FreePlacement)
                {
                    SetFreeOffsetLeft(dragged, Math.Round(rect.X, 1));
                    SetFreeOffsetTop(dragged, Math.Round(rect.Y, 1));
                }
                else
                {
                    SetPosition(dragged, Math.Clamp((rect.X + rect.Width / 2) / ActualWidth, 0, 1));
                }
            }
            dragged.Opacity = 1;
        }
        _dragging = null;
        Cursor = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
}
