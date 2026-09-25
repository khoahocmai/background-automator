using System.Drawing;

namespace BackgroundAutomator.Core.TextDetection;

/// <summary>
/// Parameters for a text detection query.
/// </summary>
/// <param name="ExpectedText">The text string expected to appear in the target window.</param>
/// <param name="MatchMode">Match comparison mode (Contains or Exact).</param>
/// <param name="VisibleOnly">If true, prioritizes currently visible viewport text (e.g. terminal visible ranges) to prevent stale history matches.</param>
/// <param name="Region">Optional sub-region bounds if a specific bounding box is targeted.</param>
public sealed record TextDetectionRequest(
    string ExpectedText,
    TextMatchMode MatchMode = TextMatchMode.Contains,
    bool VisibleOnly = true,
    Rectangle? Region = null);
