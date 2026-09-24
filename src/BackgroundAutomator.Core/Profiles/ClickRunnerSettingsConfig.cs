using BackgroundAutomator.Core.Runner;

namespace BackgroundAutomator.Core.Profiles;

/// <summary>
/// Serializable configuration for Simple Mode runner settings.
/// </summary>
public sealed class ClickRunnerSettingsConfig
{
    public int IntervalMs { get; set; } = 500;
    public RepeatMode RepeatMode { get; set; } = RepeatMode.UntilStopped;
    public int RepeatCount { get; set; } = 1;

    public static ClickRunnerSettingsConfig FromConfig(ClickRunnerConfig config) =>
        new()
        {
            IntervalMs = config.IntervalMilliseconds,
            RepeatMode = config.RepeatMode,
            RepeatCount = config.RepeatCount
        };

    public ClickRunnerConfig ToConfig(IReadOnlyList<BackgroundAutomator.Core.Clicking.ClickPoint>? points = null) =>
        new()
        {
            IntervalMilliseconds = Math.Max(10, IntervalMs),
            RepeatMode = RepeatMode,
            RepeatCount = Math.Max(1, RepeatCount),
            Points = points ?? Array.Empty<BackgroundAutomator.Core.Clicking.ClickPoint>()
        };
}
