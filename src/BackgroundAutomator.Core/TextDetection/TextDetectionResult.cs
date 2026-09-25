namespace BackgroundAutomator.Core.TextDetection;

/// <summary>
/// Result of evaluating a text detection request against a target window.
/// </summary>
/// <param name="Matched">Whether the expected text was found.</param>
/// <param name="ObservedText">The text or excerpt observed during the detection attempt.</param>
/// <param name="FailureReason">Optional explanation if detection could not be performed or text was not found.</param>
/// <param name="RawText">The un-normalized raw visible text captured from the target control.</param>
public sealed record TextDetectionResult(
    bool Matched,
    string? ObservedText = null,
    string? FailureReason = null,
    string? RawText = null)
{
    public static TextDetectionResult Success(string observedText, string? rawText = null) =>
        new(true, observedText, null, rawText ?? observedText);

    public static TextDetectionResult NotFound(string? observedText = null, string message = "Expected text not found.", string? rawText = null) =>
        new(false, observedText, message, rawText ?? observedText);

    public static TextDetectionResult Failed(string failureReason, string? rawText = null) =>
        new(false, null, failureReason, rawText);
}
