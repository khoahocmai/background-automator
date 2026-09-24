using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BackgroundAutomator.App.Converters;

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNullOrEmpty = value switch
        {
            null => true,
            string s => string.IsNullOrWhiteSpace(s),
            _ => false
        };

        bool visible = Invert ? isNullOrEmpty : !isNullOrEmpty;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
