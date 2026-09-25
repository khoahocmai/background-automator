namespace BackgroundAutomator.Core.Keyboard;

/// <summary>
/// Abstraction for dispatching foreground keyboard input (e.g. SendInput).
/// Enables deterministic mocking without moving the real desktop cursor or injecting hardware input during automated tests.
/// </summary>
public interface IForegroundKeyboard
{
    /// <summary>
    /// Dispatches an Enter key press (key down followed by key up) to the current active foreground window.
    /// </summary>
    /// <returns>True if the input events were successfully posted.</returns>
    bool SendEnter();
}
