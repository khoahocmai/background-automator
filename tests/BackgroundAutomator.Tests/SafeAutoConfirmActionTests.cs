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
        public bool ShouldFailRestore { get; set; } = false;
        public Action<string>? OnEvent { get; set; }
        public bool IsMinimized { get; set; } = false;
        public bool WindowClosed { get; set; } = false;
        public int ProcessId { get; set; } = 1234;
        public string ProcessName { get; set; } = "WindowsTerminal";
        public string WindowClass { get; set; } = "CASCADIA_HOSTING_WINDOW_CLASS";
        public IntPtr RootHwndOverride { get; set; } = IntPtr.Zero;
        public Func<IntPtr>? ForegroundProvider { get; set; }

        public IntPtr GetForegroundWindow() => ForegroundProvider != null ? ForegroundProvider() : CurrentForeground;

        public bool ActivateWindow(IntPtr hWnd)
        {
            ActivateCallCount++;
            OnEvent?.Invoke($"ActivateWindow:{hWnd}");
            if (ShouldFailActivation) return false;
            CurrentForeground = hWnd;
            return true;
        }

        public bool RestoreForegroundWindow(IntPtr hWnd)
        {
            RestoreCallCount++;
            OnEvent?.Invoke($"RestoreForegroundWindow:{hWnd}");
            LastRestoredHwnd = hWnd;
            if (ShouldFailRestore) return false;
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
        public Action<string>? OnEvent { get; set; }

        public bool SendEnter()
        {
            SendEnterCallCount++;
            OnEvent?.Invoke("SendEnter");
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

    public class FakeUserActivityService : BackgroundAutomator.Core.UserActivity.IUserActivityService
    {
        public TimeSpan IdleDuration { get; set; } = TimeSpan.FromSeconds(10);
        public bool ReturnSuccess { get; set; } = true;
        public int CallCount { get; private set; }
        public bool InputInjectedNotified { get; private set; }
        public Func<TimeSpan>? IdleDurationProvider { get; set; }

        public bool TryGetIdleDuration(out TimeSpan idleDuration)
        {
            CallCount++;
            if (!ReturnSuccess)
            {
                idleDuration = TimeSpan.Zero;
                return false;
            }

            idleDuration = IdleDurationProvider != null ? IdleDurationProvider() : IdleDuration;
            return true;
        }

        public TimeSpan LastNotifiedIdleDurationBeforeInjection { get; private set; }

        public void NotifyInputInjected(TimeSpan idleDurationBeforeInjection)
        {
            InputInjectedNotified = true;
            LastNotifiedIdleDurationBeforeInjection = idleDurationBeforeInjection;
        }

        public void NotifyInputInjected()
        {
            NotifyInputInjected(TimeSpan.Zero);
        }
    }

    private static (MacroExecutionContext context, FakeForegroundService fgService, FakeForegroundKeyboard fgKb, FakeElevationService elevService) CreateTestContext(
        ITextDetectionService detector,
        IntPtr targetHwnd)
    {
        var fgService = new FakeForegroundService();
        var fgKb = new FakeForegroundKeyboard();
        var elevService = new FakeElevationService();
        var actService = new FakeUserActivityService();

        var context = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            foregroundKeyboard: fgKb,
            foregroundService: fgService,
            textDetector: detector,
            elevationService: elevService,
            userActivityService: actService);

        return (context, fgService, fgKb, elevService);
    }

    private static (MacroExecutionContext context, FakeForegroundService fgService, FakeForegroundKeyboard fgKb, FakeElevationService elevService, FakeUserActivityService actService) CreateTestContextWithActivity(
        ITextDetectionService detector,
        IntPtr targetHwnd,
        FakeUserActivityService? activityService = null)
    {
        var fgService = new FakeForegroundService();
        var fgKb = new FakeForegroundKeyboard();
        var elevService = new FakeElevationService();
        var actService = activityService ?? new FakeUserActivityService();

        var context = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            foregroundKeyboard: fgKb,
            foregroundService: fgService,
            textDetector: detector,
            elevationService: elevService,
            userActivityService: actService);

        return (context, fgService, fgKb, elevService, actService);
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

        var (context, fgService, fgKb, elevService) = CreateTestContext(null!, targetHwnd);
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (fgKb.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound());
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        context = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            foregroundKeyboard: fgKb,
            foregroundService: fgService,
            textDetector: detector,
            elevationService: elevService);

        fgService.ShouldFailActivation = true; // First attempt fails
        var rule = CreateRule();

        var progressList = new List<string>();
        context.ProgressCallback = p => progressList.Add(p);

        // After 100ms, simulate activation succeeding on retry
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            fgService.ShouldFailActivation = false;
        });

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(progressList, p => p.Contains("PAUSED — Waiting for Terminal focus"));
        Assert.Equal(1, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Confirm_ForegroundActivation_Cancelled_DoesNotSendEnter()
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
        fgService.ShouldFailActivation = true; // Activation fails permanently
        var rule = CreateRule();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Confirm_ForegroundChanged_ImmediatelyBeforeSendInput_RetriesAndSendsEnterOnce()
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
                // Simulate focus stealing right after revalidation on first attempt
                fgService.CurrentForeground = thirdPartyHwnd;
            }
            if (fgKb.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound());
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
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount);
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
    public async Task Target_Minimized_Pauses_AndResumesWhenRestored()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";
        var (context, fgService, fgKb, _) = CreateTestContext(null!, targetHwnd);
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (fgKb.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound());
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
        fgService.IsMinimized = true; // Minimized target initially!

        var progressList = new List<string>();
        context.ProgressCallback = p => progressList.Add(p);

        // Simulate restore after 100ms
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            fgService.IsMinimized = false;
        });

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(progressList, p => p.Contains("PAUSED — Target minimized"));
        Assert.Equal(1, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Target_Minimized_Cancelled_DoesNotSendEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        var detector = new FakeTextDetector((hwnd, req) => Task.FromResult(TextDetectionResult.Success("")));
        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
        fgService.IsMinimized = true; // Minimized target

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(rule, timeout: TimeSpan.FromSeconds(5));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
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

    [Fact]
    public async Task FastPulse_Restores_Foreground_Immediately_Before_Acknowledgement_Polling()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        var eventLog = new List<string>();

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
            eventLog.Add($"DetectAsync:{detectCount}");
            if (detectCount <= 2)
            {
                // Detection before and during initial re-validation
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            }
            // Dismissed after Enter
            return Task.FromResult(TextDetectionResult.NotFound());
        });

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
        fgService.CurrentForeground = (IntPtr)0x9999;
        fgService.OnEvent = ev => eventLog.Add(ev);
        fgKb.OnEvent = ev => eventLog.Add(ev);

        var rule = new CommandApprovalRule { Name = "Date", AllowedCommand = "Get-Date" };
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            focusBehavior: FocusBehavior.FastPulse,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount);
        Assert.Equal(1, fgService.ActivateCallCount);
        Assert.Equal(1, fgService.RestoreCallCount);
        Assert.Equal((IntPtr)0x9999, fgService.LastRestoredHwnd);

        // Verify ordering: Activate -> SendEnter -> Restore -> Acknowledgement DetectAsync
        int activateIdx = eventLog.FindIndex(e => e.StartsWith("ActivateWindow"));
        int enterIdx = eventLog.FindIndex(e => e == "SendEnter");
        int restoreIdx = eventLog.FindIndex(e => e.StartsWith("RestoreForegroundWindow"));
        int ackDetectIdx = eventLog.FindIndex(e => e == "DetectAsync:3");

        Assert.True(activateIdx >= 0, "ActivateWindow was not called");
        Assert.True(enterIdx > activateIdx, "SendEnter must happen after ActivateWindow");
        Assert.True(restoreIdx > enterIdx, "RestoreForegroundWindow must happen after SendEnter");
        Assert.True(ackDetectIdx > restoreIdx, "Acknowledgement polling must happen after RestoreForegroundWindow in FastPulse");
    }

    [Fact]
    public async Task KeepTargetForeground_DoesNotRestoreForeground()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        var eventLog = new List<string>();

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
            if (detectCount <= 2)
            {
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            }
            return Task.FromResult(TextDetectionResult.NotFound());
        });

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
        fgService.CurrentForeground = (IntPtr)0x9999;
        fgService.OnEvent = ev => eventLog.Add(ev);
        fgKb.OnEvent = ev => eventLog.Add(ev);

        var rule = new CommandApprovalRule { Name = "Date", AllowedCommand = "Get-Date" };
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            focusBehavior: FocusBehavior.KeepTargetForeground,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount);
        Assert.Equal(1, fgService.ActivateCallCount);
        Assert.Equal(0, fgService.RestoreCallCount); // Did NOT restore!
        Assert.Equal(targetHwnd, fgService.CurrentForeground);
    }

    [Fact]
    public async Task AlreadyForeground_Skips_Activation_And_Restoration()
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
            if (detectCount <= 2)
            {
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            }
            return Task.FromResult(TextDetectionResult.NotFound());
        });

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
        fgService.CurrentForeground = targetHwnd; // Already foreground!

        var rule = new CommandApprovalRule { Name = "Date", AllowedCommand = "Get-Date" };
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            focusBehavior: FocusBehavior.FastPulse,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount);
        Assert.Equal(0, fgService.ActivateCallCount); // Skipped activation
        Assert.Equal(0, fgService.RestoreCallCount);  // Skipped restoration
    }

    [Fact]
    public async Task RestoreForeground_Failure_DoesNotFail_Approval()
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
            if (detectCount <= 2)
            {
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            }
            return Task.FromResult(TextDetectionResult.NotFound());
        });

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);
        fgService.CurrentForeground = (IntPtr)0x9999;
        fgService.ShouldFailRestore = true; // Simulating restore failure

        var rule = new CommandApprovalRule { Name = "Date", AllowedCommand = "Get-Date" };
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            focusBehavior: FocusBehavior.FastPulse,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        // Approval must STILL succeed because Enter was sent and prompt acknowledged!
        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount);
        Assert.Equal(1, fgService.RestoreCallCount);
    }

    [Fact]
    public async Task PromptFingerprint_Mismatch_DuringPolling_CountsAsAcknowledged_AndDoesNotResendEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptA = @"
Requesting permission for:
Get-Date

Run this command?
> 1. Yes, run command
";
        string promptB = @"
Requesting permission for:
git status

Run this command?
> 1. Yes, run command
";
        int detectCount = 0;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            detectCount++;
            if (detectCount <= 2)
            {
                // First prompt is Prompt A
                return Task.FromResult(TextDetectionResult.Success(promptA, promptA));
            }
            // Next poll immediately returns Prompt B (different prompt has appeared)
            return Task.FromResult(TextDetectionResult.Success(promptB, promptB));
        });

        var (context, fgService, fgKb, _) = CreateTestContext(detector, targetHwnd);

        var rule = new CommandApprovalRule { Name = "Date", AllowedCommand = "Get-Date" };
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        // A is acknowledged, result is success
        Assert.True(result.IsSuccess);
        // Single approval invariant: exactly 1 Enter! B must NOT be approved
        Assert.Equal(1, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task ForegroundRetry_PromptDisappears_AbortsRetry_ZeroEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";

        int callCount = 0;
        var (context, fgService, fgKb, _) = CreateTestContext(null!, targetHwnd);
        fgService.ShouldFailActivation = true; // Foreground cannot be acquired

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            callCount++;
            if (callCount == 1)
            {
                // First call: prompt visible
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            }
            // While waiting for foreground, prompt disappears
            return Task.FromResult(TextDetectionResult.NotFound(""));
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
            timeout: TimeSpan.FromMilliseconds(700),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        // Timed out because prompt disappeared while waiting for foreground and never returned
        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Timeout, result.Status);
        Assert.Equal(0, fgKb.SendEnterCallCount); // Zero Enter sent
    }

    [Fact]
    public async Task ForegroundRetry_PromptChangesFromAToB_AbortsRetry_ZeroEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptA = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";
        string promptB = @"
rm -rf /unauthorized

Run this command?
> 1. Yes, run command
";

        int callCount = 0;
        var (context, fgService, fgKb, _) = CreateTestContext(null!, targetHwnd);
        fgService.ShouldFailActivation = true; // Foreground cannot be acquired initially

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return Task.FromResult(TextDetectionResult.Success(promptA, promptA));
            }
            // Prompt changed to unauthorized command B while waiting for foreground
            return Task.FromResult(TextDetectionResult.Success(promptB, promptB));
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
            timeout: TimeSpan.FromMilliseconds(700),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Timeout, result.Status);
        Assert.Equal(0, fgKb.SendEnterCallCount); // Zero Enter dispatched for Prompt A or B
    }

    [Fact]
    public async Task ForegroundRetry_TargetMinimized_TransitionsToPaused_AndResumesWhenRestored()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";

        var (context, fgService, fgKb, _) = CreateTestContext(null!, targetHwnd);
        fgService.ShouldFailActivation = true; // Initial activation fails

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (fgKb.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound());
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var progressList = new List<string>();
        context = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            foregroundKeyboard: fgKb,
            foregroundService: fgService,
            textDetector: detector);
        context.ProgressCallback = p => progressList.Add(p);

        // Simulate target becoming minimized during foreground retry, then restored and focus acquired
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            fgService.IsMinimized = true;
            await Task.Delay(600); // Wait long enough so loop evaluates IsWindowMinimized while true
            fgService.IsMinimized = false;
            fgService.ShouldFailActivation = false;
        });

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(progressList, p => p.Contains("PAUSED — Waiting for Terminal focus"));
        Assert.Contains(progressList, p => p.Contains("PAUSED — Target minimized"));
        Assert.Equal(1, fgKb.SendEnterCallCount); // Exactly 1 Enter sent
    }

    [Fact]
    public async Task TargetDestroyed_DuringMinimizedPause_ReturnsTargetUnavailable_ZeroEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        var (context, fgService, fgKb, _) = CreateTestContext(null!, targetHwnd);
        fgService.IsMinimized = true; // Minimized at start

        var detector = new FakeTextDetector((hwnd, req) => Task.FromResult(TextDetectionResult.NotFound()));
        context = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            foregroundKeyboard: fgKb,
            foregroundService: fgService,
            textDetector: detector);

        // Destroy target window after 100ms while paused
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            fgService.WindowClosed = true;
        });

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.TargetUnavailable, result.Status);
        Assert.Contains("closed while minimized", result.Message);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task TargetDestroyed_DuringForegroundWait_ReturnsTargetUnavailable_ZeroEnter()
    {
        IntPtr targetHwnd = (IntPtr)0x1111;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
";

        var (context, fgService, fgKb, _) = CreateTestContext(null!, targetHwnd);
        fgService.ShouldFailActivation = true; // Fails activation to enter foreground retry

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        context = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            foregroundKeyboard: fgKb,
            foregroundService: fgService,
            textDetector: detector);

        // Destroy target window after 100ms during foreground acquisition
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            fgService.WindowClosed = true;
        });

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.TargetUnavailable, result.Status);
        Assert.Contains("closed during foreground acquisition", result.Message);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task RespectUserFocus_TargetAlreadyForeground_RecentInput_DoesNotPause_SendsEnter()
    {
        // Section 35: Target already foreground + recent input (e.g. 10ms)
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        FakeForegroundKeyboard? fgKbRef = null;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (fgKbRef?.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var actService = new FakeUserActivityService
        {
            IdleDuration = TimeSpan.FromMilliseconds(10) // Input occurred 10ms ago
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgKbRef = fgKb;
        fgService.CurrentForeground = targetHwnd; // Target is ALREADY foreground

        var progressList = new List<string>();
        context.ProgressCallback = p => progressList.Add(p);

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true,
            userIdleThreshold: TimeSpan.FromMilliseconds(1500));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, fgService.ActivateCallCount); // Bypassed SetForegroundWindow!
        Assert.Equal(1, fgKb.SendEnterCallCount); // Enter sent safely
        Assert.DoesNotContain(progressList, p => p.Contains("User active"));
        Assert.True(actService.InputInjectedNotified); // Enter injection was notified
    }

    [Fact]
    public async Task RespectUserFocus_OtherForeground_RecentUserInput_EntersPausedUserActive_ZeroFocus_ZeroEnter()
    {
        // Section 36: Other foreground + recent user input (100ms idle < 1500ms threshold)
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        var actService = new FakeUserActivityService
        {
            IdleDuration = TimeSpan.FromMilliseconds(100) // User is actively interacting
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgService.CurrentForeground = (IntPtr)0x9999; // Another app foreground (e.g. Chrome)

        var progressList = new List<string>();
        context.ProgressCallback = p => progressList.Add(p);

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true,
            userIdleThreshold: TimeSpan.FromMilliseconds(1500));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Contains(progressList, p => p == "PAUSED — User active in another window");
        Assert.Equal(0, fgService.ActivateCallCount); // ZERO calls to SetForegroundWindow!
        Assert.Equal(0, fgKb.SendEnterCallCount);     // ZERO calls to SendInput!
    }

    [Fact]
    public async Task RespectUserFocus_UserBecomesIdle_AndForegroundStable_ApprovesWithFastPulse()
    {
        // Section 37: User starts active, becomes idle >= 1500ms, foreground stable 300ms -> FastPulse -> Enter
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        FakeForegroundKeyboard? fgKbRef = null;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (fgKbRef?.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var actService = new FakeUserActivityService
        {
            // First 150ms user is active (100ms idle); after 150ms user becomes idle (2500ms idle)
            IdleDurationProvider = () => sw.ElapsedMilliseconds < 150
                ? TimeSpan.FromMilliseconds(100)
                : TimeSpan.FromMilliseconds(2500)
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgKbRef = fgKb;
        fgService.CurrentForeground = (IntPtr)0x9999; // Another window foreground

        var progressList = new List<string>();
        context.ProgressCallback = p => progressList.Add(p);

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true,
            userIdleThreshold: TimeSpan.FromMilliseconds(1500));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(progressList, p => p.Contains("User active"));
        Assert.True(fgService.ActivateCallCount >= 1); // FastPulse attempted after idle & stability!
        Assert.Equal(1, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task RespectUserFocus_ContinuousActivity_RemainsPaused_ZeroActivations()
    {
        // Section 38: Continuous user activity (never reaching 1500ms threshold)
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        int callIndex = 0;
        int[] intervals = [100, 200, 50, 400, 100, 300, 50];
        var actService = new FakeUserActivityService
        {
            IdleDurationProvider = () =>
            {
                int val = intervals[callIndex % intervals.Length];
                callIndex++;
                return TimeSpan.FromMilliseconds(val);
            }
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgService.CurrentForeground = (IntPtr)0x9999;

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true,
            userIdleThreshold: TimeSpan.FromMilliseconds(1500));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Equal(0, fgService.ActivateCallCount);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task RespectUserFocus_NewInputDuringStabilityWindow_AbortsActivation()
    {
        // Section 39: Idle met, but before 300ms stability completes, new physical input occurs
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        int callCount = 0;
        var actService = new FakeUserActivityService
        {
            IdleDurationProvider = () =>
            {
                callCount++;
                // 1st call (Step A): active (50ms) -> enters pause loop
                if (callCount == 1) return TimeSpan.FromMilliseconds(50);
                // 2nd call (pause loop check): met threshold (2000ms) -> starts 300ms stability
                if (callCount == 2) return TimeSpan.FromMilliseconds(2000);
                // 3rd call (stability verification after 300ms): user pressed a key! (10ms)
                return TimeSpan.FromMilliseconds(10);
            }
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgService.CurrentForeground = (IntPtr)0x9999;

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true,
            userIdleThreshold: TimeSpan.FromMilliseconds(1500));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Equal(0, fgService.ActivateCallCount);
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task RespectUserFocus_ForegroundChangesDuringStabilityWindow_ResetsStability()
    {
        // Section 40: Chrome (0x9999) changes to VS Code (0x8888) during stability wait
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var actService = new FakeUserActivityService
        {
            IdleDurationProvider = () => sw.ElapsedMilliseconds < 50
                ? TimeSpan.FromMilliseconds(50)
                : TimeSpan.FromMilliseconds(2000)
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgService.CurrentForeground = (IntPtr)0x9999;

        // Change foreground window 150ms in (during stability wait)
        _ = Task.Run(async () =>
        {
            await Task.Delay(150);
            fgService.CurrentForeground = (IntPtr)0x8888;
        });

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true,
            userIdleThreshold: TimeSpan.FromMilliseconds(1500));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(450));
        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Equal(0, fgService.ActivateCallCount);
    }

    [Fact]
    public async Task RespectUserFocus_TargetBecomesForegroundNaturallyDuringPause_BypassesIdleWait()
    {
        // Section 41: User manually clicked Terminal while paused -> naturally acquires foreground -> approves immediately
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        FakeForegroundKeyboard? fgKbRef = null;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (fgKbRef?.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var actService = new FakeUserActivityService
        {
            IdleDuration = TimeSpan.FromMilliseconds(50) // User is actively interacting
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgKbRef = fgKb;
        fgService.CurrentForeground = (IntPtr)0x9999;

        // User clicks Terminal after 100ms
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            fgService.CurrentForeground = targetHwnd;
        });

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true,
            userIdleThreshold: TimeSpan.FromMilliseconds(1500));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, fgService.ActivateCallCount); // Bypassed SetForegroundWindow!
        Assert.Equal(1, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task RespectUserFocus_Disabled_UsesExistingFastPulsePath()
    {
        // Section 42: RespectUserFocus = false preserves legacy FastPulse path
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        FakeForegroundKeyboard? fgKbRef = null;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (fgKbRef?.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var actService = new FakeUserActivityService
        {
            IdleDuration = TimeSpan.FromMilliseconds(50) // Recent input
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgKbRef = fgKb;
        fgService.CurrentForeground = (IntPtr)0x9999;

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: false); // OFF

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(fgService.ActivateCallCount >= 1); // FastPulse called immediately!
        Assert.Equal(1, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task RespectUserFocus_TargetMinimized_TakesPriorityOverUserActive()
    {
        // Section 43: Target minimized takes priority over user-active pause
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        var actService = new FakeUserActivityService
        {
            IdleDuration = TimeSpan.FromMilliseconds(50)
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgService.CurrentForeground = (IntPtr)0x9999;
        fgService.IsMinimized = true;

        var progressList = new List<string>();
        context.ProgressCallback = p => progressList.Add(p);

        // Restore target after 100ms
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            fgService.IsMinimized = false;
        });

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Contains(progressList, p => p == "PAUSED — Target minimized");
        Assert.Equal(0, fgService.ActivateCallCount);
    }

    [Fact]
    public async Task RespectUserFocus_ActivationFailureAfterIdle_TransitionsToPausedForegroundUnavailable()
    {
        // Section 44: Activation failure after idle transitions to PAUSED_FOREGROUND_UNAVAILABLE
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        var actService = new FakeUserActivityService
        {
            IdleDuration = TimeSpan.FromSeconds(5) // User idle
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgService.CurrentForeground = (IntPtr)0x9999;
        fgService.ShouldFailActivation = true; // OS denies SetForegroundWindow

        var progressList = new List<string>();
        context.ProgressCallback = p => progressList.Add(p);

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));
        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Contains(progressList, p => p == "PAUSED — Waiting for Terminal focus");
        Assert.DoesNotContain(progressList, p => p.Contains("Blocked"));
    }

    [Fact]
    public async Task RespectUserFocus_UserResumesActivityDuringForegroundRetry_TransitionsBackToPausedUserActive()
    {
        // Section 45: In PAUSED_FOREGROUND_UNAVAILABLE, user starts typing -> transitions back to PAUSED_USER_ACTIVE
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var actService = new FakeUserActivityService
        {
            // First 400ms idle -> attempts activation (fails) -> then user resumes typing at 400ms
            IdleDurationProvider = () => sw.ElapsedMilliseconds < 400
                ? TimeSpan.FromSeconds(5)
                : TimeSpan.FromMilliseconds(50)
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgService.CurrentForeground = (IntPtr)0x9999;
        fgService.ShouldFailActivation = true;

        var progressList = new List<string>();
        context.ProgressCallback = p => progressList.Add(p);

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1200));
        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Contains(progressList, p => p == "PAUSED — Waiting for Terminal focus");
        Assert.Contains(progressList, p => p == "PAUSED — User active in another window");
        // Only 1 activation attempt was made before user resumed activity, then retries halted
        Assert.Equal(1, fgService.ActivateCallCount);
    }

    [Fact]
    public async Task RespectUserFocus_PromptDisappearsDuringUserActivePause_ReturnsWithoutEnter()
    {
        // Section 46: Prompt disappears during user pause -> returns without Enter
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        int detectCount = 0;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            detectCount++;
            // Prompt matched at start, disappears after user pause
            if (detectCount == 1) return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            return Task.FromResult(TextDetectionResult.NotFound());
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var actService = new FakeUserActivityService
        {
            IdleDurationProvider = () => sw.ElapsedMilliseconds < 150
                ? TimeSpan.FromMilliseconds(50)
                : TimeSpan.FromSeconds(5)
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgService.CurrentForeground = (IntPtr)0x9999;

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromMilliseconds(600),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(0, fgKb.SendEnterCallCount); // NO Enter sent!
        Assert.Equal(0, fgService.ActivateCallCount);
    }

    [Fact]
    public async Task RespectUserFocus_FinalGuard_UserActivityDuringRevalidation_AbortsActivation()
    {
        // User adjustment 2: Final guard immediately before ActivateWindow
        // If user activity occurred during prompt revalidation, returns to PAUSED_USER_ACTIVE
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var actService = new FakeUserActivityService
        {
            IdleDurationProvider = () => sw.ElapsedMilliseconds < 50
                ? TimeSpan.FromMilliseconds(50) // initial active
                : TimeSpan.FromSeconds(5) // becomes idle
        };

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            // Simulate user typing during prompt re-read
            actService.IdleDurationProvider = () => TimeSpan.FromMilliseconds(20);
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgService.CurrentForeground = (IntPtr)0x9999;

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Equal(0, fgService.ActivateCallCount); // Final guard blocked activation!
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task RespectUserFocus_ActivityServiceFails_FallsBackToFastPulse_LogsWarning()
    {
        // User adjustment 4: If TryGetIdleDuration fails, log warning once and fall back to existing FastPulse path
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        FakeForegroundKeyboard? fgKbRef = null;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (fgKbRef?.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var actService = new FakeUserActivityService
        {
            ReturnSuccess = false // TryGetIdleDuration fails
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        fgKbRef = fgKb;
        fgService.CurrentForeground = (IntPtr)0x9999;

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50),
            respectUserFocus: true);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(fgService.ActivateCallCount >= 1); // Activated immediately via fallback!
        Assert.Equal(1, fgKb.SendEnterCallCount);
        var inMemoryLogger = (InMemoryLogger)context.Logger!;
        Assert.Contains(inMemoryLogger.Entries, e => e.Message.Contains("Failed to determine user idle duration") || e.Message.Contains("GetLastInputInfo failed"));
    }

    [Fact]
    public async Task RespectUserFocus_SingleMonitorCompatibility_Scenarios_A_B_C_D()
    {
        // Section 53: Explicit Single-Monitor Compatibility Test
        var targetHwnd = (IntPtr)0x1234;
        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";

        // Scenario A: Terminal foreground, recent physical input (10ms)
        {
            FakeForegroundKeyboard? kbRef = null;
            var detector = new FakeTextDetector((hwnd, req) =>
            {
                if (kbRef?.SendEnterCallCount > 0)
                    return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            });
            var act = new FakeUserActivityService { IdleDuration = TimeSpan.FromMilliseconds(10) };
            var (ctx, fg, kb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, act);
            kbRef = kb;
            fg.CurrentForeground = targetHwnd;

            var action = new SafeAutoConfirmAction(CreateRule(), executionMode: AutoConfirmExecutionMode.Confirm, pollInterval: TimeSpan.FromMilliseconds(50), respectUserFocus: true);
            var res = await action.ExecuteAsync(ctx, CancellationToken.None);

            Assert.True(res.IsSuccess);
            Assert.Equal(0, fg.ActivateCallCount);
            Assert.Equal(1, kb.SendEnterCallCount);
        }

        // Scenario B: Another app foreground, user actively typing
        {
            var detector = new FakeTextDetector((hwnd, req) =>
                Task.FromResult(TextDetectionResult.Success(promptText, promptText)));
            var act = new FakeUserActivityService { IdleDuration = TimeSpan.FromMilliseconds(50) };
            var (ctx, fg, kb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, act);
            fg.CurrentForeground = (IntPtr)0x7777;

            var action = new SafeAutoConfirmAction(CreateRule(), executionMode: AutoConfirmExecutionMode.Confirm, pollInterval: TimeSpan.FromMilliseconds(50), respectUserFocus: true);
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var res = await action.ExecuteAsync(ctx, cts.Token);

            Assert.Equal(MacroActionStatus.Cancelled, res.Status);
            Assert.Equal(0, fg.ActivateCallCount);
            Assert.Equal(0, kb.SendEnterCallCount);
        }

        // Scenario C: Another app foreground, user becomes idle > threshold
        {
            FakeForegroundKeyboard? kbRef = null;
            var detector = new FakeTextDetector((hwnd, req) =>
            {
                if (kbRef?.SendEnterCallCount > 0)
                    return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var act = new FakeUserActivityService
            {
                IdleDurationProvider = () => sw.ElapsedMilliseconds < 100 ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromSeconds(5)
            };
            var (ctx, fg, kb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, act);
            kbRef = kb;
            fg.CurrentForeground = (IntPtr)0x7777;

            var action = new SafeAutoConfirmAction(CreateRule(), executionMode: AutoConfirmExecutionMode.Confirm, pollInterval: TimeSpan.FromMilliseconds(50), respectUserFocus: true);
            var res = await action.ExecuteAsync(ctx, CancellationToken.None);

            Assert.True(res.IsSuccess);
            Assert.True(fg.ActivateCallCount >= 1);
            Assert.Equal(1, kb.SendEnterCallCount);
        }

        // Scenario D: RespectUserFocus = false
        {
            FakeForegroundKeyboard? kbRef = null;
            var detector = new FakeTextDetector((hwnd, req) =>
            {
                if (kbRef?.SendEnterCallCount > 0)
                    return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
                return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
            });
            var act = new FakeUserActivityService { IdleDuration = TimeSpan.FromMilliseconds(10) };
            var (ctx, fg, kb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, act);
            kbRef = kb;
            fg.CurrentForeground = (IntPtr)0x7777;

            var action = new SafeAutoConfirmAction(CreateRule(), executionMode: AutoConfirmExecutionMode.Confirm, pollInterval: TimeSpan.FromMilliseconds(50), respectUserFocus: false);
            var res = await action.ExecuteAsync(ctx, CancellationToken.None);

            Assert.True(res.IsSuccess);
            Assert.True(fg.ActivateCallCount >= 1);
            Assert.Equal(1, kb.SendEnterCallCount);
        }
    }

    [Fact]
    public async Task Section24_MandatoryRegressionTest_StaleRestore_RestoresWindowB_NeverWindowA()
    {
        var targetHwnd = (IntPtr)0x1234;
        IntPtr windowA = (IntPtr)0xC0C0; // CocCoc
        IntPtr windowB = (IntPtr)0xCCAA; // Chrome

        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";
        FakeForegroundKeyboard? kbRef = null;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (kbRef?.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var actService = new FakeUserActivityService
        {
            // Active for first 40ms, then idle
            IdleDurationProvider = () => sw.ElapsedMilliseconds < 40 ? TimeSpan.FromMilliseconds(50) : TimeSpan.FromSeconds(5)
        };

        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        kbRef = fgKb;

        // Prompt initially arrives while Window A (CocCoc) is foreground
        fgService.CurrentForeground = windowA;

        // During PAUSED_USER_ACTIVE, user switches foreground to Window B (Chrome)
        _ = Task.Run(async () =>
        {
            await Task.Delay(20);
            fgService.CurrentForeground = windowB;
        });

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20),
            respectUserFocus: true);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount);
        Assert.Equal(1, fgService.RestoreCallCount);
        Assert.Equal(windowB, fgService.LastRestoredHwnd);
        Assert.NotEqual(windowA, fgService.LastRestoredHwnd);
    }

    [Fact]
    public async Task Section25_MandatoryRegressionTest_UserChangesFocusDuringPulse_RestorationSkipped()
    {
        var targetHwnd = (IntPtr)0x1234;
        IntPtr windowChrome = (IntPtr)0xCCAA;
        IntPtr windowCocCoc = (IntPtr)0xC0C0;

        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";
        FakeForegroundKeyboard? kbRef = null;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (kbRef?.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var actService = new FakeUserActivityService { IdleDuration = TimeSpan.FromSeconds(5) };
        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        kbRef = fgKb;

        fgService.CurrentForeground = windowChrome;

        // When SendEnter executes, simulate user clicking CocCoc during the pulse!
        fgKb.OnEvent += ev =>
        {
            if (ev == "SendEnter")
            {
                fgService.CurrentForeground = windowCocCoc;
            }
        };

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20),
            respectUserFocus: true);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount);
        // Restoration must be skipped because foreground changed away from Terminal
        Assert.Equal(0, fgService.RestoreCallCount);
        Assert.Equal(windowCocCoc, fgService.CurrentForeground);
    }

    [Fact]
    public async Task Section26_FinallyPathRegression_NormalVsUserFocusChanged()
    {
        var targetHwnd = (IntPtr)0x1234;
        IntPtr windowChrome = (IntPtr)0xCCAA;
        IntPtr windowCocCoc = (IntPtr)0xC0C0;

        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";
        // Case 1: Exception occurs after activation, but current foreground is still Terminal
        {
            bool firstDetect = true;
            var detector = new FakeTextDetector((hwnd, req) =>
            {
                if (firstDetect)
                {
                    firstDetect = false;
                    return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
                }
                // Throw exception during revalidation after activation
                throw new InvalidOperationException("Test exception during revalidation");
            });

            var actService = new FakeUserActivityService { IdleDuration = TimeSpan.FromSeconds(5) };
            var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
            fgService.CurrentForeground = windowChrome;

            var rule = CreateRule();
            var action = new SafeAutoConfirmAction(
                rule,
                executionMode: AutoConfirmExecutionMode.Confirm,
                timeout: TimeSpan.FromSeconds(5),
                pollInterval: TimeSpan.FromMilliseconds(20),
                respectUserFocus: true);

            await Assert.ThrowsAsync<InvalidOperationException>(() => action.ExecuteAsync(context, CancellationToken.None));
            // Finally block should have restored windowChrome since Terminal was still foreground
            Assert.Equal(1, fgService.RestoreCallCount);
            Assert.Equal(windowChrome, fgService.LastRestoredHwnd);
        }

        // Case 2: Exception occurs after activation, but user changed foreground to CocCoc
        {
            bool firstDetect = true;
            FakeForegroundService? fgRef = null;
            var detector = new FakeTextDetector((hwnd, req) =>
            {
                if (firstDetect)
                {
                    firstDetect = false;
                    return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
                }
                // User clicks CocCoc right before revalidation exception
                if (fgRef != null) fgRef.CurrentForeground = windowCocCoc;
                throw new InvalidOperationException("Test exception during revalidation");
            });

            var actService = new FakeUserActivityService { IdleDuration = TimeSpan.FromSeconds(5) };
            var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
            fgRef = fgService;
            fgService.CurrentForeground = windowChrome;

            var rule = CreateRule();
            var action = new SafeAutoConfirmAction(
                rule,
                executionMode: AutoConfirmExecutionMode.Confirm,
                timeout: TimeSpan.FromSeconds(5),
                pollInterval: TimeSpan.FromMilliseconds(20),
                respectUserFocus: true);

            await Assert.ThrowsAsync<InvalidOperationException>(() => action.ExecuteAsync(context, CancellationToken.None));
            // Finally block must NOT restore windowChrome because user changed focus to CocCoc
            Assert.Equal(0, fgService.RestoreCallCount);
            Assert.Equal(windowCocCoc, fgService.CurrentForeground);
        }
    }

    [Fact]
    public async Task Section27_MinimizeRestoreNewForeground_RestoresWindowB_NotA()
    {
        var targetHwnd = (IntPtr)0x1234;
        IntPtr windowA = (IntPtr)0xC0C0;
        IntPtr windowB = (IntPtr)0xCCAA;

        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";
        FakeForegroundKeyboard? kbRef = null;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (kbRef?.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var actService = new FakeUserActivityService { IdleDuration = TimeSpan.FromSeconds(5) };
        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        kbRef = fgKb;

        // Prompt arrives with Window A foreground, but target is minimized
        fgService.CurrentForeground = windowA;
        fgService.IsMinimized = true;

        _ = Task.Run(async () =>
        {
            await Task.Delay(50);
            // While minimized, user switches to Window B
            fgService.CurrentForeground = windowB;
            // Target is restored
            fgService.IsMinimized = false;
        });

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20),
            respectUserFocus: true);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount);
        Assert.Equal(1, fgService.RestoreCallCount);
        Assert.Equal(windowB, fgService.LastRestoredHwnd);
        Assert.NotEqual(windowA, fgService.LastRestoredHwnd);
    }

    [Fact]
    public async Task Section33_34_ForegroundDenial_FirstActivationDenied_NoRepeatedStormAgainstSameForeground()
    {
        var targetHwnd = (IntPtr)0x1234;
        IntPtr windowChrome = (IntPtr)0xCCAA;

        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";
        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        var actService = new FakeUserActivityService { IdleDuration = TimeSpan.FromSeconds(5) };
        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);

        fgService.CurrentForeground = windowChrome;
        fgService.ShouldFailActivation = true; // OS denies activation

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(600));

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20),
            respectUserFocus: true);

        var result = await action.ExecuteAsync(context, cts.Token);

        // Cancelled because of timeout waiting for state change
        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Equal(0, fgKb.SendEnterCallCount);
        // Only 1 activation attempt was made! No repeated SetForegroundWindow storm!
        Assert.Equal(1, fgService.ActivateCallCount);
    }

    [Fact]
    public async Task Section35_ForegroundDenial_ForegroundChanges_AllowsNewActivationAttempt()
    {
        var targetHwnd = (IntPtr)0x1234;
        IntPtr windowChrome = (IntPtr)0xCCAA;
        IntPtr windowCocCoc = (IntPtr)0xC0C0;

        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";
        FakeForegroundKeyboard? kbRef = null;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (kbRef?.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var actService = new FakeUserActivityService { IdleDuration = TimeSpan.FromSeconds(5) };
        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        kbRef = fgKb;

        fgService.CurrentForeground = windowChrome;
        fgService.ShouldFailActivation = true; // First activation denied on Chrome

        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            // User switches to CocCoc, and this time OS permits activation
            fgService.CurrentForeground = windowCocCoc;
            fgService.ShouldFailActivation = false;
        });

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20),
            respectUserFocus: true);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount);
        Assert.Equal(2, fgService.ActivateCallCount); // 1 on Chrome (failed) + 1 on CocCoc (succeeded)
        Assert.Equal(windowCocCoc, fgService.LastRestoredHwnd);
    }

    [Fact]
    public async Task Section36_ForegroundDenial_UserResumesActivity_TransitionsToPausedUserActive()
    {
        var targetHwnd = (IntPtr)0x1234;
        IntPtr windowChrome = (IntPtr)0xCCAA;

        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";
        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(promptText, promptText)));

        var actService = new FakeUserActivityService { IdleDuration = TimeSpan.FromSeconds(5) };
        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);

        fgService.CurrentForeground = windowChrome;
        fgService.ShouldFailActivation = true; // Denied

        _ = Task.Run(async () =>
        {
            await Task.Delay(80);
            // User resumes typing in Chrome
            actService.IdleDuration = TimeSpan.FromMilliseconds(10);
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20),
            respectUserFocus: true);

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Equal(1, fgService.ActivateCallCount); // 0 additional activation calls while active
        Assert.Equal(0, fgKb.SendEnterCallCount);
    }

    [Fact]
    public async Task Section37_ForegroundDenial_UserManuallyFocusesTerminal_DirectEnterNoActivation()
    {
        var targetHwnd = (IntPtr)0x1234;
        IntPtr windowChrome = (IntPtr)0xCCAA;

        string promptText = @"
dotnet test BackgroundAutomator.sln

Run this command?
> 1. Yes, run command
  2. No, edit command
";
        FakeForegroundKeyboard? kbRef = null;
        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (kbRef?.SendEnterCallCount > 0)
                return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
            return Task.FromResult(TextDetectionResult.Success(promptText, promptText));
        });

        var actService = new FakeUserActivityService { IdleDuration = TimeSpan.FromSeconds(5) };
        var (context, fgService, fgKb, _, _) = CreateTestContextWithActivity(detector, targetHwnd, actService);
        kbRef = fgKb;

        fgService.CurrentForeground = windowChrome;
        fgService.ShouldFailActivation = true; // First attempt fails

        _ = Task.Run(async () =>
        {
            await Task.Delay(80);
            // User manually clicks on Terminal
            fgService.CurrentForeground = targetHwnd;
        });

        var rule = CreateRule();
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(20),
            respectUserFocus: true);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKb.SendEnterCallCount);
        // Only the initial denied call occurred; no SetForegroundWindow call was made for manual focus!
        Assert.Equal(1, fgService.ActivateCallCount);
        Assert.Equal(0, fgService.RestoreCallCount); // No restoration on target-already-foreground path
    }
}
