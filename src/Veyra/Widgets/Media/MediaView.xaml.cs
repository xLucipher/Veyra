using System.Windows.Controls;
using System.Windows.Input;

namespace Veyra.Widgets.Media;

public partial class MediaView : UserControl
{
    public MediaView()
    {
        InitializeComponent();
    }

    private MediaWidget? Widget => DataContext as MediaWidget;

    private void OnPrevious(object sender, MouseButtonEventArgs e) => Widget?.Previous();

    private void OnPlayPause(object sender, MouseButtonEventArgs e) => Widget?.PlayPause();

    private void OnNext(object sender, MouseButtonEventArgs e) => Widget?.Next();

    /// <summary>Cover oder Titel: Auswahl aller Wiedergaben öffnen bzw. schließen.</summary>
    private void OnSourceClick(object sender, MouseButtonEventArgs e) => Widget?.ToggleFlyout(this);
}
