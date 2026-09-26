using BackgroundAutomator.Core.Targeting;

namespace BackgroundAutomator.App.Services;

/// <summary>
/// Production Win32 implementation of <see cref="ITargetValidator"/> checking live OS window existence.
/// </summary>
public sealed class Win32TargetValidator : ITargetValidator
{
    public bool IsValid(WindowTarget? target)
    {
        return target != null && target.IsWindowValid();
    }
}
