namespace BackgroundAutomator.Core.Profiles;

/// <summary>
/// Serializable configuration for an ApprovalRuleSet.
/// </summary>
public sealed class ApprovalRuleSetConfig
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? ExpectedProcess { get; set; }
    public string? ExpectedWindowClass { get; set; }
    public string? ExpectedPrompt { get; set; }
    public string? ExpectedSelectedOption { get; set; }
    public List<CommandApprovalRuleConfig> Rules { get; set; } = new();
}

/// <summary>
/// Serializable configuration for an individual CommandApprovalRule within a rule set.
/// </summary>
public sealed class CommandApprovalRuleConfig
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? AllowedCommand { get; set; }
    public string? CommandMatchMode { get; set; }
    public bool Enabled { get; set; } = true;
}
