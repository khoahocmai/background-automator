using System.Drawing;
using BackgroundClicker.Win32;

namespace BackgroundClicker.Core.Targeting;

/// <summary>
/// Represents resolved target window information, distinguishing the root window from the deepest child control.
/// </summary>
public sealed class WindowTarget
{
    public IntPtr RootHwnd { get; init; }
    public IntPtr TargetHwnd { get; init; }
    public IntPtr ParentHwnd { get; init; }

    public int ProcessId { get; init; }
    public uint ThreadId { get; init; }
    public string ProcessName { get; init; } = string.Empty;

    public string WindowTitle { get; init; } = string.Empty;
    public string WindowClass { get; init; } = string.Empty;

    public Point ScreenPoint { get; init; }
    public TargetPoint ClientPoint { get; init; }

    /// <summary>
    /// Friendly display text for dropdowns and inspection labels.
    /// </summary>
    public string DisplayText
    {
        get
        {
            string proc = string.IsNullOrWhiteSpace(ProcessName) ? "[Unknown]" : ProcessName;
            string title = string.IsNullOrWhiteSpace(WindowTitle)
                ? (string.IsNullOrWhiteSpace(WindowClass) ? "[No Title]" : $"[{WindowClass}]")
                : WindowTitle;

            return $"{proc} — {title} ({HwndFormatter.FormatShort(RootHwnd)})";
        }
    }

    /// <summary>
    /// Checks whether the target window handle still exists and is recognized by the OS.
    /// </summary>
    public bool IsWindowValid() =>
        TargetHwnd != IntPtr.Zero && User32.IsWindow(TargetHwnd);

    /// <summary>
    /// Checks whether the root window handle still exists and is recognized by the OS.
    /// </summary>
    public bool IsRootValid() =>
        RootHwnd != IntPtr.Zero && User32.IsWindow(RootHwnd);

    public override string ToString() =>
        $"{DisplayText} | Target: {HwndFormatter.Format(TargetHwnd)} Client: ({ClientPoint.ClientX}, {ClientPoint.ClientY})";
}
