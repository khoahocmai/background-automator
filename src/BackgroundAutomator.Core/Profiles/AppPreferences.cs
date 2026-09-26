namespace BackgroundAutomator.Core.Profiles;

/// <summary>
/// Application-level preferences persisted across sessions.
/// </summary>
public sealed class AppPreferences
{
    public string? LastUsedProfileName { get; set; }
    public string? StartupProfileName { get; set; }
}
