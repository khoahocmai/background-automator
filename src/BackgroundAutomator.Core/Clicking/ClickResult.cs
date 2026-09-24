namespace BackgroundAutomator.Core.Clicking;

/// <summary>
/// Represents the outcome of a background mouse click operation.
/// </summary>
public enum ClickResult
{
    /// <summary>
    /// All messages in the click sequence were successfully posted to the target message queue.
    /// </summary>
    Success,

    /// <summary>
    /// The target window handle is zero, invalid, or belongs to a closed window.
    /// </summary>
    InvalidTarget,

    /// <summary>
    /// Posting one or more messages to the target window queue failed.
    /// </summary>
    PostFailed
}
