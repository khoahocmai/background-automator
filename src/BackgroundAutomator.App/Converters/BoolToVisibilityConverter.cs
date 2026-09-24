using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BackgroundAutomator.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (Invert || (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)))
        {
            b = !b;
        }

        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isVisible = value is Visibility v && v == Visibility.Visible;
        if (Invert || (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase)))
        {
            isVisible = !isVisible;
        }

        return isVisible;
    }
}
