namespace BackgroundAutomator.Core.Runner;

/// <summary>
/// Lifecycle state of the click execution runner.
/// </summary>
public enum RunnerState
{
    /// <summary>
    /// Runner is stopped and ready to start.
    /// </summary>
    Idle,

    /// <summary>
    /// Runner is actively executing the click loop.
    /// </summary>
    Running,

    /// <summary>
    /// Cancellation has been requested and the runner is shutting down.
    /// </summary>
    Stopping
}
