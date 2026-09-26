using BackgroundAutomator.Core.Targeting;

namespace BackgroundAutomator.App.Services;

/// <summary>
/// Abstraction for checking the validity and existence of an automation target window.
/// </summary>
public interface ITargetValidator
{
    bool IsValid(WindowTarget? target);
}
