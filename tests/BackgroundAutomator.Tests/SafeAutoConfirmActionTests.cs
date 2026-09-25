using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Core.TextDetection;
using Xunit;

namespace BackgroundAutomator.Tests;

public class SafeAutoConfirmActionTests
{
    private class FakeForegroundService : IWindowForegroundService
    {
        public IntPtr CurrentForeground { get; set; } = (IntPtr)0x9999; // some other app
        public IntPtr LastRestoredHwnd { get; set; } = IntPtr.Zero;
        public bool ShouldFailActivation { get; set; } = false;
        public bool IsMinimized { get; set; } = false;
        public string ProcessName { get; set; } = "WindowsTerminal";
        public string WindowClass { get; set; } = "CASCADIA_HOSTING_WINDOW_CLASS";

        public IntPtr GetForegroundWindow() => CurrentForeground;

        public bool ActivateWindow(IntPtr hWnd)
        {
            if (ShouldFailActivation) return false;
            CurrentForeground = hWnd;
            return true;
        }

        public bool RestoreForegroundWindow(IntPtr hWnd)
        {
            LastRestoredHwnd = hWnd;
            CurrentForeground = hWnd;
            return true;
        }

        public bool IsWindowMinimized(IntPtr hWnd) => IsMinimized;
        public bool IsWindow(IntPtr hWnd) => hWnd != IntPtr.Zero;
        public bool IsWindowVisible(IntPtr hWnd) => true;
        public string GetProcessName(IntPtr hWnd) => ProcessName;
        public string GetWindowClass(IntPtr hWnd) => WindowClass;
        public IntPtr GetRootWindow(IntPtr hWnd) => hWnd;
    }

    private class FakeForegroundKeyboard : IForegroundKeyboard
    {
        public int SendEnterCallCount { get; private set; }

        public bool SendEnter()
        {
            SendEnterCallCount++;
            return true;
        }
    }

    private class FakeTextDetector : ITextDetectionService
    {
        private readonly Func<IntPtr, TextDetectionRequest, Task<TextDetectionResult>> _handler;

        public FakeTextDetector(Func<IntPtr, TextDetectionRequest, Task<TextDetectionResult>> handler)
        {
            _handler = handler;
        }

        public Task<TextDetectionResult> DetectAsync(IntPtr targetHwnd, TextDetectionRequest request, CancellationToken ct = default) =>
            _handler(targetHwnd, request);
    }

    private class NullClicker : IBackgroundClicker
    {
        public ClickResult Click(TargetPoint target) => ClickResult.Success;
        public ClickResult DoubleClick(TargetPoint target) => ClickResult.Success;
    }

    private class NullCaptureService : IWindowCaptureService
    {
        public WindowCapture? CaptureClientArea(IntPtr hWnd) => null;
        public Task<WindowCapture?> CaptureClientAreaAsync(IntPtr hWnd, CancellationToken ct = default) =>
            Task.FromResult<WindowCapture?>(null);
    }

    private static (MacroExecutionContext context, FakeForegroundService fgService, FakeForegroundKeyboard fgKb) CreateTestContext(
        ITextDetectionService detector,
        IntPtr targetHwnd)
    {
        var fgService = new FakeForegroundService();
        var fgKb = new FakeForegroundKeyboard();

        var context = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            foregroundKeyboard: fgKb,
            foregroundService: fgService,
            textDetector: detector);

        return (context, fgService, fgKb);
    }

    private static CommandApprovalRule CreateRule() => new()
    {
        Name = "Approve tests",
        ExpectedProcess = "WindowsTerminal.exe",
        ExpectedWindowClass = "CASCADIA",
        ExpectedPrompt = "Run this command?",
        ExpectedSelectedOption = "Yes, run command",
        AllowedCommand = "dotnet test BackgroundAutomator.sln",
        CommandMatchMode = CommandMatchMode.Exact,
        Enabled = true
    };

    [Fact]
    public async Task ObserveOnly_Matches_Rule_Logs_WouldApprove_No_SendEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        var (context, fgService, fgKb) = CreateTestContext(detector, targetHwnd);
        var rule = CreateRule();

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.ObserveOnly,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Contains("WOULD APPROVE", result.Message);
        Assert.Equal(0, fgKb.SendEnterCallCount); // Must NOT send input in ObserveOnly
    }

    [Fact]
    public async Task Confirm_FullFlow_Activates_Revalidates_SendsEnter_RestoresForeground_Success()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        IntPtr originalForeground = (IntPtr)0x9999;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        int callCount = 0;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            callCount++;
            // 1st call: initial detection -> matches prompt
            // 2nd call: revalidation after foreground activation -> matches prompt
            // 3rd call: acknowledgement check after Enter -> prompt disappeared
            if (callCount <= 2)
            {
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            }
            return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
        });

        var (context, fgService, fgKb) = CreateTestContext(detector, targetHwnd);
        fgService.CurrentForeground = originalForeground;
        var rule = CreateRule();

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, fgKb.SendEnterCallCount); // Enter sent exactly once
        Assert.Equal(originalForeground, fgService.LastRestoredHwnd); // Previous foreground restored
    }

    [Fact]
    public async Task Confirm_ActivationFailure_DoesNotSendEnter_ReturnsApprovalBlocked()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        var (context, fgService, fgKb) = CreateTestContext(detector, targetHwnd);
        fgService.ShouldFailActivation = true; // SetForegroundWindow fails
        var rule = CreateRule();

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("ForegroundActivationFailed", result.Message);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Confirm_ForegroundChanged_ImmediatelyBeforeSendInput_Aborts_DoesNotSendEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        IntPtr thirdPartyHwnd = (IntPtr)0x5555;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";

        int callCount = 0;
        var (context, fgService, fgKb) = CreateTestContext(null!, targetHwnd);

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            callCount++;
            if (callCount == 2)
            {
                // Simulate focus stealing right after revalidation
                fgService.CurrentForeground = thirdPartyHwnd;
            }
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        // re-instantiate context with detector
        context = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            foregroundKeyboard: fgKb,
            foregroundService: fgService,
            textDetector: detector);

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("ForegroundChanged", result.Message);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Confirm_RevalidationFails_PromptDisappears_DoesNotSendEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";

        int callCount = 0;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            }
            // Prompt disappeared on revalidation!
            return Task.FromResult(TextDetectionResult.NotFound(""));
        });

        var (context, fgService, fgKb) = CreateTestContext(detector, targetHwnd);
        var rule = CreateRule();

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("RevalidationFailed", result.Message);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Confirm_RevalidationFails_CommandChanged_DoesNotSendEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string goodPrompt = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";
        string maliciousPrompt = @"
rm -rf /

Run this command?
> 1. Yes, run command
";

        int callCount = 0;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return Task.FromResult(TextDetectionResult.Success(goodPrompt, goodPrompt));
            }
            // Command changed between background check and foreground revalidation!
            return Task.FromResult(TextDetectionResult.Success(maliciousPrompt, maliciousPrompt));
        });

        var (context, fgService, fgKb) = CreateTestContext(detector, targetHwnd);
        var rule = CreateRule();

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Target_Minimized_FailsClosed_TargetNotInteractable()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        var detector = new FakeTextDetector((hwnd, req) => Task.FromResult(TextDetectionResult.Success("")));
        var (context, fgService, fgKb) = CreateTestContext(detector, targetHwnd);
        fgService.IsMinimized = true; // Minimized target!

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(rule, timeout: TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("TargetNotInteractable", result.Message);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Confirmation_Not_Acknowledged_When_Prompt_Persists_After_Enter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";

        // Prompt NEVER disappears even after Enter
        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        var (context, fgService, fgKb) = CreateTestContext(detector, targetHwnd);
        var rule = CreateRule();

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("ConfirmationNotAcknowledged", result.Message);
        Assert.Equal(1, fgKb.SendEnterCallCount); // Enter was sent, but duplicate was prevented
    }

    [Fact]
    public async Task Cancellation_Halts_Execution()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        var detector = new FakeTextDetector(async (hwnd, req) =>
        {
            await Task.Delay(200);
            return TextDetectionResult.NotFound();
        });

        var (context, fgService, fgKb) = CreateTestContext(detector, targetHwnd);
        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(rule, timeout: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(60);
        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task Timeout_When_Prompt_Never_Appears()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.NotFound("Other output...")));

        var (context, fgService, fgKb) = CreateTestContext(detector, targetHwnd);
        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(rule, timeout: TimeSpan.FromMilliseconds(100), pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Timeout, result.Status);
    }

    [Fact]
    public async Task InvalidConfiguration_When_AllowedCommand_Empty()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        var (context, _, _) = CreateTestContext(null!, targetHwnd);
        var rule = CreateRule() with { AllowedCommand = "   " };

        var action = new SafeAutoConfirmAction(rule);
        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.InvalidConfiguration, result.Status);
    }
}
