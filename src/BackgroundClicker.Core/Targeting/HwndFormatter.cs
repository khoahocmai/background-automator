namespace BackgroundClicker.Core.Targeting;

/// <summary>
/// Consistent x64-safe HWND formatting helper.
/// Formats 64-bit window handles without 32-bit truncation (e.g. 0x00000000001203AA).
/// </summary>
public static class HwndFormatter
{
    /// <summary>
    /// Formats an HWND as a full 16-hex-digit string (e.g. 0x00000000001203AA).
    /// </summary>
    public static string Format(IntPtr hwnd)
    {
        ulong value = unchecked((ulong)hwnd.ToInt64());
        return $"0x{value:X16}";
    }

    /// <summary>
    /// Formats an HWND as an 8-to-16 hex string for shorter display.
    /// </summary>
    public static string FormatShort(IntPtr hwnd)
    {
        ulong value = unchecked((ulong)hwnd.ToInt64());
        return $"0x{value:X8}";
    }

    /// <summary>
    /// Safely parses a hex or decimal string into an IntPtr without truncation.
    /// </summary>
    public static bool TryParse(string? input, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string clean = input.Trim();
        if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(2);

        if (ulong.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out ulong result))
        {
            hwnd = new IntPtr(unchecked((long)result));
            return true;
        }

        if (long.TryParse(input.Trim(), out long decimalResult))
        {
            hwnd = new IntPtr(decimalResult);
            return true;
        }

        return false;
    }
}
