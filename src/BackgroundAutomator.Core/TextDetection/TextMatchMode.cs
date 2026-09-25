namespace BackgroundAutomator.Core.TextDetection;

/// <summary>
/// Specifies how target text should be compared against observed window text.
/// </summary>
public enum TextMatchMode
{
    /// <summary>
    /// Matches if the observed text contains the expected text (whitespace normalized, case-insensitive).
    /// </summary>
    Contains,

    /// <summary>
    /// Matches if the observed text exactly equals the expected text (whitespace normalized, case-insensitive).
    /// </summary>
    Exact
}
