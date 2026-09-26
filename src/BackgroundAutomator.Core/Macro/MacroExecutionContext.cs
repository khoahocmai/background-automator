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
    public Security.ProcessElevationService ElevationService { get; }
    public IAppLogger? Logger { get; }
    public IntPtr TargetHwnd { get; set; }
    public Action<string>? ProgressCallback { get; set; }

    /// <summary>
    /// Indicates whether unrestricted prompt approval ("FOOL MODE") was explicitly authorized
    /// by the user for this runner session.
    /// </summary>
    public bool IsFoolModeAuthorized { get; set; }

    public void ReportProgress(string progress) => ProgressCallback?.Invoke(progress);

    public MacroExecutionContext(
        IBackgroundClicker clicker,
        IWindowCaptureService captureService,
        IAppLogger? logger = null,
        IntPtr targetHwnd = default,
        IBackgroundKeyboard? keyboard = null,
        ITextDetectionService? textDetector = null,
        IForegroundKeyboard? foregroundKeyboard = null,
        IWindowForegroundService? foregroundService = null,
        ICommandPromptParser? commandPromptParser = null,
        Security.ProcessElevationService? elevationService = null)
    {
        Clicker = clicker ?? throw new ArgumentNullException(nameof(clicker));
        CaptureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        Keyboard = keyboard ?? new BackgroundKeyboardEngine(logger);
        TextDetector = textDetector ?? new UiAutomationTextDetectionService(logger);
        ForegroundKeyboard = foregroundKeyboard ?? new ForegroundKeyboardEngine(logger);
        ForegroundService = foregroundService ?? new Win32WindowForegroundService(logger);
        CommandPromptParser = commandPromptParser ?? new CommandPromptParser();
        ElevationService = elevationService ?? new Security.ProcessElevationService(logger);
        Logger = logger;
        TargetHwnd = targetHwnd;
    }
}
