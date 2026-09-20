using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MTS2000.App.Converters;

/// <summary>Converts null/non-null to <see cref="Visibility.Collapsed"/>/<see cref="Visibility.Visible"/>.
/// Pass ConverterParameter="Invert" to swap which state is visible.</summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNull = value is null;
        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            isNull = !isNull;
        }

        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
