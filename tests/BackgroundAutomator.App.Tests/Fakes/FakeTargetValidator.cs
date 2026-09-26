using BackgroundAutomator.App.Services;
using BackgroundAutomator.Core.Targeting;

namespace BackgroundAutomator.App.Tests.Fakes;

public class FakeTargetValidator : ITargetValidator
{
    public bool IsValidResult { get; set; } = true;
    public int ValidateCallCount { get; private set; }
    public WindowTarget? LastValidatedTarget { get; private set; }

    public bool IsValid(WindowTarget? target)
    {
        ValidateCallCount++;
        LastValidatedTarget = target;
        return target != null && IsValidResult;
    }

    public void Reset()
    {
        ValidateCallCount = 0;
        LastValidatedTarget = null;
        IsValidResult = true;
    }
}
