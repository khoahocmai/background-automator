using BackgroundAutomator.Core.Profiles;

namespace BackgroundAutomator.App.Models;

public sealed class ProfileListItem
{
    public string Name { get; }
    public ProfileMode Mode { get; }
    public string ModeString => Mode == ProfileMode.Macro ? "Macro" : "Simple";
    public string TargetDescription { get; }
    public int ItemCount { get; }
    public DateTime UpdatedAt { get; }
    public string UpdatedAtFormatted => UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public bool IsStartup { get; set; }
    public string DisplayName => IsStartup ? $"★ {Name}" : Name;

    public ProfileListItem(ProfileHeader header, bool isStartup = false)
    {
        Name = header.Name;
        Mode = header.Mode;
        TargetDescription = header.TargetDescription;
        ItemCount = header.ItemCount;
        UpdatedAt = header.UpdatedAt;
        IsStartup = isStartup;
    }
}
