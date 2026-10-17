using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Veyra.Configuration;

namespace Veyra.Bar;

public sealed class WidgetMovedEventArgs(string widgetId, double position) : EventArgs
{
    public string WidgetId { get; } = widgetId;

    /// <summary>Neue Position der Widget-Mitte als Anteil der Leistenbreite (0..1).</summary>
    public double Position { get; } = position;
}

/// <summary>
/// Layout der Leiste.
///
/// 1. Feste Widgets (Uhr) werden zuerst in ihren Bereich Links/Mitte/Rechts gesetzt. Ihr Platz plus
///    Mindestabstand ist gesperrt – kein freies Widget kann ihn je belegen oder das feste Widget verschieben.
/// 2. Freie Widgets werden in der Reihenfolge ihrer Wunschposition in die jeweils nächstgelegene freie Lücke
///    gesetzt, die groß genug ist. Dadurch können sie sich nie überlappen; wächst ein Widget (neues Gerät),
///    rückt es in die nächste passende Lücke. Passt ein Widget nirgends hin, wird es ausgeblendet.
/// 3. Drag &amp; Drop: Ein freies Widget wird ab einer kleinen Mindestbewegung gezogen. Es wird zuletzt platziert,
///    weicht also allen anderen aus und schiebt niemanden weg. Beim Loslassen wird die tatsächlich erreichte
///    Position gemeldet (<see cref="WidgetMoved"/>), ein Klick darauf (z. B. Gerätewechsel) wird dann unterdrückt.
/// </summary>
public sealed class BarLayoutPanel : Panel
{
    private const double FreeGap = 6;      // Mindestabstand zwischen freien Widgets und zum Leistenrand
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

    private readonly Dictionary<UIElement, Rect> _arranged = [];
    private UIElement? _pressed;
    private Point _pressPoint;
    private double _grabOffset;
    private UIElement? _dragging;
    private double _dragLeft;

    public BarLayoutPanel()
    {
        ClipToBounds = true;
        Background = System.Windows.Media.Brushes.Transparent;
    }

    public event EventHandler<WidgetMovedEventArgs>? WidgetMoved;

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

    protected override Size MeasureOverride(Size availableSize)
    {
        double height = 0, width = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
            width += child.DesiredSize.Width;
        }
        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _arranged.Clear();
        double width = finalSize.Width;
        var blocked = new List<(double Start, double End)>
        {
            (double.NegativeInfinity, FreeGap),
            (width - FreeGap, double.PositiveInfinity),
        };

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
            .Where(t => GetFixedSlot(t.child) is null && t.child != _dragging)
            .OrderBy(t => DesiredLeft(t.child, width))
            .ThenBy(t => t.index)
            .Select(t => t.child)
            .ToList();
        if (_dragging is not null) free.Add(_dragging);

        foreach (var child in free)
        {
            double w = child.DesiredSize.Width;
            double desired = child == _dragging ? _dragLeft : DesiredLeft(child, width);
            if (w <= 0)
            {
                Place(child, desired, 0, finalSize.Height);
                continue;
            }

            double? x = FindNearestGap(blocked, desired, w);
            if (x is null)
            {
                // Kein Platz mehr: außerhalb des sichtbaren Bereichs ablegen (Panel schneidet ab).
                child.Arrange(new Rect(-10000, 0, w, finalSize.Height));
                continue;
            }

            Place(child, x.Value, w, finalSize.Height);
            blocked.Add((x.Value - FreeGap, x.Value + w + FreeGap));
        }

        return finalSize;
    }

    private void Place(UIElement child, double x, double w, double height)
    {
        double h = Math.Min(child.DesiredSize.Height, height);
        var rect = new Rect(x, (height - h) / 2, w, h);
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

    // ---- Drag & Drop ----------------------------------------------------------------------

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        var point = e.GetPosition(this);
        _pressed = _arranged
            .Where(kv => GetFixedSlot(kv.Key) is null && kv.Value.Contains(point))
            .Select(kv => kv.Key)
            .FirstOrDefault();
        if (_pressed is null) return;
        _pressPoint = point;
        _grabOffset = point.X - _arranged[_pressed].X;
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
            if (Math.Abs(point.X - _pressPoint.X) < DragThreshold) return;
            // Ab hier ist es ein Ziehen: Maus übernehmen, damit der Button darunter keinen Klick auslöst.
            _dragging = _pressed;
            _dragging.Opacity = 0.7;
            CaptureMouse();
            Cursor = Cursors.SizeWE;
        }

        _dragLeft = point.X - _grabOffset;
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
        if (_arranged.TryGetValue(dropped, out var rect) && ActualWidth > 0)
        {
            double position = Math.Clamp((rect.X + rect.Width / 2) / ActualWidth, 0, 1);
            SetPosition(dropped, position);
            WidgetMoved?.Invoke(this, new WidgetMovedEventArgs(GetWidgetId(dropped), position));
        }
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_dragging is not null && !IsMouseCaptured)
        {
            // Abgebrochen (z. B. Fokuswechsel): Widget kehrt an seine gespeicherte Position zurück.
            EndDrag();
            InvalidateArrange();
        }
    }

    private void EndDrag()
    {
        var dragged = _dragging;
        if (dragged is not null)
        {
            // Wunschposition = erreichte Position, damit das Widget beim Freigeben nicht springt.
            if (_arranged.TryGetValue(dragged, out var rect) && ActualWidth > 0)
                SetPosition(dragged, Math.Clamp((rect.X + rect.Width / 2) / ActualWidth, 0, 1));
            dragged.Opacity = 1;
        }
        _dragging = null;
        Cursor = null;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
}
