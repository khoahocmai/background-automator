namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Runtime snapshot representing the live confirmation prompt state in the target window.
/// Note: TargetHwnd is runtime-only and never persisted to disk.
/// </summary>
public sealed record CommandPromptSnapshot(
    IntPtr TargetHwnd,
    string RawVisibleText,
    string? CommandText,
    string? PromptText,
    string? SelectedOptionText,
    bool IsApprovalPromptVisible,
    bool IsYesOptionSelected,
    bool IsAmbiguous = false,
    string? AmbiguityReason = null,
    PermissionPromptEnvelope? Envelope = null)
{
    public static CommandPromptSnapshot FromExtraction(
        IntPtr targetHwnd,
        string rawVisibleText,
        CommandExtractionResult extraction)
    {
        return new CommandPromptSnapshot(
            targetHwnd,
            rawVisibleText,
            extraction.CommandText,
            extraction.PromptText,
            extraction.SelectedOptionText,
            extraction.IsApprovalPromptVisible,
            extraction.IsYesOptionSelected,
            extraction.IsAmbiguous,
            extraction.AmbiguityReason ?? extraction.FailureReason,
            extraction.Envelope);
    }
}
