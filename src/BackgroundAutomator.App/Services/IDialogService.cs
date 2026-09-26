namespace BackgroundAutomator.App.Services;

public enum DialogSeverity
{
    Information,
    Warning,
    Error
}

/// <summary>
/// Abstraction for user-facing modal dialogs, warnings, and confirmations.
/// Enables automated testing without triggering blocking Win32/WPF modal loops.
/// </summary>
public interface IDialogService
{
    void ShowInfo(string title, string message);

    void ShowWarning(string title, string message);

    bool Confirm(
        string title,
        string message,
        DialogSeverity severity = DialogSeverity.Warning);
}
