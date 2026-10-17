using System.Windows.Media;

namespace CustomTopBar.Core;

public static class ColorUtil
{
    /// <summary>Akzeptiert #RGB, #RRGGBB, #AARRGGBB sowie WPF-Farbnamen.</summary>
    public static bool TryParse(string? value, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            if (ColorConverter.ConvertFromString(value.Trim()) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch (FormatException)
        {
        }
        catch (NotSupportedException)
        {
        }
        return false;
    }

    public static bool IsValid(string? value) => TryParse(value, out _);

    public static SolidColorBrush ToBrush(string? value, Color fallback, double opacity = 1.0)
    {
        var brush = new SolidColorBrush(TryParse(value, out var c) ? c : fallback) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }
}
