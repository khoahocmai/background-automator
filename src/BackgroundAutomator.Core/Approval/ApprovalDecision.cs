namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Result of evaluating a CommandApprovalRule or ApprovalRuleSet against the live target state.
/// </summary>
public sealed record ApprovalDecision(
    bool IsAllowed,
    ApprovalBlockReason? BlockReason = null,
    string? Explanation = null,
    Guid? MatchedRuleId = null,
    string? MatchedRuleName = null)
{
    public static ApprovalDecision Allowed(
        string explanation = "Command approval rule matched.",
        Guid? matchedRuleId = null,
        string? matchedRuleName = null) =>
        new(true, null, explanation, matchedRuleId, matchedRuleName);

    public static ApprovalDecision Blocked(ApprovalBlockReason reason, string explanation) =>
        new(false, reason, explanation, null, null);
}
