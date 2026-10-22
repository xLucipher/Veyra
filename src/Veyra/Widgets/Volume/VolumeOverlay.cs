using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Veyra.Monitors;
using static Veyra.Interop.NativeMethods;

namespace Veyra.Widgets.Volume;

/// <summary>
/// Kurze Anzeige oben in der Mitte des Bildschirms nach einem Tastenkürzel („Mikrofon stumm“, „Lautstärke 42 %“) –
/// wie die Lautstärkeanzeige von Windows, aber auch über randlosen Vollbildspielen. Erscheint auf dem Monitor des
/// aktiven Fensters, nimmt nie den Fokus und lässt Klicks durch. Eine gemeinsame Instanz für alle Widgets.
/// </summary>
internal sealed class VolumeOverlay : Window
{
    private static readonly Brush AlertBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xDA, 0x44, 0x53)));
    private static readonly Brush CardBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xF0, 0x1E, 0x1F, 0x26)));
    private static readonly Brush AlertCardBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xF0, 0x3A, 0x16, 0x1B)));
    private static readonly Brush CardBorderBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)));
    private static VolumeOverlay? _instance;

    private readonly TextBlock _glyph;
    private readonly TextBlock _text;
    private readonly Border _card;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(1300) };

    private VolumeOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Veyra – Lautstärke";

        _glyph = new TextBlock
        {
            FontFamily = (FontFamily)Application.Current.Resources["WidgetIconFont"],
            FontSize = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        _text = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
        };
        _card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 11, 22, 11),
            Margin = new Thickness(12), // Platz für den Schatten
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.45 },
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { _glyph, _text } },
        };
        Content = _card;
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, _) =>
            {
                if (!_hideTimer.IsEnabled) Hide();
            };
            BeginAnimation(OpacityProperty, fade);
        };
    }

    /// <param name="alert">Rot hervorheben (z. B. Mikrofon stumm).</param>
    public static void Flash(string glyph, string text, bool alert)
    {
        var overlay = _instance ??= new VolumeOverlay();
        overlay.Display(glyph, text, alert);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        // Nicht in Alt+Tab, nie aktivieren, Klicks gehen an das Fenster darunter (z. B. das Spiel).
        long exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE).ToInt64() | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
        SetWindowLongPtr(handle, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private void Display(string glyph, string text, bool alert)
    {
        _glyph.Text = glyph;
        _glyph.Foreground = alert ? AlertBrush : Brushes.White;
        _text.Text = text;
        _card.Background = alert ? AlertCardBrush : CardBrush;
        _card.BorderBrush = alert ? AlertBrush : CardBorderBrush;

        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        if (!IsVisible) Show();
        UpdateLayout();
        PlaceOnActiveMonitor();
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    /// <summary>Oben mittig auf dem Monitor des aktiven Fensters, immer über allem (auch über Topmost-Spielen).</summary>
    private void PlaceOnActiveMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var anchor = GetForegroundWindow();
        var monitor = MonitorService.Query(MonitorFromWindow(anchor != IntPtr.Zero ? anchor : handle, MONITOR_DEFAULTTONEAREST));
        if (monitor is null || !GetWindowRect(handle, out var rect)) return;
        var b = monitor.Bounds;
        int x = b.Left + (b.Width - rect.Width) / 2;
        int y = b.Top + (int)(b.Height * 0.06);
        SetWindowPos(handle, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
        // Anderer Monitor mit anderer Skalierung: WPF passt die Größe an – dann erneut zentrieren.
        if (GetWindowRect(handle, out var moved) && moved.Width != rect.Width)
            SetWindowPos(handle, HWND_TOPMOST, b.Left + (b.Width - moved.Width) / 2, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }
}
