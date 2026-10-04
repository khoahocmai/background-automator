using System.Globalization;
using System.Windows.Data;
using BackgroundAutomator.Core.Approval;

namespace BackgroundAutomator.App.Converters;

public sealed class ForegroundPolicyDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ForegroundPolicy policy)
        {
            if (parameter is string param && param.Equals("tooltip", StringComparison.OrdinalIgnoreCase))
            {
                return policy switch
                {
                    ForegroundPolicy.AllowIdlePulse => "BackgroundAutomator briefly activates Windows Terminal when user is idle, then restores focus.",
                    ForegroundPolicy.StrictTerminalForegroundOnly => "BackgroundAutomator never activates Windows Terminal automatically. Pauses until you focus Terminal.",
                    _ => string.Empty
                };
            }

            return policy switch
            {
                ForegroundPolicy.AllowIdlePulse => "Allow Idle Pulse",
                ForegroundPolicy.StrictTerminalForegroundOnly => "Strict Terminal FG Only",
                _ => policy.ToString()
            };
        }

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
