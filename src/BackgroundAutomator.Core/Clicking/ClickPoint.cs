using BackgroundAutomator.Core.Targeting;

namespace BackgroundAutomator.Core.Clicking;

/// <summary>
/// Represents an ordered click target containing HWND, target-relative client coordinates, and click action type.
/// </summary>
public sealed record ClickPoint(IntPtr Hwnd, int ClientX, int ClientY, ClickType ClickType = ClickType.Single)
{
    public TargetPoint TargetPoint => new(Hwnd, ClientX, ClientY);

    public ClickPoint(TargetPoint target, ClickType clickType = ClickType.Single)
        : this(target.Hwnd, target.ClientX, target.ClientY, clickType)
    {
    }

    public override string ToString() =>
        $"[HWND {HwndFormatter.FormatShort(Hwnd)}: ({ClientX}, {ClientY}) | {ClickType}]";
}
