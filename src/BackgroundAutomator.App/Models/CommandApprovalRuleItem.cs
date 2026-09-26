using BackgroundAutomator.Core.Approval;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BackgroundAutomator.App.Models;

/// <summary>
/// Observable item representing an individual command approval rule in the UI.
/// </summary>
public sealed partial class CommandApprovalRuleItem : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _commandText = string.Empty;

    [ObservableProperty]
    private bool _isEnabled = true;

    public CommandApprovalRuleItem()
    {
    }

    public CommandApprovalRuleItem(string name, string commandText, bool isEnabled = true, Guid? id = null)
    {
        _id = id ?? Guid.NewGuid();
        _name = name;
        _commandText = commandText;
        _isEnabled = isEnabled;
    }

    public CommandApprovalRule ToRule(
        string expectedProcess = "WindowsTerminal.exe",
        string expectedPrompt = "Run this command?",
        string expectedSelectedOption = "Yes, run command",
        string? expectedWindowClass = null) => new()
    {
        Id = Id,
        Name = string.IsNullOrWhiteSpace(Name) ? CommandText : Name,
        AllowedCommand = CommandText,
        Enabled = IsEnabled,
        CommandMatchMode = CommandMatchMode.Exact,
        ExpectedProcess = expectedProcess,
        ExpectedPrompt = expectedPrompt,
        ExpectedSelectedOption = expectedSelectedOption,
        ExpectedWindowClass = expectedWindowClass
    };

    public static CommandApprovalRuleItem FromRule(CommandApprovalRule rule) =>
        new(rule.Name, rule.AllowedCommand, rule.Enabled, rule.Id);
}
