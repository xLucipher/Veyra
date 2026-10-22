using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Veyra.Configuration;
using Veyra.Core;

namespace Veyra.Widgets.Launcher;

/// <summary>
/// Schnellstart: ein Symbol je Eintrag (Programm, Datei, Ordner, Webadresse), Klick öffnet ihn. Dateien, Ordner und
/// Links lassen sich direkt auf das Widget ziehen und werden angehängt. Es läuft nichts im Hintergrund.
/// </summary>
public sealed class LauncherWidget : IBarWidget
{
    private readonly string _entryId;
    private readonly Action<string, Action<WidgetEntry>> _updateEntry;
    private readonly StackPanel _view;
    private LauncherSettings _launcher = new();
    private Brush _foreground = Brushes.White;
    private Brush _iconBrush = Brushes.White;
    private FontFamily _fontFamily = new("Segoe UI");

    public LauncherWidget(string entryId, Action<string, Action<WidgetEntry>> updateEntry)
    {
        _entryId = entryId;
        _updateEntry = updateEntry;
        _view = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            AllowDrop = true,
        };
        _view.DragOver += OnDragOver;
        _view.Drop += OnDrop;
    }

    public FrameworkElement View => _view;

    public void ApplySettings(AppSettings settings, BarSettings bar)
    {
        var entry = bar.Widgets.FirstOrDefault(w => w.Id == _entryId);
        _launcher = entry?.Launcher ?? new LauncherSettings();
        var barForeground = ColorUtil.ToBrush(bar.Appearance.ForegroundColor, Colors.White);
        _foreground = entry?.LabelColor is { } label ? ColorUtil.ToBrush(label, Colors.White) : barForeground;
        _iconBrush = entry?.IconColor is { } icon ? ColorUtil.ToBrush(icon, Colors.White) : barForeground;
        _fontFamily = new FontFamily(bar.Appearance.FontFamily);
        Build();
    }

    private void Build()
    {
        _view.Children.Clear();
        double size = _launcher.IconSize;
        if (_launcher.Items.Count == 0)
        {
            var empty = Button(Glyph("", size * 0.8), null);
            empty.ToolTip = "Schnellstart – Programme, Dateien oder Ordner hierher ziehen\noder in den Einstellungen hinzufügen";
            empty.Opacity = 0.6;
            _view.Children.Add(empty);
            return;
        }

        foreach (var item in _launcher.Items)
        {
            string name = item.Name.Length > 0 ? item.Name : LauncherIcons.DefaultName(item.Path);
            FrameworkElement icon = LauncherIcons.Get(item.Path, size) is { } image
                ? new Image { Source = image, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center }
                : Glyph(LauncherIcons.IsWebAddress(item.Path) ? "" : "", size * 0.85);
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

            var content = new StackPanel { Orientation = Orientation.Horizontal, Children = { icon } };
            if (_launcher.ShowNames)
            {
                content.Children.Add(new TextBlock
                {
                    Text = name,
                    Foreground = _foreground,
                    FontFamily = _fontFamily,
                    FontSize = 13,
                    MaxWidth = 140,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                });
            }
            var button = Button(content, item);
            button.ToolTip = item.Arguments.Length > 0 ? $"{name}\n{item.Path} {item.Arguments}" : $"{name}\n{item.Path}";
            _view.Children.Add(button);
        }
    }

    private TextBlock Glyph(string glyph, double size)
    {
        var text = new TextBlock { Text = glyph, FontSize = size, Foreground = _iconBrush, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.FontFamilyProperty, "WidgetIconFont");
        return text;
    }

    private Border Button(UIElement content, LauncherItem? item)
    {
        // Schmaler als der gemeinsame Hover-Stil, damit mehrere Symbole dicht nebeneinander stehen.
        var border = new Border { Child = content, Padding = new Thickness(5, 4, 5, 4) };
        border.SetResourceReference(FrameworkElement.StyleProperty, "WidgetHoverArea");
        if (item is not null) border.MouseLeftButtonUp += (_, _) => Launch(item);
        return border;
    }

    private static void Launch(LauncherItem item)
    {
        string path = Environment.ExpandEnvironmentVariables(item.Path);
        if (path.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) path = "https://" + path;
        try
        {
            var info = new ProcessStartInfo(path) { UseShellExecute = true };
            if (item.Arguments.Length > 0) info.Arguments = item.Arguments;
            if (File.Exists(path) && Path.GetDirectoryName(path) is { Length: > 0 } folder) info.WorkingDirectory = folder;
            Process.Start(info)?.Dispose();
            Log.Info("Launcher", $"Gestartet: {item.Path}");
        }
        catch (Exception ex)
        {
            Log.Warning("Launcher", $"'{item.Path}' konnte nicht geöffnet werden: {ex.Message}");
            Notifier.Show("Schnellstart", $"„{LauncherIcons.DefaultName(item.Path)}“ konnte nicht geöffnet werden: {ex.Message}", silent: false);
        }
    }

    // ---- Dateien, Ordner und Links auf das Widget ziehen -------------------------------------

    private static List<string> DroppedPaths(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            return files.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text &&
            LauncherIcons.IsWebAddress(text.Trim()) && !text.Trim().Contains(' '))
            return [text.Trim()];
        return [];
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedPaths(e.Data).Count > 0 ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var paths = DroppedPaths(e.Data);
        e.Handled = true;
        if (paths.Count == 0) return;
        _updateEntry(_entryId, entry =>
        {
            entry.Launcher ??= new LauncherSettings();
            foreach (var path in paths.Where(p => entry.Launcher.Items.All(i => !string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))))
                entry.Launcher.Items.Add(new LauncherItem { Path = path });
        });
        Log.Info("Launcher", $"{paths.Count} Eintrag/Einträge per Drag & Drop hinzugefügt");
    }

    public void Start()
    {
    }

    public void Stop()
    {
    }

    public void Refresh()
    {
    }

    public void Dispose()
    {
        _view.DragOver -= OnDragOver;
        _view.Drop -= OnDrop;
    }
}
