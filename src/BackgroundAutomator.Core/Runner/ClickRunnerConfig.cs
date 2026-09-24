using BackgroundAutomator.Core.Clicking;

namespace BackgroundAutomator.Core.Runner;

/// <summary>
/// Configuration parameters for click runner execution.
/// </summary>
public sealed class ClickRunnerConfig
{
    /// <summary>
    /// Ordered list of click points to execute in sequence.
    /// </summary>
    public IReadOnlyList<ClickPoint> Points { get; init; } = Array.Empty<ClickPoint>();

    /// <summary>
    /// Interval in milliseconds between clicks. Sane minimum is 10 ms.
    /// </summary>
    public int IntervalMilliseconds { get; init; } = 500;

    /// <summary>
    /// Mode of repetition (UntilStopped or Count).
    /// </summary>
    public RepeatMode RepeatMode { get; init; } = RepeatMode.UntilStopped;

    /// <summary>
    /// Target number of full cycles if RepeatMode is Count.
    /// </summary>
    public int RepeatCount { get; init; } = 1;

    /// <summary>
    /// Validates configuration parameters, throwing an exception if invalid.
    /// </summary>
    public void Validate()
    {
        if (Points == null || Points.Count == 0)
        {
            throw new ArgumentException("At least one click point must be configured.", nameof(Points));
        }

        if (IntervalMilliseconds < 10)
        {
            throw new ArgumentOutOfRangeException(nameof(IntervalMilliseconds), "Interval must be at least 10 ms.");
        }

        if (RepeatMode == RepeatMode.Count && RepeatCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(RepeatCount), "Repeat count must be at least 1.");
        }
    }
}
