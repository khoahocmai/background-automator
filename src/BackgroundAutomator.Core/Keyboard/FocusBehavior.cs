namespace BackgroundAutomator.Core.Keyboard;

/// <summary>
/// Specifies the foreground window focus restoration behavior during safe auto-confirm execution.
/// </summary>
public enum FocusBehavior
{
    /// <summary>
    /// Minimizes foreground duration by restoring the previous foreground window immediately after Enter dispatch.
    /// Prompt acknowledgement polling proceeds while the target window is in the background.
    /// </summary>
    FastPulse,

    /// <summary>
    /// Retains foreground focus on the target window after Enter dispatch without restoring the previous foreground window.
    /// Prompt acknowledgement polling proceeds normally while the target remains foreground.
    /// </summary>
    KeepTargetForeground
}
