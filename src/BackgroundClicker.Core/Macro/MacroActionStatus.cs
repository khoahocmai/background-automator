namespace BackgroundClicker.Core.Macro;

/// <summary>
/// Status outcome of an individual macro action or entire macro execution.
/// </summary>
public enum MacroActionStatus
{
    Success,
    Cancelled,
    TargetUnavailable,
    Timeout,
    CaptureFailed,
    ClickFailed,
    InvalidConfiguration
}
