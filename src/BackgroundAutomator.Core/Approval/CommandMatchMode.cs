namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Matching mode for command allowlist evaluation.
/// Safe default is Exact.
/// </summary>
public enum CommandMatchMode
{
    /// <summary>
    /// Exact match against the extracted command (whitespace trimmed, case-insensitive).
    /// </summary>
    Exact
}
