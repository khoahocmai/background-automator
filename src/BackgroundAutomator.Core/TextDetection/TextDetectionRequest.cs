using System.Drawing;

namespace BackgroundAutomator.Core.TextDetection;

/// <summary>
/// Parameters for a text detection query.
/// </summary>
public sealed record TextDetectionRequest
{
    public string ExpectedText { get; init; }
    public TextMatchMode MatchMode { get; init; }
    public TextDetectionScope Scope { get; init; }
    public Rectangle? Region { get; init; }

    /// <summary>
    /// Backward-compatible property indicating whether inspection is restricted to the visible viewport.
    /// </summary>
    public bool VisibleOnly => Scope == TextDetectionScope.VisibleViewportOnly;

    public TextDetectionRequest(
        string expectedText,
        TextMatchMode matchMode = TextMatchMode.Contains,
        TextDetectionScope scope = TextDetectionScope.VisibleViewportOnly,
        Rectangle? region = null)
    {
        ExpectedText = expectedText;
        MatchMode = matchMode;
        Scope = scope;
        Region = region;
    }

    /// <summary>
    /// Backward-compatible constructor accepting a boolean VisibleOnly parameter.
    /// </summary>
    public TextDetectionRequest(
        string expectedText,
        TextMatchMode matchMode,
        bool VisibleOnly,
        Rectangle? region = null)
        : this(expectedText, matchMode, VisibleOnly ? TextDetectionScope.VisibleViewportOnly : TextDetectionScope.DocumentBuffer, region)
    {
    }
}
