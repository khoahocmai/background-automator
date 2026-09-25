using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.TextDetection;

namespace BackgroundAutomator.Core.Macro;

/// <summary>
/// Runtime context and dependencies provided to macro actions during execution.
/// </summary>
public sealed class MacroExecutionContext
{
    public IBackgroundClicker Clicker { get; }
    public IWindowCaptureService CaptureService { get; }
    public IBackgroundKeyboard Keyboard { get; }
    public IForegroundKeyboard ForegroundKeyboard { get; }
    public IWindowForegroundService ForegroundService { get; }
    public ITextDetectionService TextDetector { get; }
    public ICommandPromptParser CommandPromptParser { get; }
    public IAppLogger? Logger { get; }
    public IntPtr TargetHwnd { get; set; }

    public MacroExecutionContext(
        IBackgroundClicker clicker,
        IWindowCaptureService captureService,
        IAppLogger? logger = null,
        IntPtr targetHwnd = default,
        IBackgroundKeyboard? keyboard = null,
        ITextDetectionService? textDetector = null,
        IForegroundKeyboard? foregroundKeyboard = null,
        IWindowForegroundService? foregroundService = null,
        ICommandPromptParser? commandPromptParser = null)
    {
        Clicker = clicker ?? throw new ArgumentNullException(nameof(clicker));
        CaptureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        Keyboard = keyboard ?? new BackgroundKeyboardEngine(logger);
        TextDetector = textDetector ?? new UiAutomationTextDetectionService(logger);
        ForegroundKeyboard = foregroundKeyboard ?? new ForegroundKeyboardEngine(logger);
        ForegroundService = foregroundService ?? new Win32WindowForegroundService(logger);
        CommandPromptParser = commandPromptParser ?? new CommandPromptParser();
        Logger = logger;
        TargetHwnd = targetHwnd;
    }
}
