namespace BackgroundAutomator.Core.Keyboard;

/// <summary>
/// Represents the outcome of a background keyboard press operation.
/// </summary>
public enum KeyPressResult
{
    /// <summary>
    /// All keyboard messages (WM_KEYDOWN, WM_KEYUP) were successfully posted to the target message queue.
    /// </summary>
    Success,

    /// <summary>
    /// The target window handle is zero, invalid, or belongs to a closed window.
    /// </summary>
    InvalidTarget,

    /// <summary>
    /// Posting one or more keyboard messages to the target window queue failed.
    /// </summary>
    PostFailed
}
