namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Specific fail-closed reason explaining why command auto-approval was blocked or aborted.
/// </summary>
public enum ApprovalBlockReason
{
    /// <summary>
    /// Target process name or window class does not match the rule specification.
    /// </summary>
    TargetMismatch,

    /// <summary>
    /// Expected confirmation prompt string is not visible in the active viewport.
    /// </summary>
    PromptNotVisible,

    /// <summary>
    /// Expected approval option is missing or not currently selected.
    /// </summary>
    OptionNotSelected,

    /// <summary>
    /// Command text could not be reliably extracted from the visible prompt area.
    /// </summary>
    CommandNotFound,

    /// <summary>
    /// Extracted command is not present in the explicit allowlist rule.
    /// </summary>
    CommandNotAllowed,

    /// <summary>
    /// Multiple conflicting prompts or ambiguous command candidates detected.
    /// </summary>
    AmbiguousPrompt,

    /// <summary>
    /// Text detection backend or UI Automation query failed.
    /// </summary>
    DetectionFailed,

    /// <summary>
    /// Target window is minimized, hung, or unavailable for safe interaction.
    /// </summary>
    TargetNotInteractable,

    /// <summary>
    /// Request to bring the target window into the foreground failed or timed out.
    /// </summary>
    ForegroundActivationFailed,

    /// <summary>
    /// Foreground window changed unexpectedly immediately before input injection.
    /// </summary>
    ForegroundChanged,

    /// <summary>
    /// Prompt remained visible after Enter injection (acknowledgement timeout).
    /// </summary>
    ConfirmationNotAcknowledged
}
