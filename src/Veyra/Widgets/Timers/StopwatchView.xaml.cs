using System.Windows.Controls;
using System.Windows.Input;

namespace Veyra.Widgets.Timers;

public partial class StopwatchView : UserControl
{
    public StopwatchView()
    {
        InitializeComponent();
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is StopwatchWidget widget) widget.ToggleFlyout(this);
    }
}
