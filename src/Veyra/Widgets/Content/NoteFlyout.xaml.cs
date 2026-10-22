using System.Windows;

namespace Veyra.Widgets.Content;

/// <summary>Bearbeiten einer Notiz; der Text wird beim Schließen übernommen (<see cref="NoteWidget.CommitEdit"/>).</summary>
public partial class NoteFlyout : FlyoutWindow
{
    private readonly NoteWidget _widget;

    public NoteFlyout(NoteWidget widget)
    {
        _widget = widget;
        DataContext = widget;
        InitializeComponent();
        IsDraggable = true;
        Loaded += (_, _) =>
        {
            Editor.Focus();
            Editor.CaretIndex = Editor.Text.Length;
        };
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _widget.CommitEdit();
    }
}
