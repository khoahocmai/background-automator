namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Controls whether BackgroundAutomator is permitted to activate Windows Terminal foreground
/// while another application owns keyboard focus.
/// </summary>
public enum ForegroundPolicy
{
    /// <summary>
    /// BackgroundAutomator may briefly activate Windows Terminal after user activity has been idle long enough.
    /// Preserves backward-compatible focus pulse behavior.
    /// </summary>
    AllowIdlePulse,

    /// <summary>
    /// BackgroundAutomator never activates Windows Terminal automatically.
    /// Approval pauses until Windows Terminal is naturally brought to the foreground by the user.
    /// </summary>
    StrictTerminalForegroundOnly
}
