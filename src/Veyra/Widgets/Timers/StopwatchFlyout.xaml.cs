using System.Windows.Input;

namespace Veyra.Widgets.Timers;

/// <summary>Bedienung der Stoppuhr (DataContext: <see cref="StopwatchWidget"/>). Leertaste startet/pausiert, R nimmt eine Runde, Entf setzt zurück.</summary>
public partial class StopwatchFlyout : FlyoutWindow
{
    public StopwatchFlyout()
    {
        InitializeComponent();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (DataContext is not StopwatchWidget widget) return;
        var command = e.Key switch
        {
            Key.Space => widget.StartPauseCommand,
            Key.R => widget.LapCommand,
            Key.Delete or Key.Back => widget.ResetCommand,
            _ => null,
        };
        if (command is null) return;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true; // Leertaste soll nicht zusätzlich den fokussierten Button auslösen
    }
}
