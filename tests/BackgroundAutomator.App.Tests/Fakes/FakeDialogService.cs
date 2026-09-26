using BackgroundAutomator.App.Services;

namespace BackgroundAutomator.App.Tests.Fakes;

public class FakeDialogService : IDialogService
{
    public bool ConfirmResult { get; set; } = true;
    public int ConfirmCallCount { get; private set; }
    public int InfoCallCount { get; private set; }
    public int WarningCallCount { get; private set; }

    public string? LastTitle { get; private set; }
    public string? LastMessage { get; private set; }
    public DialogSeverity? LastSeverity { get; private set; }

    public void ShowInfo(string title, string message)
    {
        InfoCallCount++;
        LastTitle = title;
        LastMessage = message;
        LastSeverity = DialogSeverity.Information;
    }

    public void ShowWarning(string title, string message)
    {
        WarningCallCount++;
        LastTitle = title;
        LastMessage = message;
        LastSeverity = DialogSeverity.Warning;
    }

    public bool Confirm(
        string title,
        string message,
        DialogSeverity severity = DialogSeverity.Warning)
    {
        ConfirmCallCount++;
        LastTitle = title;
        LastMessage = message;
        LastSeverity = severity;
        return ConfirmResult;
    }

    public void Reset()
    {
        ConfirmCallCount = 0;
        InfoCallCount = 0;
        WarningCallCount = 0;
        LastTitle = null;
        LastMessage = null;
        LastSeverity = null;
    }
}
