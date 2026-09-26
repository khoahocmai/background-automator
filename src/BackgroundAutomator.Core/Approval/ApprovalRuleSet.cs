namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// A collection of command approval rules with shared terminal environment expectations.
/// Evaluates extracted commands against allowlisted rules using exact matching.
/// </summary>
public sealed record ApprovalRuleSet
{
    /// <summary>
    /// Unique identifier for this rule set.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Descriptive name for this rule set.
    /// </summary>
    public string Name { get; init; } = "Approval Rule Set";

    /// <summary>
    /// Expected process name hosting the terminal (e.g. "WindowsTerminal.exe").
    /// </summary>
    public string ExpectedProcess { get; init; } = "WindowsTerminal.exe";

    /// <summary>
    /// Optional window class restriction (e.g. "CASCADIA_HOSTING_WINDOW_CLASS").
    /// </summary>
    public string? ExpectedWindowClass { get; init; }

    /// <summary>
    /// Expected prompt string (e.g. "Run this command?").
    /// </summary>
    public string ExpectedPrompt { get; init; } = "Run this command?";

    /// <summary>
    /// Expected selected option text (e.g. "Yes, run command").
    /// </summary>
    public string ExpectedSelectedOption { get; init; } = "Yes, run command";

    /// <summary>
    /// List of command approval rules in this set.
    /// </summary>
    public List<CommandApprovalRule> Rules { get; init; } = new();

    public ApprovalRuleSet()
    {
    }

    public ApprovalRuleSet(
        string name,
        IEnumerable<CommandApprovalRule> rules,
        string expectedProcess = "WindowsTerminal.exe",
        string expectedPrompt = "Run this command?",
        string expectedSelectedOption = "Yes, run command",
        string? expectedWindowClass = null)
    {
        Name = name;
        Rules = rules.ToList();
        ExpectedProcess = expectedProcess;
        ExpectedPrompt = expectedPrompt;
        ExpectedSelectedOption = expectedSelectedOption;
        ExpectedWindowClass = expectedWindowClass;
    }

    /// <summary>
    /// Creates an ApprovalRuleSet containing a single rule for backward compatibility.
    /// </summary>
    public static ApprovalRuleSet FromSingleRule(CommandApprovalRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new ApprovalRuleSet
        {
            Id = rule.Id,
            Name = rule.Name,
            ExpectedProcess = rule.ExpectedProcess,
            ExpectedWindowClass = rule.ExpectedWindowClass,
            ExpectedPrompt = rule.ExpectedPrompt,
            ExpectedSelectedOption = rule.ExpectedSelectedOption,
            Rules = new List<CommandApprovalRule> { rule }
        };
    }
}
