using System.Windows.Controls;
using System.Windows.Input;

namespace Veyra.Widgets.Timers;

/// <summary>Bedienung und Bearbeitung der Timer eines Widgets (DataContext: <see cref="TimerWidget"/>).</summary>
public partial class TimerFlyout : FlyoutWindow
{
    public TimerFlyout()
    {
        InitializeComponent();
        // Enter übernimmt Name bzw. Dauer sofort (sonst erst beim Verlassen des Feldes).
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || e.OriginalSource is not TextBox box) return;
            box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            Keyboard.ClearFocus();
            e.Handled = true;
        };
    }

    protected override void OnDeactivated(EventArgs e)
    {
        // Klick außerhalb schließt das Popup – eine angefangene Eingabe trotzdem übernehmen.
        if (FocusManager.GetFocusedElement(this) is TextBox box) box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        base.OnDeactivated(e);
    }
}
