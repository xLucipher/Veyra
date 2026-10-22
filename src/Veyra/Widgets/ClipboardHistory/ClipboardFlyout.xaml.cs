using System.Windows;
using System.Windows.Input;

namespace Veyra.Widgets.ClipboardHistory;

/// <summary>Verlauf der Zwischenablage (DataContext: <see cref="ClipboardWidget"/>). Bedienbar komplett per Tastatur.</summary>
public partial class ClipboardFlyout : FlyoutWindow
{
    public ClipboardFlyout()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private ClipboardWidget? Widget => DataContext as ClipboardWidget;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Widget?.MoveSelection(1);
                break;
            case Key.Up:
                Widget?.MoveSelection(-1);
                break;
            case Key.Enter:
                Widget?.SelectCurrent();
                break;
            case Key.Delete when SearchBox.Text.Length == 0 || SearchBox.CaretIndex == SearchBox.Text.Length:
                Widget?.RemoveCurrent();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void OnRowClick(object sender, MouseButtonEventArgs e)
    {
        // Klicks auf Anheften/Entfernen behandeln die Buttons selbst.
        if (e.Handled || sender is not FrameworkElement { DataContext: ClipboardRow row }) return;
        row.SelectCommand.Execute(null);
        e.Handled = true;
    }

    /// <summary>Gewählte Zeile in den sichtbaren Bereich holen.</summary>
    internal void ScrollIntoView(int index)
    {
        if (List.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
            container.BringIntoView();
    }
}
