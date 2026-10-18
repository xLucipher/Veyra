using System.Windows.Controls;
using System.Windows.Input;

namespace Veyra.Widgets.Volume;

public partial class VolumeView : UserControl
{
    public VolumeView()
    {
        InitializeComponent();
    }

    private VolumeWidget? Widget => DataContext as VolumeWidget;

    private void OnClick(object sender, MouseButtonEventArgs e) => Widget?.ToggleFlyout(this);

    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        // Behandelt = kein Kontextmenü der Leiste.
        if (Widget?.OnRightClick() == true) e.Handled = true;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        Widget?.OnWheel(e.Delta);
        e.Handled = true;
    }
}
