using System.Windows.Controls;
using System.Windows.Input;

namespace Veyra.Widgets.Content;

public partial class NoteView : UserControl
{
    public NoteView()
    {
        InitializeComponent();
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is NoteWidget widget) widget.ToggleFlyout(this);
    }
}
