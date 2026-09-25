namespace BackgroundAutomator.Core.TextDetection;

/// <summary>
/// Service contract for detecting expected text in target application windows.
/// </summary>
public interface ITextDetectionService
{
    /// <summary>
    /// Evaluates whether the target window satisfies the text detection request.
    /// </summary>
    /// <param name="targetHwnd">Native window handle of the target window.</param>
    /// <param name="request">Text detection parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="TextDetectionResult"/> indicating match status and observed text.</returns>
    Task<TextDetectionResult> DetectAsync(
        IntPtr targetHwnd,
        TextDetectionRequest request,
        CancellationToken ct = default);
}
