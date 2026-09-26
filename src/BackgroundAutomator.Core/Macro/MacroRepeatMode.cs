namespace BackgroundAutomator.Core.Macro;

/// <summary>
/// Specifies how many times a macro action sequence should be repeated by MacroRunner.
/// </summary>
public enum MacroRepeatMode
{
    /// <summary>
    /// Executes the entire action sequence once and stops.
    /// </summary>
    Once,

    /// <summary>
    /// Executes the entire action sequence a fixed number of times.
    /// </summary>
    Count,

    /// <summary>
    /// Repeatedly executes the entire action sequence until explicitly stopped or cancelled.
    /// </summary>
    UntilStopped
}
