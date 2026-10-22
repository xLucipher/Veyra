using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Widgets.Draw;

/// <summary>
/// Stift auf der Fläche: Klick startet bzw. beendet das Zeichnen auf dem Bildschirm (<see cref="DrawingSession"/>),
/// der Papierkorb daneben löscht alles Gezeichnete. Beides auch per Tastenkürzel, ebenso im Spiel.
/// Läuft nichts im Hintergrund – das Widget reagiert nur auf Klicks und Änderungen der Zeichnung.
/// </summary>
public sealed class DrawWidget : IBarWidget, IHotkeyTarget
{
    private readonly string _entryId;
    private readonly Action<string, Action<WidgetEntry>> _updateEntry;
    private readonly StackPanel _view;
    private readonly Border _pen;
    private readonly Border _clear;
    private readonly TextBlock _penGlyph;
    private readonly TextBlock _clearGlyph;
    private DrawSettings _draw = new();
    private Brush _activeBrush = Brushes.Transparent;

    public DrawWidget(string entryId, Action<string, Action<WidgetEntry>> updateEntry)
    {
        _entryId = entryId;
        _updateEntry = updateEntry;
        var iconFont = (FontFamily)Application.Current.Resources["WidgetIconFont"];
        var hoverArea = (Style)Application.Current.Resources["WidgetHoverArea"];
        _penGlyph = new TextBlock { Text = "", FontFamily = iconFont, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        _clearGlyph = new TextBlock { Text = "", FontFamily = iconFont, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        _pen = new Border { Style = hoverArea, Child = _penGlyph };
        _clear = new Border { Style = hoverArea, Child = _clearGlyph, ToolTip = "Alles Gezeichnete löschen" };
        _pen.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            DrawingSession.Toggle(_draw, SaveOrientation);
        };
        _clear.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            DrawingSession.ClearAll();
        };
        _view = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            Children = { _pen, _clear },
        };
        DrawingSession.Changed += Update;
    }

    public FrameworkElement View => _view;

    public string EntryId => _entryId;

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _draw = entry?.Draw ?? new DrawSettings();
        var foreground = entry?.IconColor is { } color
            ? ColorUtil.ToBrush(color, Colors.White)
            : ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        _penGlyph.Foreground = foreground;
        _clearGlyph.Foreground = foreground;
        _activeBrush = ColorUtil.ToBrush(entry?.IndicatorColor ?? WidgetEntry.DefaultIndicatorColor, Colors.DodgerBlue, 0.45);
        Update();
    }

    private void Update()
    {
        bool drawing = DrawingSession.IsDrawing;
        // Eigener Hintergrund nur beim Zeichnen; sonst gilt wieder der Hover-Effekt des Stils.
        if (drawing) _pen.Background = _activeBrush;
        else _pen.ClearValue(Border.BackgroundProperty);
        _pen.ToolTip = drawing
            ? "Zeichnen beenden (Esc) – das Gezeichnete bleibt stehen"
            : "Auf den Bildschirm zeichnen – auf dem Monitor unter der Maus";
        _clear.Visibility = _draw.ShowClearButton && DrawingSession.HasDrawings ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveOrientation(bool horizontal) => _updateEntry(_entryId, entry =>
    {
        entry.Draw ??= new DrawSettings();
        entry.Draw.ToolbarHorizontal = horizontal;
    });

    public void OnHotkey(string action)
    {
        switch (action)
        {
            case WidgetHotkeys.ToggleDraw: DrawingSession.Toggle(_draw, SaveOrientation); break;
            case WidgetHotkeys.ClearDrawing: DrawingSession.ClearAll(); break;
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

    public void Dispose() => DrawingSession.Changed -= Update;
}
