namespace BackgroundClicker.Core.Macro;

/// <summary>
/// Final summary result of an entire macro sequence run.
/// </summary>
public sealed record MacroExecutionResult(
    MacroActionStatus FinalStatus,
    int CompletedActionsCount,
    int TotalActionsCount,
    TimeSpan ElapsedTime,
    string? Message = null)
{
    public bool IsSuccess => FinalStatus == MacroActionStatus.Success;
}
