namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Operational mode for safe auto-confirm execution.
/// </summary>
public enum AutoConfirmExecutionMode
{
    /// <summary>
    /// Safe dry-run mode. Detects prompt, extracts command, evaluates rule, and logs decision without dispatching any keyboard input.
    /// Newly created actions default to ObserveOnly.
    /// </summary>
    ObserveOnly,

    /// <summary>
    /// Fully automated execution. Verifies target, activates foreground, revalidates everything, and dispatches Enter.
    /// </summary>
    Confirm
}
