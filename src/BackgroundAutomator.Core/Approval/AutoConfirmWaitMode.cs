namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Specifies the polling duration behavior for SafeAutoConfirmAction.
/// </summary>
public enum AutoConfirmWaitMode
{
    /// <summary>
    /// Polls until a matching prompt is approved or the configured timeout duration expires.
    /// </summary>
    FixedTimeout,

    /// <summary>
    /// Polls indefinitely until a matching prompt is approved or the action is cancelled / interrupted.
    /// Having no prompt does not produce a timeout.
    /// </summary>
    Indefinite
}
