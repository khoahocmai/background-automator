namespace BackgroundAutomator.Core.TextDetection;

/// <summary>
/// Defines the inspection scope for text detection in target windows.
/// </summary>
public enum TextDetectionScope
{
    /// <summary>
    /// Restricts text detection strictly to currently visible viewport ranges (e.g. TextPattern.GetVisibleRanges()).
    /// Required for command approval automation to prevent matching scrolled-away history prompts.
    /// Fails closed if visible viewport ranges cannot be extracted.
    /// </summary>
    VisibleViewportOnly,

    /// <summary>
    /// Inspects the entire document/history buffer (e.g. TextPattern.DocumentRange).
    /// Suitable for non-security-sensitive searches or diagnostics.
    /// </summary>
    DocumentBuffer
}
