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

public class ForegroundFocusPolicyTests
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
        public TimeSpan IdleDuration { get; set; } = TimeSpan.FromSeconds(10);
        public bool TryGetIdleResult { get; set; } = true;

        public bool TryGetIdleDuration(out TimeSpan idleDuration)
        {
            idleDuration = IdleDuration;
            return TryGetIdleResult;
        }

        public void NotifyInputInjected(TimeSpan idleDurationBeforeInjection) { }
        public void NotifyInputInjected() { }
    }

    private static (MacroExecutionContext context, FakeForegroundService fg, FakeForegroundKeyboard kb, FakeUserActivity userActivity, List<string> progressUpdates) CreateContext(
        ITextDetectionService detector,
        IntPtr targetHwnd,
        IntPtr initialFg)
    {
        var fg = new FakeForegroundService { CurrentForeground = initialFg };
        var kb = new FakeForegroundKeyboard();
        var progress = new List<string>();
        var userActivity = new FakeUserActivity();

        var ctx = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            foregroundKeyboard: kb,
            foregroundService: fg,
            textDetector: detector,
            elevationService: new FakeElevationService(),
            userActivityService: userActivity)
        {
            ProgressCallback = msg => progress.Add(msg)
        };

        return (ctx, fg, kb, userActivity, progress);
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
    public async Task Test1_StrictTerminalForegroundOnly_ChromeForeground_Active_Pauses_ZeroActivate_ZeroEnter()
    {
        IntPtr wtHwnd = new(0x1000);
        IntPtr chromeHwnd = new(0x2000);

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            var pane = new TerminalPaneInfo(PromptText, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { pane }));
        });

        var (context, fg, kb, userActivity, progress) = CreateContext(detector, wtHwnd, chromeHwnd);
        userActivity.IdleDuration = TimeSpan.FromMilliseconds(50); // user actively typing in Chrome

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20),
            foregroundPolicy: ForegroundPolicy.StrictTerminalForegroundOnly);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Equal(0, fg.ActivateCallCount);
        Assert.Equal(0, fg.RestoreCallCount);
        Assert.Equal(0, kb.SendEnterCallCount);
        Assert.Contains(progress, p => p.Contains("Waiting for Terminal foreground"));
    }

    [Fact]
    public async Task Test2_StrictTerminalForegroundOnly_ChromeForeground_30sIdle_ZeroActivate_ZeroEnter()
    {
        IntPtr wtHwnd = new(0x1000);
        IntPtr chromeHwnd = new(0x2000);

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            var pane = new TerminalPaneInfo(PromptText, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { pane }));
        });

        var (context, fg, kb, userActivity, progress) = CreateContext(detector, wtHwnd, chromeHwnd);
        userActivity.IdleDuration = TimeSpan.FromSeconds(30); // Proves even 30s idle cannot bypass strict policy!

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20),
            foregroundPolicy: ForegroundPolicy.StrictTerminalForegroundOnly);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Equal(0, fg.ActivateCallCount);
        Assert.Equal(0, fg.RestoreCallCount);
        Assert.Equal(0, kb.SendEnterCallCount);
        Assert.Contains(progress, p => p.Contains("Waiting for Terminal foreground"));
    }

    [Fact]
    public async Task Test3_StrictTerminalForegroundOnly_ChromeForeground_UserNaturallyFocusesTerminal_ZeroActivate_SendsEnterOnce()
    {
        IntPtr wtHwnd = new(0x1000);
        IntPtr chromeHwnd = new(0x2000);
        bool promptDismissed = false;
        int detectCount = 0;
        FakeForegroundService? fgRef = null;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            detectCount++;
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            if (detectCount >= 2 && fgRef != null)
            {
                // Switch foreground naturally to Terminal when strict wait begins
                fgRef.CurrentForeground = wtHwnd;
            }
            var pane = new TerminalPaneInfo(PromptText, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { pane }));
        });

        var (context, fg, kb, userActivity, progress) = CreateContext(detector, wtHwnd, chromeHwnd);
        fgRef = fg;
        kb.OnEnterSent = () => promptDismissed = true;

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20),
            foregroundPolicy: ForegroundPolicy.StrictTerminalForegroundOnly);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(0, fg.ActivateCallCount);
        Assert.Equal(0, fg.RestoreCallCount);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    [Fact]
    public async Task Test4_StrictTerminalForegroundOnly_TerminalAlreadyForeground_ZeroActivate_SendsEnterOnce()
    {
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

        var (context, fg, kb, userActivity, progress) = CreateContext(detector, wtHwnd, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20),
            foregroundPolicy: ForegroundPolicy.StrictTerminalForegroundOnly);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(0, fg.ActivateCallCount);
        Assert.Equal(0, fg.RestoreCallCount);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    [Fact]
    public async Task Test5_StrictTerminalForegroundOnly_TerminalForeground_PaneAUnfocusedPromptInB_Pauses_ThenUserFocusesB_SendsEnterOnce()
    {
        IntPtr wtHwnd = new(0x1000);
        bool promptDismissed = false;
        bool paneBFocused = false;
        int detectCount = 0;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            detectCount++;
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            if (detectCount >= 3)
            {
                // User focuses Pane B while BackgroundAutomator is waiting for pane focus
                paneBFocused = true;
            }
            var paneA = new TerminalPaneInfo("PS D:\\> Get-Date", HasKeyboardFocus: !paneBFocused, new Rect(0, 0, 500, 800));
            var paneB = new TerminalPaneInfo(PromptText, HasKeyboardFocus: paneBFocused, new Rect(500, 0, 500, 800));
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { paneA, paneB }));
        });

        var (context, fg, kb, userActivity, progress) = CreateContext(detector, wtHwnd, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20),
            foregroundPolicy: ForegroundPolicy.StrictTerminalForegroundOnly);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Contains(progress, p => p.Contains("Prompt detected in inactive pane"));
        Assert.Equal(0, fg.ActivateCallCount);
        Assert.Equal(0, fg.RestoreCallCount);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    [Fact]
    public async Task Test6_StrictTerminalForegroundOnly_ChromeForeground_TwoPanes_ProgressesThroughBothPauses_SendsEnterOnce()
    {
        IntPtr wtHwnd = new(0x1000);
        IntPtr chromeHwnd = new(0x2000);
        bool promptDismissed = false;
        bool paneBFocused = false;
        int detectCount = 0;
        FakeForegroundService? fgRef = null;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            detectCount++;
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            if (detectCount >= 2 && fgRef != null)
            {
                // Step 1: Switch foreground naturally to Terminal when strict wait begins
                fgRef.CurrentForeground = wtHwnd;
            }
            if (detectCount >= 4)
            {
                // Step 2: Switch focus to Pane B after split-pane wait begins
                paneBFocused = true;
            }
            var paneA = new TerminalPaneInfo("PS D:\\> Get-Date", HasKeyboardFocus: !paneBFocused, new Rect(0, 0, 500, 800));
            var paneB = new TerminalPaneInfo(PromptText, HasKeyboardFocus: paneBFocused, new Rect(500, 0, 500, 800));
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { paneA, paneB }));
        });

        var (context, fg, kb, userActivity, progress) = CreateContext(detector, wtHwnd, chromeHwnd);
        fgRef = fg;
        kb.OnEnterSent = () => promptDismissed = true;

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(3),
            pollInterval: TimeSpan.FromMilliseconds(20),
            foregroundPolicy: ForegroundPolicy.StrictTerminalForegroundOnly);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Contains(progress, p => p.Contains("Waiting for Terminal foreground"));
        Assert.Contains(progress, p => p.Contains("Prompt detected in inactive pane"));
        Assert.Equal(0, fg.ActivateCallCount);
        Assert.Equal(0, fg.RestoreCallCount);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    [Fact]
    public async Task Test7_AllowIdlePulse_UserActive_PausesUserActive_ZeroActivate_ZeroEnter()
    {
        IntPtr wtHwnd = new(0x1000);
        IntPtr chromeHwnd = new(0x2000);

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            var pane = new TerminalPaneInfo(PromptText, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { pane }));
        });

        var (context, fg, kb, userActivity, progress) = CreateContext(detector, wtHwnd, chromeHwnd);
        userActivity.IdleDuration = TimeSpan.FromMilliseconds(100); // active user

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20),
            foregroundPolicy: ForegroundPolicy.AllowIdlePulse,
            respectUserFocus: true,
            userIdleThreshold: TimeSpan.FromMilliseconds(1500));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Equal(0, fg.ActivateCallCount);
        Assert.Equal(0, fg.RestoreCallCount);
        Assert.Equal(0, kb.SendEnterCallCount);
        Assert.Contains(progress, p => p.Contains("User active in another window"));
    }

    [Fact]
    public async Task Test8_AllowIdlePulse_UserIdle_ExecutesFastPulse_ActivatesAndRestores()
    {
        IntPtr wtHwnd = new(0x1000);
        IntPtr chromeHwnd = new(0x2000);
        int detectCount = 0;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            detectCount++;
            // 1st call: initial detection
            // 2nd call: revalidation after activation
            // 3rd call: acknowledgment after Enter
            if (detectCount <= 2)
            {
                var pane = new TerminalPaneInfo(PromptText, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
                return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { pane }));
            }
            return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
        });

        var (context, fg, kb, userActivity, progress) = CreateContext(detector, wtHwnd, chromeHwnd);
        userActivity.IdleDuration = TimeSpan.FromSeconds(5); // idle

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20),
            focusBehavior: FocusBehavior.FastPulse,
            foregroundPolicy: ForegroundPolicy.AllowIdlePulse,
            respectUserFocus: true);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, fg.ActivateCallCount);
        Assert.Equal(1, kb.SendEnterCallCount);
        Assert.Equal(1, fg.RestoreCallCount);
        Assert.Contains(chromeHwnd, fg.RestoredHwnds);
    }

    [Fact]
    public async Task Test9_AllowIdlePulse_TerminalAlreadyForeground_ImmediateValidation_SendsEnterOnce()
    {
        IntPtr wtHwnd = new(0x1000);
        int detectCount = 0;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            detectCount++;
            if (detectCount <= 2)
            {
                var pane = new TerminalPaneInfo(PromptText, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
                return Task.FromResult(TextDetectionResult.Success(PromptText, PromptText, new[] { pane }));
            }
            return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
        });

        var (context, fg, kb, userActivity, progress) = CreateContext(detector, wtHwnd, wtHwnd);

        var action = new SafeAutoConfirmAction(
            CreateRule(),
            executionMode: AutoConfirmExecutionMode.Confirm,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(20),
            focusBehavior: FocusBehavior.FastPulse,
            foregroundPolicy: ForegroundPolicy.AllowIdlePulse);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(0, fg.ActivateCallCount);
        Assert.Equal(0, fg.RestoreCallCount);
        Assert.Equal(1, kb.SendEnterCallCount);
    }
}
