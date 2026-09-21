namespace BackgroundClicker.Core.Runner;

/// <summary>
/// Specifies the repetition mode for the click runner loop.
/// </summary>
public enum RepeatMode
{
    /// <summary>
    /// Repeats continuously until explicitly stopped or target window is closed.
    /// </summary>
    UntilStopped,

    /// <summary>
    /// Repeats for an exact specified cycle count, then halts into Idle state.
    /// </summary>
    Count
}
