namespace BackgroundClicker.Core.Targeting;

/// <summary>
/// Descriptor representing a child control within a top-level window.
/// </summary>
public sealed class ChildTargetDescriptor
{
    public string? ControlClass { get; set; }
    public string? ControlText { get; set; }
    public int? ControlIndex { get; set; }
}

/// <summary>
/// Durable target specification that does NOT store ephemeral live HWNDs.
/// Used to persist and reliably re-resolve windows across restarts or reboots.
/// </summary>
public sealed class TargetDescriptor
{
    public string ProcessName { get; set; } = string.Empty;
    public string? WindowTitle { get; set; }
    public string? WindowClass { get; set; }
    public TitleMatchMode MatchMode { get; set; } = TitleMatchMode.Contains;
    public ChildTargetDescriptor? ChildDescriptor { get; set; }
    public int? SavedClientX { get; set; }
    public int? SavedClientY { get; set; }

    /// <summary>
    /// Creates a durable TargetDescriptor from an active live WindowTarget.
    /// </summary>
    public static TargetDescriptor FromWindowTarget(WindowTarget target, TitleMatchMode matchMode = TitleMatchMode.Contains)
    {
        var desc = new TargetDescriptor
        {
            ProcessName = target.ProcessName,
            WindowTitle = target.WindowTitle,
            WindowClass = target.WindowClass,
            MatchMode = matchMode,
            SavedClientX = target.ClientPoint.ClientX,
            SavedClientY = target.ClientPoint.ClientY
        };

        if (target.IsChildWindow)
        {
            desc.ChildDescriptor = new ChildTargetDescriptor
            {
                ControlClass = target.WindowClass,
                ControlText = target.WindowTitle
            };
        }

        return desc;
    }
}
