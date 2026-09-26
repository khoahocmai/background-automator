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
        public int ActivateCallCount { get; private set; }
        public int RestoreCallCount { get; private set; }
        public bool ShouldFailActivation { get; set; } = false;
        public bool IsMinimized { get; set; } = false;
        public bool WindowClosed { get; set; } = false;
        public int ProcessId { get; set; } = 1234;
        public string ProcessName { get; set; } = "WindowsTerminal";
        public string WindowClass { get; set; } = "CASCADIA_HOSTING_WINDOW_CLASS";
        public IntPtr RootHwndOverride { get; set; } = IntPtr.Zero;

        public IntPtr GetForegroundWindow() => CurrentForeground;

        public bool ActivateWindow(IntPtr hWnd)
        {
            ActivateCallCount++;
            if (ShouldFailActivation) return false;
            CurrentForeground = hWnd;
            return true;
        }

        public bool RestoreForegroundWindow(IntPtr hWnd)
        {
            RestoreCallCount++;
            LastRestoredHwnd = hWnd;
            CurrentForeground = hWnd;
            return true;
        }

        public bool IsWindowMinimized(IntPtr hWnd) => IsMinimized;
        public bool IsWindow(IntPtr hWnd) => !WindowClosed && hWnd != IntPtr.Zero;
        public bool IsWindowVisible(IntPtr hWnd) => true;
        public string GetProcessName(IntPtr hWnd) => ProcessName;
        public int GetProcessId(IntPtr hWnd) => ProcessId;
        public string GetWindowClass(IntPtr hWnd) => WindowClass;
        public IntPtr GetRootWindow(IntPtr hWnd) => RootHwndOverride != IntPtr.Zero ? RootHwndOverride : hWnd;
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

    private class FakeElevationService : BackgroundAutomator.Core.Security.ProcessElevationService
    {
        public BackgroundAutomator.Core.Security.ElevationCompatibility DesiredCompatibility { get; set; } =
            BackgroundAutomator.Core.Security.ElevationCompatibility.Compatible;

        public override BackgroundAutomator.Core.Security.ElevationCheckResult CheckCompatibility(int targetProcessId)
        {
            return DesiredCompatibility switch
            {
                BackgroundAutomator.Core.Security.ElevationCompatibility.UipiMismatch =>
                    BackgroundAutomator.Core.Security.ElevationCheckResult.CreateUipiMismatch(),
                BackgroundAutomator.Core.Security.ElevationCompatibility.Unknown =>
                    BackgroundAutomator.Core.Security.ElevationCheckResult.CreateUnknown(false, "Unknown"),
                _ => BackgroundAutomator.Core.Security.ElevationCheckResult.CreateCompatible(false, false)
            };
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

    private static (MacroExecutionContext context, FakeForegroundService fgService, FakeForegroundKeyboard fgKb, FakeElevationService elevService) CreateTestContext(
        ITextDetectionService detector,
        IntPtr targetHwnd)
    {
        var fgService = new FakeForegroundService();
        var fgKb = new FakeForegroundKeyboard();
        var elevService = new FakeElevationService();

        var context = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            foregroundKeyboard: fgKb,
            foregroundService: fgService,
            textDetector: detector,
            elevationService: elevService);

        return (context, fgService, fgKb, elevService);
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

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
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
        Assert.Equal(0, fgService.ActivateCallCount); // Must NOT activate window in ObserveOnly
        Assert.Equal(0, fgService.RestoreCallCount); // Must NOT restore window in ObserveOnly
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

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
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

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
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
        var (context, fgService, fgKb, elevService) = CreateTestContext(null!, targetHwnd);

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
            textDetector: detector,
            elevationService: elevService);

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

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
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

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
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
        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
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

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
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

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
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

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
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
        var (context, _, _, _) = CreateTestContext(null!, targetHwnd);
        var rule = CreateRule() with { AllowedCommand = "   " };

        var action = new SafeAutoConfirmAction(rule);
        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.InvalidConfiguration, result.Status);
    }

    [Fact]
    public async Task UIPI_Mismatch_Aborts_Immediately_Zero_Activation_Zero_SendEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        var (context, fgService, fgKb, elevService) = CreateTestContext(detector, targetHwnd);
        elevService.DesiredCompatibility = BackgroundAutomator.Core.Security.ElevationCompatibility.UipiMismatch;
        var rule = CreateRule();

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("UIPI mismatch", result.Message);
        Assert.Equal(0, fgService.ActivateCallCount);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Process_Name_Changed_After_Activation_Aborts_TargetMismatch_Zero_SendEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";

        int callCount = 0;
        var (context, fgService, fgKb, _) = CreateTestContext(null!, targetHwnd);

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            callCount++;
            if (callCount == 2)
            {
                // Target process name changed maliciously after foreground activation
                fgService.ProcessName = "MaliciousProcess.exe";
            }
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

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
            timeout: TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("does not match expected", result.Message);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Root_Window_Changed_After_Activation_Aborts_TargetMismatch_Zero_SendEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";

        int callCount = 0;
        var (context, fgService, fgKb, _) = CreateTestContext(null!, targetHwnd);

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            callCount++;
            if (callCount == 2)
            {
                // Target root HWND changed after foreground activation
                fgService.RootHwndOverride = (IntPtr)0x9999;
            }
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

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
            timeout: TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("Target root window changed", result.Message);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Prompt_A_Acknowledged_When_Prompt_B_Immediately_Appears_Without_Approving_B()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptA = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";
        string promptB = @"
rm -rf /danger

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        int callCount = 0;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            callCount++;
            if (callCount <= 2)
            {
                return Task.FromResult(TextDetectionResult.Success(promptA, promptA));
            }
            // During acknowledgment poll, prompt A is replaced immediately by prompt B!
            return Task.FromResult(TextDetectionResult.Success(promptB, promptB));
        });

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
        var rule = CreateRule();

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        // Command A was acknowledged because the fingerprint changed!
        Assert.True(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Contains("dotnet test BackgroundAutomator.sln", result.Message);
        Assert.Equal(1, fgKb.SendEnterCallCount); // Dispatched Enter for command A ONLY, NOT command B!
    }

    [Fact]
    public async Task Previous_Foreground_Closed_During_Execution_Does_Not_Crash()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        IntPtr closedForeground = (IntPtr)0x8888;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";

        int callCount = 0;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            callCount++;
            if (callCount <= 2)
            {
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            }
            return Task.FromResult(TextDetectionResult.NotFound(""));
        });

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
        fgService.CurrentForeground = closedForeground;

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Success, result.Status);
    }

    [Fact]
    public async Task Polling_Reports_Live_Blocker_And_Preserves_Blocker_On_Timeout()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        // Prompt with 2 conflicting candidates detected before prompt
        string ambiguousPrompt = @"
git pull
git status

Run this command?
> 1. Yes, run command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(ambiguousPrompt, ambiguousPrompt)));

        var (context, _, _, _) = CreateTestContext(detector, targetHwnd);
        var reportedStatuses = new List<string>();
        context.ProgressCallback = status => reportedStatuses.Add(status);

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            timeout: TimeSpan.FromMilliseconds(80),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Timeout, result.Status);
        Assert.Equal("AmbiguousPrompt", result.BlockerReason);
        Assert.Contains("Last blocker: AmbiguousPrompt", result.Message);
        Assert.Contains(reportedStatuses, s => s.StartsWith("Waiting — Ambiguous command"));
    }

    [Fact]
    public async Task Polling_Reports_CommandNotAllowed_And_Preserves_Blocker_On_Timeout()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string disallowedPrompt = @"
rm -rf /some/dir

Run this command?
> 1. Yes, run command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(disallowedPrompt, disallowedPrompt)));

        var (context, _, _, _) = CreateTestContext(detector, targetHwnd);
        var reportedStatuses = new List<string>();
        context.ProgressCallback = status => reportedStatuses.Add(status);

        var rule = CreateRule(); // Allowed: dotnet test BackgroundAutomator.sln
        var action = new SafeAutoConfirmAction(
            rule,
            timeout: TimeSpan.FromMilliseconds(80),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Timeout, result.Status);
        Assert.Equal("CommandNotAllowed", result.BlockerReason);
        Assert.Contains("Last blocker: CommandNotAllowed", result.Message);
        Assert.Contains("Blocked — Command not allowed", reportedStatuses);
    }

    [Fact]
    public async Task RuleSet_Approves_AllowedCommand_In_MultiRule_Set()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
Requesting permission for:
Get-Date

Run this command?
> 1. Yes, run command
";
        int detectCount = 0;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            detectCount++;
            if (detectCount >= 3)
            {
                // Prompt dismissed after Enter injection
                return Task.FromResult(TextDetectionResult.NotFound());
            }
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);

        var rule1 = new CommandApprovalRule { Name = "Build", AllowedCommand = "dotnet build BackgroundAutomator.sln" };
        var rule2 = new CommandApprovalRule { Name = "Test", AllowedCommand = "dotnet test BackgroundAutomator.sln" };
        var rule3 = new CommandApprovalRule { Name = "Date", AllowedCommand = "Get-Date" };
        var ruleSet = new ApprovalRuleSet("ToolSet", new[] { rule1, rule2, rule3 });

        var action = new SafeAutoConfirmAction(
            ruleSet,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount); // Single approval invariant: exactly 1 Enter!
        Assert.Contains("Get-Date", result.Message);
        Assert.Contains("using rule 'Date'", result.Message);
    }

    [Fact]
    public async Task WaitMode_Indefinite_DoesNotTimeout_And_Approves_When_Prompt_Appears()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
Requesting permission for:
Get-Date

Run this command?
> 1. Yes, run command
";
        int callCount = 0;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            callCount++;
            // First 4 calls: no prompt
            if (callCount <= 4)
            {
                return Task.FromResult(TextDetectionResult.NotFound());
            }
            // Next call: prompt visible
            if (callCount <= 6)
            {
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            }
            // After Enter: prompt dismissed
            return Task.FromResult(TextDetectionResult.NotFound());
        });

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
        var rule = new CommandApprovalRule { Name = "Date", AllowedCommand = "Get-Date" };

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromMilliseconds(50), // Even though timeout is small, Indefinite wait ignores it
            pollInterval: TimeSpan.FromMilliseconds(15),
            waitMode: AutoConfirmWaitMode.Indefinite);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount);
        Assert.True(callCount > 4);
    }

    [Fact]
    public async Task WaitMode_Indefinite_CancelsCleanly_When_Cancelled()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.NotFound()));

        var (context, _, fgKb, _) = CreateTestContext(detector, targetHwnd);
        var rule = new CommandApprovalRule { Name = "Date", AllowedCommand = "Get-Date" };

        var action = new SafeAutoConfirmAction(
            rule,
            pollInterval: TimeSpan.FromMilliseconds(20),
            waitMode: AutoConfirmWaitMode.Indefinite);

        using var cts = new CancellationTokenSource(60);

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }
}
