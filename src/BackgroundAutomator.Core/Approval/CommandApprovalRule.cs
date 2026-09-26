namespace BackgroundAutomator.Core.Approval;

/// <summary>
/// Explicit rule for evaluating and approving terminal command confirmation prompts.
/// Unattended auto-confirm requires all configured criteria to match.
/// </summary>
public sealed record CommandApprovalRule
{
    /// <summary>
    /// Unique identifier for this rule.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Friendly descriptive name for this rule (e.g. "Approve BackgroundAutomator tests").
    /// </summary>
    public string Name { get; init; } = string.Empty;

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
    /// Explicit allowlisted command required to authorize execution (e.g. "dotnet test BackgroundAutomator.sln").
    /// </summary>
    public string AllowedCommand { get; init; } = string.Empty;

    /// <summary>
    /// Alias for AllowedCommand.
    /// </summary>
    public string CommandText => AllowedCommand;

    /// <summary>
    /// Command comparison mode (Exact). Wildcards are intentionally not supported in Phase 4.
    /// </summary>
    public CommandMatchMode CommandMatchMode { get; init; } = CommandMatchMode.Exact;

    /// <summary>
    /// Whether this rule is currently active.
    /// </summary>
    public bool Enabled { get; init; } = true;
}
