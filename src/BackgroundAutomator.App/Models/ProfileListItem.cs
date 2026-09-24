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

    public ProfileListItem(ProfileHeader header)
    {
        Name = header.Name;
        Mode = header.Mode;
        TargetDescription = header.TargetDescription;
        ItemCount = header.ItemCount;
        UpdatedAt = header.UpdatedAt;
    }
}
