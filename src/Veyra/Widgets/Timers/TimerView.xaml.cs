using System.Windows.Controls;
using System.Windows.Input;

namespace Veyra.Widgets.Timers;

public partial class TimerView : UserControl
{
    public TimerView()
    {
        InitializeComponent();
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is TimerWidget widget) widget.OnClick(this);
    }
}
