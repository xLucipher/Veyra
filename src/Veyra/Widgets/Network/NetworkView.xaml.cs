using System.Windows.Controls;
using System.Windows.Input;

namespace Veyra.Widgets.Network;

public partial class NetworkView : UserControl
{
    public NetworkView()
    {
        InitializeComponent();
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is NetworkWidget widget) widget.ToggleFlyout(this);
    }
}
