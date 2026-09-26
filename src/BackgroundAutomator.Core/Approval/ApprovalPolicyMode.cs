namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Policy mode controlling command approval evaluation.
/// </summary>
public enum ApprovalPolicyMode
{
    /// <summary>
    /// Requires explicit command matching against an enabled ApprovalRuleSet.
    /// </summary>
    ExactRules = 0,

    /// <summary>
    /// Unrestricted prompt auto-approval ("FOOL MODE"): approves any command presented
    /// by a structurally recognized permission prompt with valid selected option,
    /// target identity, and UIPI checks, bypassing command allowlist matching.
    /// </summary>
    FoolMode = 1
}
