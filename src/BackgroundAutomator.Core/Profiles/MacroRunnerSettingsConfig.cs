namespace BackgroundAutomator.Core.Profiles;

/// <summary>
/// Serializable configuration for macro runner settings (repeat mode, count, delay).
/// </summary>
public sealed class MacroRunnerSettingsConfig
{
    public string RepeatMode { get; set; } = "Once";
    public int RepeatCount { get; set; } = 1;
    public int CycleDelayMilliseconds { get; set; } = 500;
}
