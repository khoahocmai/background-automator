namespace BackgroundAutomator.App.Services;

/// <summary>
/// Abstraction for dispatching actions to the UI thread.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>
    /// Queues an action for asynchronous execution on the UI thread.
    /// </summary>
    void InvokeAsync(Action action);
}
