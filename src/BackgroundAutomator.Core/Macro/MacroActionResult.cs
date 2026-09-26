namespace BackgroundAutomator.Core.Macro;

/// <summary>
/// Detailed result returned from the execution of an <see cref="IMacroAction"/>.
/// </summary>
public sealed record MacroActionResult(
    MacroActionStatus Status,
    string? Message = null,
    string? BlockerReason = null)
{
    public bool IsSuccess => Status == MacroActionStatus.Success;

    public static MacroActionResult Success(string? message = null) =>
        new(MacroActionStatus.Success, message);

    public static MacroActionResult Cancelled(string message = "Macro action was cancelled.") =>
        new(MacroActionStatus.Cancelled, message);

    public static MacroActionResult TargetUnavailable(string message = "Target window is invalid or unavailable.") =>
        new(MacroActionStatus.TargetUnavailable, message);

    public static MacroActionResult Timeout(
        string message = "Operation timed out before the condition was met.",
        string? blockerReason = null) =>
        new(MacroActionStatus.Timeout, message, blockerReason);

    public static MacroActionResult CaptureFailed(string message = "Failed to capture window client area.") =>
        new(MacroActionStatus.CaptureFailed, message);

    public static MacroActionResult ClickFailed(string message = "Failed to dispatch background click.") =>
        new(MacroActionStatus.ClickFailed, message);

    public static MacroActionResult KeyPressFailed(string message = "Failed to dispatch background key press.") =>
        new(MacroActionStatus.KeyPressFailed, message);

    public static MacroActionResult TextDetectionFailed(string message = "Failed to detect text in target window.") =>
        new(MacroActionStatus.TextDetectionFailed, message);

    public static MacroActionResult InvalidConfiguration(string message = "Invalid action configuration parameters.") =>
        new(MacroActionStatus.InvalidConfiguration, message);

    public static MacroActionResult ApprovalBlocked(string message = "Command approval was blocked by rule.") =>
        new(MacroActionStatus.ApprovalBlocked, message);

    public static MacroActionResult ApprovalFailed(string message = "Command approval failed.") =>
        new(MacroActionStatus.ApprovalFailed, message);

    public static implicit operator MacroActionResult(MacroActionStatus status) => new(status);
}
