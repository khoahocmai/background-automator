using BackgroundAutomator.Core.Clicking;

namespace BackgroundAutomator.Core.Profiles;

/// <summary>
/// Serializable configuration for a single click point in Simple Mode.
/// </summary>
public sealed class ClickPointConfig
{
    public int ClientX { get; set; }
    public int ClientY { get; set; }
    public ClickType ClickType { get; set; } = ClickType.Single;
    public string? Label { get; set; }

    public ClickPointConfig() { }

    public ClickPointConfig(int clientX, int clientY, ClickType clickType = ClickType.Single, string? label = null)
    {
        ClientX = clientX;
        ClientY = clientY;
        ClickType = clickType;
        Label = label;
    }

    public static ClickPointConfig FromClickPoint(ClickPoint point, string? label = null) =>
        new(point.ClientX, point.ClientY, point.ClickType, label);

    public ClickPoint ToClickPoint(IntPtr hwnd) =>
        new(hwnd, ClientX, ClientY, ClickType);
}
