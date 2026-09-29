namespace BackgroundAutomator.Core.UserActivity;

/// <summary>
/// Service providing system-level user idle duration detection based on Windows last-input mechanism.
/// </summary>
public interface IUserActivityService
{
    /// <summary>
    /// Attempts to retrieve the time elapsed since the last user input event.
    /// </summary>
    /// <param name="idleDuration">Output time span elapsed since last input event.</param>
    /// <returns>True if idle duration was successfully retrieved; false if the system call failed.</returns>
    bool TryGetIdleDuration(out TimeSpan idleDuration);

    /// <summary>
    /// Informs the service that synthetic input was injected by the automation,
    /// preventing injected keystrokes from being falsely recognized as user physical activity.
    /// </summary>
    void NotifyInputInjected();
}
