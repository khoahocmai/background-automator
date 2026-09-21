namespace BackgroundClicker.Core.Targeting;

public enum TargetResolutionStatus
{
    Success,
    NotFound,
    Ambiguous,
    TargetInvalid
}

/// <summary>
/// Outcome of a TargetResolver re-resolution attempt.
/// </summary>
public sealed class TargetResolutionResult
{
    public TargetResolutionStatus Status { get; }
    public WindowTarget? Target { get; }
    public IReadOnlyList<WindowTargetCandidate> Candidates { get; }
    public string Message { get; }

    public bool IsSuccess => Status == TargetResolutionStatus.Success && Target != null;

    private TargetResolutionResult(
        TargetResolutionStatus status,
        WindowTarget? target,
        IReadOnlyList<WindowTargetCandidate> candidates,
        string message)
    {
        Status = status;
        Target = target;
        Candidates = candidates;
        Message = message;
    }

    public static TargetResolutionResult CreateSuccess(WindowTarget target) =>
        new(TargetResolutionStatus.Success, target, Array.Empty<WindowTargetCandidate>(),
            $"Target resolved to HWND {HwndFormatter.Format(target.TargetHwnd)}");

    public static TargetResolutionResult CreateNotFound(string reason) =>
        new(TargetResolutionStatus.NotFound, null, Array.Empty<WindowTargetCandidate>(), reason);

    public static TargetResolutionResult CreateAmbiguous(IReadOnlyList<WindowTargetCandidate> candidates, string? message = null) =>
        new(TargetResolutionStatus.Ambiguous, null, candidates,
            message ?? $"Found {candidates.Count} matching windows. Ambiguity must be resolved before targeting.");

    public static TargetResolutionResult CreateInvalid(string reason) =>
        new(TargetResolutionStatus.TargetInvalid, null, Array.Empty<WindowTargetCandidate>(), reason);
}
