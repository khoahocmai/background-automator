namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Result of evaluating a CommandApprovalRule against the live target state.
/// </summary>
public sealed record ApprovalDecision(
    bool IsAllowed,
    ApprovalBlockReason? BlockReason = null,
    string? Explanation = null)
{
    public static ApprovalDecision Allowed(string explanation = "Command approval rule matched.") =>
        new(true, null, explanation);

    public static ApprovalDecision Blocked(ApprovalBlockReason reason, string explanation) =>
        new(false, reason, explanation);
}
