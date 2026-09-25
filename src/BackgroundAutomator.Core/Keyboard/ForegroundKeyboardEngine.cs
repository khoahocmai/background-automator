using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.Core.Keyboard;

/// <summary>
/// Default implementation of <see cref="IForegroundKeyboard"/> using Win32 SendInput.
/// </summary>
public sealed class ForegroundKeyboardEngine : IForegroundKeyboard
{
    private readonly IAppLogger? _logger;

    public ForegroundKeyboardEngine(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public bool SendEnter()
    {
        try
        {
            bool ok = SendInputHelper.SendEnter();
            if (ok)
            {
                _logger?.Debug("[ForegroundKeyboard] SendInput Enter (down + up) succeeded.");
            }
            else
            {
                _logger?.Warning("[ForegroundKeyboard] SendInput Enter failed.");
            }
            return ok;
        }
        catch (Exception ex)
        {
            _logger?.Warning($"[ForegroundKeyboard] SendInput exception: {ex.Message}");
            return false;
        }
    }
}
