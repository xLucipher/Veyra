using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Veyra.Interop;
using Veyra.Monitors;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Profiles;

/// <summary>
/// Profilauswahl als Overlay über dem ganzen Monitor (unter dem Mauszeiger) – aufgerufen per Tastenkürzel, ähnlich
/// einem Spiel-Overlay. Pfeiltasten/Tab wählen, Enter oder Klick wechselt, 1–9 wechselt direkt, Esc oder ein Klick
/// daneben schließt. Danach bekommt das vorher aktive Fenster (z. B. das Spiel) den Fokus zurück.
/// </summary>
public partial class ProfileOverlayWindow : Window
{
    private readonly ProfileOverlayViewModel _viewModel;
    private readonly Action<string> _switchTo;
    private readonly Action _manage;
    private readonly IntPtr _previousForeground;
    private bool _restoreFocus = true;
    private bool _closing;

    public ProfileOverlayWindow(ProfileOverlayViewModel viewModel, Action<string> switchTo, Action manage)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _switchTo = switchTo;
        _manage = manage;
        _previousForeground = GetForegroundWindow();
        DataContext = viewModel;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        // Nicht in Alt+Tab; über den ganzen Monitor, auf dem gerade die Maus ist (physische Pixel).
        long exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW;
        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(exStyle));
        GetCursorPos(out var cursor);
        var point = new RECT(cursor.X, cursor.Y, cursor.X + 1, cursor.Y + 1);
        if (MonitorService.Query(MonitorFromRect(ref point, MONITOR_DEFAULTTONEAREST)) is { } monitor)
        {
            var b = monitor.Bounds;
            SetWindowPos(handle, HWND_TOPMOST, b.Left, b.Top, b.Width, b.Height, SWP_NOACTIVATE);
        }
    }

    /// <summary>Anzeigen und den Fokus holen, damit die Tastatur sofort funktioniert.</summary>
    public void ShowAndActivate()
    {
        Show();
        Activate();
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
        Focus();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Left or Key.Up:
                _viewModel.Move(-1);
                break;
            case Key.Right or Key.Down:
                _viewModel.Move(1);
                break;
            case Key.Tab:
                _viewModel.Move(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
                break;
            case Key.Home:
                _viewModel.Select(0);
                break;
            case Key.End:
                _viewModel.Select(_viewModel.Cards.Count - 1);
                break;
            case Key.Enter or Key.Space:
                Choose(_viewModel.Selected);
                break;
            case >= Key.D1 and <= Key.D9:
                ChooseNumber(key - Key.D1);
                break;
            case >= Key.NumPad1 and <= Key.NumPad9:
                ChooseNumber(key - Key.NumPad1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void ChooseNumber(int index)
    {
        if (index < _viewModel.Cards.Count) Choose(_viewModel.Cards[index]);
    }

    private void Choose(ProfileCard card)
    {
        if (_closing) return;
        Close();
        if (!card.IsActive) _switchTo(card.Id);
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProfileCard card }) Choose(card);
        e.Handled = true;
    }

    private void OnCardHover(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProfileCard card })
            _viewModel.Select(_viewModel.Cards.ToList().IndexOf(card));
    }

    private void OnBackdropClick(object sender, MouseButtonEventArgs e)
    {
        // Nur der freie Hintergrund schließt – nicht Klicks auf Karten oder Buttons.
        if (ReferenceEquals(e.OriginalSource, Root)) Close();
    }

    private void OnManageClick(object sender, RoutedEventArgs e)
    {
        _restoreFocus = false; // die Einstellungen bekommen den Fokus
        Close();
        _manage();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // Beim eigenen Schließen meldet Windows ebenfalls eine Deaktivierung – dann nichts ändern.
        if (_closing) return;
        // Alt+Tab oder Klick in ein anderes Fenster: Overlay verschwindet, Fokus bleibt dort.
        _restoreFocus = false;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        _closing = !e.Cancel;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_restoreFocus && _previousForeground != IntPtr.Zero && IsWindow(_previousForeground))
            SetForegroundWindow(_previousForeground);
    }
}
