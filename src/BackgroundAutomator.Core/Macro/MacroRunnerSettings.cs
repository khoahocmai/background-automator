namespace BackgroundAutomator.Core.Macro;

/// <summary>
/// Execution settings for <see cref="MacroRunner"/> controlling repetition and cycle pacing.
/// </summary>
public sealed record MacroRunnerSettings(
    MacroRepeatMode RepeatMode = MacroRepeatMode.Once,
    int RepeatCount = 1,
    int CycleDelayMilliseconds = 500)
{
    public static MacroRunnerSettings Default => new();
}
