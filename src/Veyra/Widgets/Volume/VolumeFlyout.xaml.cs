using System.Windows;
using System.Windows.Input;

namespace Veyra.Widgets.Volume;

/// <summary>Kompakter Lautstärkeregler (DataContext: <see cref="VolumeWidget"/>).</summary>
public partial class VolumeFlyout : FlyoutWindow
{
    public VolumeFlyout()
    {
        InitializeComponent();
        // Mausrad ändert die Lautstärke – über einer App deren Lautstärke, sonst die des Geräts (wie über dem Widget).
        PreviewMouseWheel += (_, e) =>
        {
            if (DataContext is not VolumeWidget widget || IsOverScrollBar(e.OriginalSource as DependencyObject)) return; // Liste scrollen
            if (FindApp(e.OriginalSource as DependencyObject) is { } app)
            {
                app.Level += Math.Sign(e.Delta) * widget.WheelStep;
            }
            else
            {
                widget.OnWheel(e.Delta);
            }
            e.Handled = true;
        };
    }

    private static bool IsOverScrollBar(DependencyObject? element)
    {
        for (; element is System.Windows.Media.Visual; element = System.Windows.Media.VisualTreeHelper.GetParent(element))
            if (element is System.Windows.Controls.Primitives.ScrollBar) return true;
        return false;
    }

    private static Veyra.Audio.AudioApp? FindApp(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is FrameworkElement { DataContext: Veyra.Audio.AudioApp app }) return app;
            element = element is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        LevelSlider.Focus(); // Pfeiltasten regeln sofort
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.M && DataContext is VolumeWidget widget) widget.ToggleMuteCommand.Execute(null);
    }
}
