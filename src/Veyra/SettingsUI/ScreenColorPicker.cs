using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Veyra.Interop;

namespace Veyra.SettingsUI;

/// <summary>
/// Pipette: friert den Bildschirm als Standbild ein und legt es auf jedem Monitor als Vollbild-Ebene aus,
/// damit die gezeigten Pixel exakt denen entsprechen, die ausgelesen werden. Eine Lupe zeigt die Umgebung
/// des Mauszeigers. Klick oder Enter übernimmt die Farbe, Esc oder Rechtsklick bricht ab,
/// Pfeiltasten verschieben den Zeiger pixelgenau (mit Umschalt um 10 Pixel).
/// </summary>
public static class ScreenColorPicker
{
    private static Session? _active;

    /// <summary>Startet die Auswahl; <paramref name="done"/> erhält die Farbe oder null bei Abbruch.</summary>
    public static void Pick(Action<Color?> done)
    {
        if (_active is not null) return;
        var shot = CaptureVirtualScreen(out var origin);
        if (shot is null)
        {
            done(null);
            return;
        }
        var session = new Session(shot, origin, color =>
        {
            _active = null;
            done(color);
        });
        if (!session.IsFinished) _active = session;
    }

    private static BitmapSource? CaptureVirtualScreen(out POINT origin)
    {
        // Per-Monitor-DPI v2: der virtuelle Bildschirm wird in physischen Pixeln gemeldet.
        origin = new POINT
        {
            X = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
            Y = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
        };
        int width = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
        int height = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN);
        if (width <= 0 || height <= 0) return null;

        IntPtr screen = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memory = NativeMethods.CreateCompatibleDC(screen);
        IntPtr bitmap = NativeMethods.CreateCompatibleBitmap(screen, width, height);
        IntPtr previous = NativeMethods.SelectObject(memory, bitmap);
        try
        {
            if (!NativeMethods.BitBlt(memory, 0, 0, width, height, screen, origin.X, origin.Y,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
            {
                return null;
            }
            NativeMethods.SelectObject(memory, previous);
            previous = IntPtr.Zero;

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
            converted.Freeze();
            return converted;
        }
        finally
        {
            if (previous != IntPtr.Zero) NativeMethods.SelectObject(memory, previous);
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memory);
            NativeMethods.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    private sealed class Session
    {
        private const int LensPixels = 11; // ungerade → es gibt ein Mittelpixel
        private const double LensZoom = 11;
        private const double LensOffset = 24;

        private readonly BitmapSource _shot;
        private readonly POINT _origin;
        private readonly Action<Color?> _finish;
        private readonly List<Overlay> _overlays = [];
        private readonly byte[] _pixel = new byte[4];
        private bool _finished;

        public Session(BitmapSource shot, POINT origin, Action<Color?> finish)
        {
            _shot = shot;
            _origin = origin;
            _finish = finish;

            var monitors = new List<RECT>();
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref RECT rect, IntPtr _) =>
            {
                monitors.Add(rect);
                return true;
            }, IntPtr.Zero);

            foreach (var monitor in monitors)
            {
                var overlay = new Overlay(this, monitor);
                _overlays.Add(overlay);
                overlay.Window.Show();
            }

            NativeMethods.GetCursorPos(out var cursor);
            var start = _overlays.FirstOrDefault(o => o.Contains(cursor)) ?? _overlays.FirstOrDefault();
            if (start is null)
            {
                Finish(null);
                return;
            }
            start.Window.Activate();
            start.Window.Focus();
            Update();
        }

        public bool IsFinished => _finished;

        private void Update()
        {
            if (_finished) return;
            NativeMethods.GetCursorPos(out var cursor);
            var (x, y) = ToPixel(cursor);
            var color = Sample(x, y);

            // Ausschnitt um den Zeiger; am Bildrand verschoben, die Markierung wandert dann mit.
            int left = Math.Clamp(x - LensPixels / 2, 0, Math.Max(0, _shot.PixelWidth - LensPixels));
            int top = Math.Clamp(y - LensPixels / 2, 0, Math.Max(0, _shot.PixelHeight - LensPixels));
            var area = new Int32Rect(left, top, Math.Min(LensPixels, _shot.PixelWidth), Math.Min(LensPixels, _shot.PixelHeight));
            var zoomed = new CroppedBitmap(_shot, area);
            zoomed.Freeze();

            foreach (var overlay in _overlays)
            {
                if (overlay.Contains(cursor)) overlay.ShowLens(cursor, zoomed, x - left, y - top, color);
                else overlay.HideLens();
            }
        }

        private (int X, int Y) ToPixel(POINT cursor) =>
            (Math.Clamp(cursor.X - _origin.X, 0, _shot.PixelWidth - 1),
             Math.Clamp(cursor.Y - _origin.Y, 0, _shot.PixelHeight - 1));

        private Color Sample(int x, int y)
        {
            _shot.CopyPixels(new Int32Rect(x, y, 1, 1), _pixel, 4, 0);
            return Color.FromRgb(_pixel[2], _pixel[1], _pixel[0]);
        }

        private void Accept()
        {
            NativeMethods.GetCursorPos(out var cursor);
            var (x, y) = ToPixel(cursor);
            Finish(Sample(x, y));
        }

        private void Finish(Color? color)
        {
            if (_finished) return;
            _finished = true;
            foreach (var overlay in _overlays) overlay.Window.Close();
            _finish(color);
        }

        private void OnKeyDown(KeyEventArgs e)
        {
            int step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
            (int dx, int dy) = e.Key switch
            {
                Key.Left => (-step, 0),
                Key.Right => (step, 0),
                Key.Up => (0, -step),
                Key.Down => (0, step),
                _ => (0, 0),
            };

            if (dx != 0 || dy != 0)
            {
                NativeMethods.GetCursorPos(out var cursor);
                NativeMethods.SetCursorPos(cursor.X + dx, cursor.Y + dy);
                Update();
            }
            else if (e.Key is Key.Enter or Key.Space) Accept();
            else if (e.Key == Key.Escape) Finish(null);
            else return;
            e.Handled = true;
        }

        private void OnDeactivated(Window window)
        {
            // Wechsel zwischen den Ebenen der Monitore ist kein Abbruch – erst wenn keine mehr aktiv ist.
            window.Dispatcher.BeginInvoke(() =>
            {
                if (!_overlays.Any(o => o.Window.IsActive)) Finish(null);
            }, DispatcherPriority.Background);
        }

        /// <summary>Vollbild-Ebene eines Monitors mit dem eingefrorenen Bildausschnitt und der Lupe.</summary>
        private sealed class Overlay
        {
            private readonly RECT _monitor;
            private readonly Canvas _layer;
            private readonly Border _lens;
            private readonly Image _zoom;
            private readonly Rectangle _marker;
            private readonly Border _swatch;
            private readonly TextBlock _hex;

            public Overlay(Session session, RECT monitor)
            {
                _monitor = monitor;

                int left = Math.Clamp(monitor.Left - session._origin.X, 0, session._shot.PixelWidth - 1);
                int top = Math.Clamp(monitor.Top - session._origin.Y, 0, session._shot.PixelHeight - 1);
                var area = new Int32Rect(left, top,
                    Math.Min(monitor.Width, session._shot.PixelWidth - left),
                    Math.Min(monitor.Height, session._shot.PixelHeight - top));
                var frozen = new CroppedBitmap(session._shot, area);
                frozen.Freeze();

                var background = new Image { Source = frozen, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(background, BitmapScalingMode.NearestNeighbor);

                double zoomSize = LensPixels * LensZoom;
                _zoom = new Image { Width = zoomSize, Height = zoomSize, Stretch = Stretch.Fill };
                RenderOptions.SetBitmapScalingMode(_zoom, BitmapScalingMode.NearestNeighbor);
                _marker = new Rectangle { Width = LensZoom + 2, Height = LensZoom + 2, Stroke = Brushes.White, StrokeThickness = 1.5 };
                var zoomHost = new Canvas { Width = zoomSize, Height = zoomSize, ClipToBounds = true };
                zoomHost.Children.Add(_zoom);
                zoomHost.Children.Add(_marker);

                _swatch = new Border
                {
                    Width = 18, Height = 18, CornerRadius = new CornerRadius(4),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(1),
                };
                _hex = new TextBlock { FontFamily = new FontFamily("Consolas"), Foreground = Brushes.White, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var info = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                info.Children.Add(_swatch);
                info.Children.Add(_hex);

                var content = new StackPanel();
                content.Children.Add(zoomHost);
                content.Children.Add(info);
                _lens = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x2B, 0x2C, 0x33)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x45, 0xFF, 0xFF, 0xFF)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6),
                    Child = content,
                    Visibility = Visibility.Collapsed,
                };
                _layer = new Canvas { IsHitTestVisible = false };
                _layer.Children.Add(_lens);

                var root = new Grid();
                root.Children.Add(background);
                root.Children.Add(_layer);

                Window = new Window
                {
                    WindowStyle = WindowStyle.None,
                    ResizeMode = ResizeMode.NoResize,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Topmost = true,
                    Background = Brushes.Black,
                    Cursor = Cursors.Cross,
                    UseLayoutRounding = true,
                    Width = 1,
                    Height = 1,
                    Content = root,
                    Title = "Farbe vom Bildschirm aufnehmen",
                };
                // Zweimal setzen: der erste Aufruf kann den Monitor und damit die DPI wechseln,
                // worauf WPF die Größe anpasst; der zweite stellt die physische Größe sicher.
                Window.SourceInitialized += (_, _) => Place();
                Window.Loaded += (_, _) =>
                {
                    Place();
                    session.Update();
                };
                Window.MouseMove += (_, _) => session.Update();
                Window.MouseLeftButtonDown += (_, e) =>
                {
                    session.Accept();
                    e.Handled = true;
                };
                Window.MouseRightButtonDown += (_, e) =>
                {
                    session.Finish(null);
                    e.Handled = true;
                };
                Window.PreviewKeyDown += (_, e) => session.OnKeyDown(e);
                Window.Deactivated += (_, _) => session.OnDeactivated(Window);
            }

            public Window Window { get; }

            public bool Contains(POINT point) =>
                point.X >= _monitor.Left && point.X < _monitor.Right && point.Y >= _monitor.Top && point.Y < _monitor.Bottom;

            public void ShowLens(POINT cursor, BitmapSource zoomed, int markerX, int markerY, Color color)
            {
                _zoom.Source = zoomed;
                Canvas.SetLeft(_marker, markerX * LensZoom - 1);
                Canvas.SetTop(_marker, markerY * LensZoom - 1);
                // Weiße Markierung auf hellen Pixeln schlecht sichtbar → dort schwarz.
                _marker.Stroke = color.R * 0.299 + color.G * 0.587 + color.B * 0.114 > 160 ? Brushes.Black : Brushes.White;
                _swatch.Background = new SolidColorBrush(color);
                _hex.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

                var dpi = VisualTreeHelper.GetDpi(Window);
                double x = (cursor.X - _monitor.Left) / dpi.DpiScaleX;
                double y = (cursor.Y - _monitor.Top) / dpi.DpiScaleY;
                _lens.Visibility = Visibility.Visible;
                _lens.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var size = _lens.DesiredSize;

                // Lupe rechts unterhalb des Zeigers, am Rand auf die andere Seite klappen.
                double lensX = x + LensOffset + size.Width > _layer.ActualWidth ? x - LensOffset - size.Width : x + LensOffset;
                double lensY = y + LensOffset + size.Height > _layer.ActualHeight ? y - LensOffset - size.Height : y + LensOffset;
                Canvas.SetLeft(_lens, Math.Max(0, lensX));
                Canvas.SetTop(_lens, Math.Max(0, lensY));
            }

            public void HideLens() => _lens.Visibility = Visibility.Collapsed;

            private void Place()
            {
                var hwnd = new WindowInteropHelper(Window).Handle;
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, _monitor.Left, _monitor.Top,
                    _monitor.Width, _monitor.Height, NativeMethods.SWP_NOACTIVATE);
            }
        }
    }
}
