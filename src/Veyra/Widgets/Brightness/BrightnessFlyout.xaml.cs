using System.Windows.Input;

namespace Veyra.Widgets.Brightness;

/// <summary>Regler je Bildschirm und Nachtmodus (DataContext: <see cref="BrightnessWidget"/>).</summary>
public partial class BrightnessFlyout : FlyoutWindow
{
    public BrightnessFlyout()
    {
        InitializeComponent();
        // Mausrad irgendwo im Popup (außer auf einem Regler) ändert alle Bildschirme wie auf der Fläche.
        PreviewMouseWheel += (_, e) =>
        {
            if (e.OriginalSource is System.Windows.DependencyObject source && FindSlider(source)) return;
            (DataContext as BrightnessWidget)?.OnWheel(e.Delta);
            e.Handled = true;
        };
    }

    private static bool FindSlider(System.Windows.DependencyObject? element)
    {
        for (; element is not null; element = System.Windows.Media.VisualTreeHelper.GetParent(element))
            if (element is System.Windows.Controls.Slider) return true;
        return false;
    }
}
