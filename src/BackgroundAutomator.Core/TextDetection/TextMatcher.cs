using System.Text;

namespace BackgroundAutomator.Core.TextDetection;

/// <summary>
/// Utility for normalizing whitespace and comparing text against expected patterns.
/// </summary>
public static class TextMatcher
{
    /// <summary>
    /// Normalizes whitespace by replacing any contiguous sequence of whitespace characters
    /// (spaces, tabs, newlines \r, \n) with a single space, and trimming leading/trailing whitespace.
    /// </summary>
    public static string NormalizeWhitespace(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        var sb = new StringBuilder(input.Length);
        bool inWhitespace = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c))
            {
                if (!inWhitespace)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(' ');
                    }
                    inWhitespace = true;
                }
            }
            else
            {
                sb.Append(c);
                inWhitespace = false;
            }
        }

        return sb.ToString().Trim();
    }

    /// <summary>
    /// Checks whether the observed text satisfies the expected text according to the specified match mode.
    /// Comparisons are case-insensitive and whitespace-normalized.
    /// </summary>
    public static bool IsMatch(string? observed, string? expected, TextMatchMode matchMode)
    {
        string normObserved = NormalizeWhitespace(observed);
        string normExpected = NormalizeWhitespace(expected);

        if (string.IsNullOrEmpty(normExpected))
            return false;

        return matchMode switch
        {
            TextMatchMode.Exact => string.Equals(normObserved, normExpected, StringComparison.OrdinalIgnoreCase),
            TextMatchMode.Contains => normObserved.Contains(normExpected, StringComparison.OrdinalIgnoreCase),
            _ => normObserved.Contains(normExpected, StringComparison.OrdinalIgnoreCase)
        };
    }

    /// <summary>
    /// Returns a bounded preview string suitable for logging without dumping huge buffers.
    /// </summary>
    public static string TruncatePreview(string? text, int maxLength = 120)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        string norm = NormalizeWhitespace(text);
        if (norm.Length <= maxLength)
            return norm;

        return norm.Substring(0, maxLength) + "...";
    }
}
