using BackgroundClicker.Core.Capture;
using BackgroundClicker.Core.Clicking;
using BackgroundClicker.Core.Logging;

namespace BackgroundClicker.Core.Macro;

/// <summary>
/// Runtime context and dependencies provided to macro actions during execution.
/// </summary>
public sealed class MacroExecutionContext
{
    public IBackgroundClicker Clicker { get; }
    public IWindowCaptureService CaptureService { get; }
    public IAppLogger? Logger { get; }
    public IntPtr TargetHwnd { get; set; }

    public MacroExecutionContext(
        IBackgroundClicker clicker,
        IWindowCaptureService captureService,
        IAppLogger? logger = null,
        IntPtr targetHwnd = default)
    {
        Clicker = clicker ?? throw new ArgumentNullException(nameof(clicker));
        CaptureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        Logger = logger;
        TargetHwnd = targetHwnd;
    }
}
