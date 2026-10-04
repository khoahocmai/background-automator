using System.Drawing;
using System.Windows;
using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Coordinates;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Security;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Core.TextDetection;
using BackgroundAutomator.Core.UserActivity;
using Xunit;

namespace BackgroundAutomator.Tests;

public class TerminalSplitPaneAwarenessTests
{
    private class FakeForegroundService : IWindowForegroundService
    {
        public IntPtr CurrentForeground { get; set; }
        public bool ActivateResult { get; set; } = true;
        public bool RestoreResult { get; set; } = true;
        public int ActivateCallCount { get; private set; }
        public int RestoreCallCount { get; private set; }
        public List<IntPtr> RestoredHwnds { get; } = new();

        public IntPtr GetForegroundWindow() => CurrentForeground;

        public bool ActivateWindow(IntPtr hWnd)
        {
            ActivateCallCount++;
            if (ActivateResult)
            {
                CurrentForeground = hWnd;
                return true;
            }
            return false;
        }

        public bool RestoreForegroundWindow(IntPtr hWnd)
        {
            RestoreCallCount++;
            RestoredHwnds.Add(hWnd);
            if (RestoreResult)
            {
                CurrentForeground = hWnd;
                return true;
            }
            return false;
        }

        public bool IsWindowMinimized(IntPtr hWnd) => false;
        public bool IsWindow(IntPtr hWnd) => hWnd != IntPtr.Zero;
        public bool IsWindowVisible(IntPtr hWnd) => true;
        public string GetProcessName(IntPtr hWnd) => "WindowsTerminal";
        public int GetProcessId(IntPtr hWnd) => 1234;
        public string GetWindowClass(IntPtr hWnd) => "CASCADIA_HOSTING_WINDOW_CLASS";
        public IntPtr GetRootWindow(IntPtr hWnd) => hWnd;
    }

    private class FakeForegroundKeyboard : IForegroundKeyboard
    {
        public int SendEnterCallCount { get; private set; }
        public bool SendEnterResult { get; set; } = true;
        public Action? OnEnterSent { get; set; }

        public bool SendEnter()
        {
            SendEnterCallCount++;
            OnEnterSent?.Invoke();
            return SendEnterResult;
        }
    }

    private class FakeElevationService : ProcessElevationService
    {
        public override ElevationCheckResult CheckCompatibility(int targetProcessId) =>
            ElevationCheckResult.CreateCompatible(false, false);
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

    private class FakeUserActivity : IUserActivityService
    {
        public bool TryGetIdleDuration(out TimeSpan idleDuration)
        {
            idleDuration = TimeSpan.FromSeconds(10);
            return true;
        }

        public void NotifyInputInjected(TimeSpan idleDurationBeforeInjection) { }
        public void NotifyInputInjected() { }
    }

    private static (MacroExecutionContext context, FakeForegroundService fg, FakeForegroundKeyboard kb, List<string> progressUpdates) CreateContext(
        ITextDetectionService detector,
        IntPtr targetHwnd,
        IntPtr initialFg)
    {
        var fg = new FakeForegroundService { CurrentForeground = initialFg };
        var kb = new FakeForegroundKeyboard();
        var progress = new List<string>();

        var ctx = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            foregroundKeyboard: kb,
            foregroundService: fg,
            textDetector: detector,
            elevationService: new FakeElevationService(),
            userActivityService: new FakeUserActivity())
        {
            ProgressCallback = msg => progress.Add(msg)
        };

        return (ctx, fg, kb, progress);
    }

    private const string PromptText = "git status\n\nRun this command?\n> 1. Yes, run command\n  2. No, edit command\n";

    private static CommandApprovalRule CreateRule(string allowedCmd = "git status") => new()
    {
        Name = "Approve command",
        ExpectedProcess = "WindowsTerminal",
        ExpectedWindowClass = "CASCADIA",
        ExpectedPrompt = "Run this command?",
        ExpectedSelectedOption = "Yes, run command",
        AllowedCommand = allowedCmd,
        CommandMatchMode = CommandMatchMode.Exact,
        Enabled = true
    };

    [Fact]
    public async Task TestA1_SinglePane_PromptPresent_PaneFocused_Succeeds()
    {
        // Arrange
        IntPtr wtHwnd = new(0x1000);
        bool promptDismissed = false;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            var pane = new TerminalPaneInfo(PromptText, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { pane }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await action.ExecuteAsync(context, cts.Token);

        // Assert
        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    [Fact]
    public async Task TestA2_TwoPanes_PromptInFocusedPane_Succeeds()
    {
        // Arrange
        IntPtr wtHwnd = new(0x1000);
        bool promptDismissed = false;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            var paneA = new TerminalPaneInfo("PS D:\\> Get-Date", HasKeyboardFocus: false, new Rect(0, 0, 500, 800));
            var paneB = new TerminalPaneInfo(PromptText, HasKeyboardFocus: true, new Rect(500, 0, 500, 800));
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { paneA, paneB }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await action.ExecuteAsync(context, cts.Token);

        // Assert
        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    [Fact]
    public async Task TestA3_TwoPanes_PromptInUnfocusedPane_PausesUntilUserFocusesPromptPane()
    {
        // Arrange
        IntPtr wtHwnd = new(0x1000);
        bool userFocusedPaneA = false;
        bool promptDismissed = false;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            var paneA = new TerminalPaneInfo(PromptText, HasKeyboardFocus: userFocusedPaneA, new Rect(0, 0, 500, 800));
            var paneB = new TerminalPaneInfo("PS D:\\> Get-Date", HasKeyboardFocus: !userFocusedPaneA, new Rect(500, 0, 500, 800));
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { paneA, paneB }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            pollInterval: TimeSpan.FromMilliseconds(30),
            timeout: TimeSpan.FromSeconds(2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Start execution asynchronously
        var execTask = action.ExecuteAsync(context, cts.Token);

        // Wait deterministically until progress reports inactive pane pause
        var waitSw = System.Diagnostics.Stopwatch.StartNew();
        while (waitSw.ElapsedMilliseconds < 2000 && !progress.Any(p => p.Contains("Prompt detected in inactive pane. Click pane to focus.")))
        {
            await Task.Delay(20);
        }

        Assert.Contains(progress, p => p.Contains("Prompt detected in inactive pane. Click pane to focus."));
        Assert.Equal(0, kb.SendEnterCallCount); // Enter must NOT have been sent yet

        // Simulate user clicking / focusing Pane A
        userFocusedPaneA = true;

        // Await action completion
        var result = await execTask;

        // Assert
        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    [Fact]
    public async Task TestA4_PromptDisappearsWhileWaitingForPaneFocus_NoEnterSent()
    {
        // Arrange
        IntPtr wtHwnd = new(0x1000);
        bool promptDisappeared = false;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDisappeared)
            {
                var goneA = new TerminalPaneInfo("PS D:\\> ^C", HasKeyboardFocus: false, new Rect(0, 0, 500, 800));
                var goneB = new TerminalPaneInfo("PS D:\\>", HasKeyboardFocus: true, new Rect(500, 0, 500, 800));
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\> ^C", terminalPanes: new[] { goneA, goneB }));
            }
            var paneA = new TerminalPaneInfo(PromptText, HasKeyboardFocus: false, new Rect(0, 0, 500, 800));
            var paneB = new TerminalPaneInfo("PS D:\\>", HasKeyboardFocus: true, new Rect(500, 0, 500, 800));
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { paneA, paneB }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd, wtHwnd);

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            pollInterval: TimeSpan.FromMilliseconds(20),
            timeout: TimeSpan.FromMilliseconds(500));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var execTask = action.ExecuteAsync(context, cts.Token);

        // Wait until paused for inactive pane
        var waitSw = System.Diagnostics.Stopwatch.StartNew();
        while (waitSw.ElapsedMilliseconds < 1000 && !progress.Any(p => p.Contains("Prompt detected in inactive pane. Click pane to focus.")))
        {
            await Task.Delay(20);
        }

        // Simulate prompt disappearing (e.g. user pressed Ctrl+C)
        promptDisappeared = true;

        var result = await execTask;

        // Assert
        Assert.NotEqual(MacroActionStatus.Success, result.Status);
        Assert.Equal(0, kb.SendEnterCallCount);
    }

    [Fact]
    public async Task TestA5_PromptChangesWhileWaitingForPaneFocus_StaleApprovalNotReused()
    {
        // Arrange
        IntPtr wtHwnd = new(0x1000);
        bool promptChanged = false;
        const string PromptBlocked = "rm -rf /\n\nRun this command?\n> 1. Yes, run command\n  2. No, edit command\n";

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptChanged)
            {
                var paneA = new TerminalPaneInfo(PromptBlocked, HasKeyboardFocus: true, new Rect(0, 0, 500, 800));
                var paneB = new TerminalPaneInfo("PS D:\\>", HasKeyboardFocus: false, new Rect(500, 0, 500, 800));
                return Task.FromResult(TextDetectionResult.Success(PromptBlocked, PromptBlocked, new[] { paneA, paneB }));
            }
            var paneAInit = new TerminalPaneInfo(PromptText, HasKeyboardFocus: false, new Rect(0, 0, 500, 800));
            var paneBInit = new TerminalPaneInfo("PS D:\\>", HasKeyboardFocus: true, new Rect(500, 0, 500, 800));
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { paneAInit, paneBInit }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd, wtHwnd);

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            pollInterval: TimeSpan.FromMilliseconds(20),
            timeout: TimeSpan.FromMilliseconds(400));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var execTask = action.ExecuteAsync(context, cts.Token);

        // Wait until paused for inactive pane
        var waitSw = System.Diagnostics.Stopwatch.StartNew();
        while (waitSw.ElapsedMilliseconds < 1000 && !progress.Any(p => p.Contains("Prompt detected in inactive pane. Click pane to focus.")))
        {
            await Task.Delay(20);
        }

        // Simulate prompt changing to unauthorized command
        promptChanged = true;

        var result = await execTask;

        // Assert
        Assert.Equal(0, kb.SendEnterCallCount);
        Assert.NotEqual(MacroActionStatus.Success, result.Status);
    }

    [Fact]
    public async Task TestA6_TwoPanesBothContainPrompt_FailsClosed_ZeroEnter()
    {
        // Arrange
        IntPtr wtHwnd = new(0x1000);
        var paneA = new TerminalPaneInfo(PromptText, HasKeyboardFocus: true, new Rect(0, 0, 500, 800));
        var paneB = new TerminalPaneInfo(PromptText, HasKeyboardFocus: false, new Rect(500, 0, 500, 800));

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { paneA, paneB })));

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd, wtHwnd);

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Act
        var result = await action.ExecuteAsync(context, cts.Token);

        // Assert
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("Multiple terminal panes contain approval prompts", result.Message);
        Assert.Equal(0, kb.SendEnterCallCount);
    }

    [Fact]
    public async Task TestA6_FoolMode_TwoPanesBothContainPrompt_FailsClosed_ZeroEnter()
    {
        // Arrange
        IntPtr wtHwnd = new(0x1000);
        var paneA = new TerminalPaneInfo(PromptText, HasKeyboardFocus: true, new Rect(0, 0, 500, 800));
        var paneB = new TerminalPaneInfo(PromptText, HasKeyboardFocus: false, new Rect(500, 0, 500, 800));

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { paneA, paneB })));

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd, wtHwnd);
        context.IsFoolModeAuthorized = true;

        var rule = new CommandApprovalRule { AllowedCommand = "anything" };
        var action = new SafeAutoConfirmAction(
            new ApprovalRuleSet { Rules = { rule }, ExpectedPrompt = "Run this command?" },
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode,
            timeout: TimeSpan.FromSeconds(2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        // Act
        var result = await action.ExecuteAsync(context, cts.Token);

        // Assert
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("Multiple terminal panes contain approval prompts", result.Message);
        Assert.Equal(0, kb.SendEnterCallCount);
    }

    [Fact]
    public async Task TestA7_TerminalRootForeground_WrongPaneFocused_NoEnterSent()
    {
        // Arrange
        IntPtr wtHwnd = new(0x1000);
        var paneA = new TerminalPaneInfo(PromptText, HasKeyboardFocus: false, new Rect(0, 0, 500, 800));
        var paneB = new TerminalPaneInfo("PS D:\\>", HasKeyboardFocus: true, new Rect(500, 0, 500, 800));

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { paneA, paneB })));

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd, wtHwnd);

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            pollInterval: TimeSpan.FromMilliseconds(20),
            timeout: TimeSpan.FromMilliseconds(150));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        var result = await action.ExecuteAsync(context, cts.Token);

        // Assert
        Assert.Equal(0, kb.SendEnterCallCount);
        Assert.Contains(progress, p => p.Contains("Prompt detected in inactive pane. Click pane to focus."));
    }

    [Fact]
    public async Task TestA8_ChromeForeground_PromptPaneDetectedInBg_PulseSucceedsAndVerifiesFocus()
    {
        // Arrange
        IntPtr wtHwnd = new(0x1000);
        IntPtr chromeHwnd = new(0x2000);
        bool promptDismissed = false;

        var paneA = new TerminalPaneInfo(PromptText, HasKeyboardFocus: true, new Rect(0, 0, 500, 800));
        var paneB = new TerminalPaneInfo("PS D:\\>", HasKeyboardFocus: false, new Rect(500, 0, 500, 800));

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { paneA, paneB }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd, chromeHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act
        var result = await action.ExecuteAsync(context, cts.Token);

        // Assert
        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, fg.ActivateCallCount);
        Assert.Equal(1, kb.SendEnterCallCount);
        Assert.Equal(1, fg.RestoreCallCount);
        Assert.Equal(chromeHwnd, fg.RestoredHwnds[0]);
    }
}
