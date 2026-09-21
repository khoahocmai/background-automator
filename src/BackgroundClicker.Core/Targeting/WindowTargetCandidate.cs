namespace BackgroundClicker.Core.Targeting;

/// <summary>
/// Lightweight metadata for a top-level window candidate discovered during enumeration.
/// Retains the raw HWND and process metadata directly.
/// </summary>
public sealed record WindowTargetCandidate(
    IntPtr Hwnd,
    int ProcessId,
    uint ThreadId,
    string ProcessName,
    string WindowTitle,
    string WindowClass)
{
    /// <summary>
    /// Friendly display text for UI dropdowns (e.g. "TestTarget.exe — BackgroundClicker Test Target (0x001203AA)").
    /// </summary>
    public string DisplayText
    {
        get
        {
            string proc = string.IsNullOrWhiteSpace(ProcessName) ? "[Unknown]" : ProcessName;
            string title = string.IsNullOrWhiteSpace(WindowTitle)
                ? (string.IsNullOrWhiteSpace(WindowClass) ? "[No Title]" : $"[{WindowClass}]")
                : WindowTitle;

            return $"{proc} — {title} ({HwndFormatter.FormatShort(Hwnd)})";
        }
    }

    public override string ToString() => DisplayText;
}
