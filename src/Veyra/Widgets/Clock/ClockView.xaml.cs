using System.Windows.Controls;
using System.Windows.Input;

namespace Veyra.Widgets.Clock;

public partial class ClockView : UserControl
{
    public ClockView()
    {
        InitializeComponent();
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ClockWidget clock)
            CalendarFlyout.Toggle(this, clock.Culture);
    }
}
