namespace BackgroundAutomator.Core.TextDetection;

/// <summary>
/// Result of evaluating a text detection request against a target window.
/// </summary>
/// <param name="Matched">Whether the expected text was found.</param>
/// <param name="ObservedText">The text or excerpt observed during the detection attempt.</param>
/// <param name="FailureReason">Optional explanation if detection could not be performed or text was not found.</param>
public sealed record TextDetectionResult(
    bool Matched,
    string? ObservedText = null,
    string? FailureReason = null)
{
    public static TextDetectionResult Success(string observedText) =>
        new(true, observedText);

    public static TextDetectionResult NotFound(string? observedText = null, string message = "Expected text not found.") =>
        new(false, observedText, message);

    public static TextDetectionResult Failed(string failureReason) =>
        new(false, null, failureReason);
}
