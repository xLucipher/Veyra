using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using CustomTopBar.Core;

namespace CustomTopBar.SettingsUI;

/// <summary>Farbvorschau im Einstellungsfenster: "#RRGGBB" → Pinsel (ungültig → transparent).</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ColorUtil.ToBrush(value as string, Colors.Transparent);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
