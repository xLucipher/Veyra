using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Veyra.SettingsUI;

/// <summary>Seitenumschaltung: sichtbar, wenn der gewählte Index dem Parameter entspricht.</summary>
public sealed class IndexToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index && int.TryParse(parameter as string, out int page) && index == page
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
