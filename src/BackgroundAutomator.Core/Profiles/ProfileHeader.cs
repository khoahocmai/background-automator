namespace BackgroundAutomator.Core.Profiles;

/// <summary>
/// Summary header for a saved profile, used for list views and selection without loading entire contents.
/// </summary>
public sealed class ProfileHeader
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public ProfileMode Mode { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string TargetDescription { get; set; } = string.Empty;
    public int ItemCount { get; set; }

    public ProfileHeader() { }

    public ProfileHeader(string name, string filePath, ProfileMode mode, DateTime updatedAt, string targetDescription, int itemCount)
    {
        Name = name;
        FilePath = filePath;
        Mode = mode;
        UpdatedAt = updatedAt;
        TargetDescription = targetDescription;
        ItemCount = itemCount;
    }
}
