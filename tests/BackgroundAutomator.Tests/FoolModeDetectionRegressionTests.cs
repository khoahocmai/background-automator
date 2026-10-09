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

public class FoolModeDetectionRegressionTests
{
    private class FakeForegroundService : IWindowForegroundService
    {
        public IntPtr CurrentForeground { get; set; } = new(0x1000);
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
        bool isFoolModeAuthorized = true)
    {
        var fg = new FakeForegroundService { CurrentForeground = targetHwnd };
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
            ProgressCallback = msg => progress.Add(msg),
            IsFoolModeAuthorized = isFoolModeAuthorized
        };

        return (ctx, fg, kb, progress);
    }

    private static SafeAutoConfirmAction CreateFoolModeAction(
        AutoConfirmExecutionMode mode = AutoConfirmExecutionMode.Confirm,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null)
    {
        var rule = new CommandApprovalRule
        {
            Name = "Unrestricted FOOL MODE rule",
            ExpectedProcess = "WindowsTerminal",
            ExpectedWindowClass = "CASCADIA",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command"
        };

        return new SafeAutoConfirmAction(
            new ApprovalRuleSet { Rules = { rule }, ExpectedPrompt = "Run this command?", ExpectedSelectedOption = "Yes, run command" },
            executionMode: mode,
            policyMode: ApprovalPolicyMode.FoolMode,
            pollInterval: pollInterval ?? TimeSpan.FromMilliseconds(20),
            timeout: timeout ?? TimeSpan.FromMilliseconds(500));
    }

    private const string CleanSingleLinePrompt =
        "Requesting permission for:\n" +
        "Get-Date\n\n" +
        "Run this command?\n" +
        "> 1. Yes, run command\n" +
        "  2. No, edit command\n";

    private const string MultilineUnindentedPrompt =
        "Requesting permission for:\n" +
        "git status\n" +
        "git log -1\n\n" +
        "Run this command?\n" +
        "> 1. Yes, run command\n" +
        "  2. No, edit command\n";

    private const string MultipleHistoricalHeadersPrompt =
        "Requesting permission for:\n" +
        "echo first-historical\n\n" +
        "Requesting permission for:\n" +
        "git status\n\n" +
        "Run this command?\n" +
        "> 1. Yes, run command\n" +
        "  2. No, edit command\n";

    private const string IncompletePromptMissingOptions =
        "Requesting permission for:\n" +
        "git status\n\n" +
        "Run this command?\n";

    private const string Option2SelectedPrompt =
        "Requesting permission for:\n" +
        "git status\n\n" +
        "Run this command?\n" +
        "  1. Yes, run command\n" +
        "> 2. No, edit command\n";

    private const string HistoricalPromptOnly =
        "Requesting permission for:\n" +
        "git status\n" +
        "On branch main\n" +
        "nothing to commit, working tree clean\n" +
        "PS D:\\project> \n";

    // 1. FOOL MODE + single-pane + clean prompt
    [Fact]
    public async Task Scenario1_FoolMode_SinglePane_CleanPrompt_Succeeds()
    {
        IntPtr wtHwnd = new(0x1000);
        bool promptDismissed = false;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            var pane = new TerminalPaneInfo(CleanSingleLinePrompt, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(CleanSingleLinePrompt, CleanSingleLinePrompt, new[] { pane }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = CreateFoolModeAction();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    // 2. FOOL MODE + single-pane + multiline command (Regression test for confirmed bug)
    [Fact]
    public async Task Scenario2_FoolMode_SinglePane_MultilineCommand_Succeeds()
    {
        IntPtr wtHwnd = new(0x1000);
        bool promptDismissed = false;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            var pane = new TerminalPaneInfo(MultilineUnindentedPrompt, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(MultilineUnindentedPrompt, MultilineUnindentedPrompt, new[] { pane }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = CreateFoolModeAction();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    // 3. FOOL MODE + valid envelope + extraction.Success=false
    [Fact]
    public async Task Scenario3_FoolMode_ValidEnvelope_ExtractionAmbiguous_Succeeds()
    {
        IntPtr wtHwnd = new(0x1000);
        bool promptDismissed = false;

        const string complexScriptPrompt =
            "Requesting permission for:\n" +
            "python -c \"import os; print(os.getcwd())\"\n" +
            "python -c \"import sys; print(sys.version)\"\n\n" +
            "Run this command?\n" +
            "> 1. Yes, run command\n" +
            "  2. No, edit command\n";

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            var pane = new TerminalPaneInfo(complexScriptPrompt, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(complexScriptPrompt, complexScriptPrompt, new[] { pane }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = CreateFoolModeAction();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    // 4. FOOL MODE + multiple historical headers -> Ambiguous block, zero Enter, correct live status
    [Fact]
    public async Task Scenario4_FoolMode_MultipleHistoricalHeaders_Blocks_ReportsAmbiguous_ZeroEnter()
    {
        IntPtr wtHwnd = new(0x1000);

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            var pane = new TerminalPaneInfo(MultipleHistoricalHeadersPrompt, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(MultipleHistoricalHeadersPrompt, MultipleHistoricalHeadersPrompt, new[] { pane }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);

        var action = CreateFoolModeAction(timeout: TimeSpan.FromMilliseconds(200));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(0, kb.SendEnterCallCount);
        Assert.NotEqual(MacroActionStatus.Success, result.Status);
        Assert.Contains(progress, p => p.Contains("Ambiguous prompt") || p.Contains("Ambiguous"));
        Assert.DoesNotContain("Waiting — Prompt not visible", progress);
    }

    // 5. FOOL MODE + incomplete permission prompt (options not yet rendered)
    [Fact]
    public async Task Scenario5_FoolMode_IncompletePrompt_ZeroEnter_ReportsOptionNotSelected()
    {
        IntPtr wtHwnd = new(0x1000);

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            var pane = new TerminalPaneInfo(IncompletePromptMissingOptions, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(IncompletePromptMissingOptions, IncompletePromptMissingOptions, new[] { pane }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);

        var action = CreateFoolModeAction(timeout: TimeSpan.FromMilliseconds(200));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(0, kb.SendEnterCallCount);
        Assert.NotEqual(MacroActionStatus.Success, result.Status);
        Assert.Contains(progress, p => p.Contains("Option not selected"));
    }

    // 6. FOOL MODE + no selected Yes option (Option 2 is selected)
    [Fact]
    public async Task Scenario6_FoolMode_Option2Selected_ZeroEnter_ReportsOptionNotSelected()
    {
        IntPtr wtHwnd = new(0x1000);

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            var pane = new TerminalPaneInfo(Option2SelectedPrompt, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(Option2SelectedPrompt, Option2SelectedPrompt, new[] { pane }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);

        var action = CreateFoolModeAction(timeout: TimeSpan.FromMilliseconds(200));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(0, kb.SendEnterCallCount);
        Assert.NotEqual(MacroActionStatus.Success, result.Status);
        Assert.Contains(progress, p => p.Contains("Option not selected"));
    }

    // 7. FOOL MODE + historical prompt only (Run this command? not visible)
    [Fact]
    public async Task Scenario7_FoolMode_HistoricalPromptOnly_ReportsPromptNotVisible_ZeroEnter()
    {
        IntPtr wtHwnd = new(0x1000);

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            var pane = new TerminalPaneInfo(HistoricalPromptOnly, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.NotFound(HistoricalPromptOnly, terminalPanes: new[] { pane }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);

        var action = CreateFoolModeAction(timeout: TimeSpan.FromMilliseconds(200));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(0, kb.SendEnterCallCount);
        Assert.NotEqual(MacroActionStatus.Success, result.Status);
        Assert.Contains(progress, p => p.Contains("Prompt not visible"));
    }

    // 8. FOOL MODE + split-pane + prompt in Pane A
    [Fact]
    public async Task Scenario8_FoolMode_SplitPane_PromptInPaneA_Succeeds()
    {
        IntPtr wtHwnd = new(0x1000);
        bool promptDismissed = false;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            var paneA = new TerminalPaneInfo(MultilineUnindentedPrompt, HasKeyboardFocus: true, new Rect(0, 0, 500, 800));
            var paneB = new TerminalPaneInfo("PS D:\\> npm run dev\nready on port 3000", HasKeyboardFocus: false, new Rect(500, 0, 500, 800));
            return Task.FromResult(TextDetectionResult.Success(MultilineUnindentedPrompt, MultilineUnindentedPrompt, new[] { paneA, paneB }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = CreateFoolModeAction();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    // 9. FOOL MODE + split-pane + prompt in Pane B
    [Fact]
    public async Task Scenario9_FoolMode_SplitPane_PromptInPaneB_Succeeds()
    {
        IntPtr wtHwnd = new(0x1000);
        bool promptDismissed = false;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            var paneA = new TerminalPaneInfo("PS D:\\> npm run dev\nready on port 3000", HasKeyboardFocus: false, new Rect(0, 0, 500, 800));
            var paneB = new TerminalPaneInfo(MultilineUnindentedPrompt, HasKeyboardFocus: true, new Rect(500, 0, 500, 800));
            return Task.FromResult(TextDetectionResult.Success(MultilineUnindentedPrompt, MultilineUnindentedPrompt, new[] { paneA, paneB }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = CreateFoolModeAction();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    // 10. FOOL MODE + unfocused prompt pane -> Pauses until user focuses
    [Fact]
    public async Task Scenario10_FoolMode_UnfocusedPromptPane_PausesUntilFocused()
    {
        IntPtr wtHwnd = new(0x1000);
        bool userFocusedPaneA = false;
        bool promptDismissed = false;

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            var paneA = new TerminalPaneInfo(MultilineUnindentedPrompt, HasKeyboardFocus: userFocusedPaneA, new Rect(0, 0, 500, 800));
            var paneB = new TerminalPaneInfo("PS D:\\> npm run dev", HasKeyboardFocus: !userFocusedPaneA, new Rect(500, 0, 500, 800));
            return Task.FromResult(TextDetectionResult.Success(MultilineUnindentedPrompt, MultilineUnindentedPrompt, new[] { paneA, paneB }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = CreateFoolModeAction(pollInterval: TimeSpan.FromMilliseconds(20), timeout: TimeSpan.FromSeconds(2));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var execTask = action.ExecuteAsync(context, cts.Token);

        var waitSw = System.Diagnostics.Stopwatch.StartNew();
        while (waitSw.ElapsedMilliseconds < 2000 && !progress.Any(p => p.Contains("FOOL MODE — PAUSED: Prompt detected in inactive pane. Click pane to focus.")))
        {
            await Task.Delay(20);
        }

        Assert.Contains(progress, p => p.Contains("FOOL MODE — PAUSED: Prompt detected in inactive pane. Click pane to focus."));
        Assert.Equal(0, kb.SendEnterCallCount);

        userFocusedPaneA = true;

        var result = await execTask;

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, kb.SendEnterCallCount);
    }

    // 11. FOOL MODE + two simultaneous active prompt panes -> Fails closed
    [Fact]
    public async Task Scenario11_FoolMode_TwoSimultaneousPromptPanes_FailsClosed()
    {
        IntPtr wtHwnd = new(0x1000);
        var paneA = new TerminalPaneInfo(MultilineUnindentedPrompt, HasKeyboardFocus: true, new Rect(0, 0, 500, 800));
        var paneB = new TerminalPaneInfo(MultilineUnindentedPrompt, HasKeyboardFocus: false, new Rect(500, 0, 500, 800));

        var detector = new FakeTextDetector((hwnd, req) =>
            Task.FromResult(TextDetectionResult.Success(MultilineUnindentedPrompt, MultilineUnindentedPrompt, new[] { paneA, paneB })));

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);

        var action = CreateFoolModeAction();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("Multiple terminal panes contain approval prompts", result.Message);
        Assert.Equal(0, kb.SendEnterCallCount);
    }

    // 12. FOOL MODE authorization missing -> Fails immediately
    [Fact]
    public async Task Scenario12_FoolMode_AuthorizationMissing_BlocksImmediately()
    {
        IntPtr wtHwnd = new(0x1000);
        var detector = new FakeTextDetector((hwnd, req) => Task.FromResult(TextDetectionResult.NotFound()));

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd, isFoolModeAuthorized: false);

        var action = CreateFoolModeAction();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("requires explicit user authorization", result.Message);
        Assert.Equal(0, kb.SendEnterCallCount);
    }

    // 13. ExpectedPrompt matched but approval evidence invalid (unrelated document text)
    [Fact]
    public async Task Scenario13_ExpectedPromptMatched_UnrelatedText_Blocks()
    {
        IntPtr wtHwnd = new(0x1000);
        const string unrelatedText = "Instructions:\r\nWhen prompted: Run this command?\r\nType yes to proceed.\r\n";

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            var pane = new TerminalPaneInfo(unrelatedText, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(unrelatedText, unrelatedText, new[] { pane }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);

        var action = CreateFoolModeAction(timeout: TimeSpan.FromMilliseconds(200));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(0, kb.SendEnterCallCount);
        Assert.NotEqual(MacroActionStatus.Success, result.Status);
    }

    // 14. Correct live status when pane is detected but approval is blocked
    [Fact]
    public async Task Scenario14_CorrectLiveStatus_WhenApprovalBlocked()
    {
        IntPtr wtHwnd = new(0x1000);

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            var pane = new TerminalPaneInfo(Option2SelectedPrompt, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(Option2SelectedPrompt, Option2SelectedPrompt, new[] { pane }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);

        var action = CreateFoolModeAction(timeout: TimeSpan.FromMilliseconds(200));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Contains("Waiting — Option not selected", progress);
        Assert.DoesNotContain("Waiting — Prompt not visible", progress);
    }

    // 15. Non-regression of existing ExactRules behavior
    [Fact]
    public async Task Scenario15_ExactRules_NonRegression_AllowedSucceeds_DisallowedBlocks()
    {
        IntPtr wtHwnd = new(0x1000);
        bool promptDismissed = false;

        var allowedRule = new CommandApprovalRule
        {
            Name = "Allowed rule",
            ExpectedProcess = "WindowsTerminal",
            ExpectedWindowClass = "CASCADIA",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command",
            AllowedCommand = "Get-Date",
            CommandMatchMode = CommandMatchMode.Exact,
            Enabled = true
        };

        var detector = new FakeTextDetector((hwnd, req) =>
        {
            if (promptDismissed)
            {
                return Task.FromResult(TextDetectionResult.NotFound("PS D:\\>"));
            }
            var pane = new TerminalPaneInfo(CleanSingleLinePrompt, HasKeyboardFocus: true, new Rect(0, 0, 1000, 800));
            return Task.FromResult(TextDetectionResult.Success(CleanSingleLinePrompt, CleanSingleLinePrompt, new[] { pane }));
        });

        var (context, fg, kb, progress) = CreateContext(detector, wtHwnd);
        kb.OnEnterSent = () => promptDismissed = true;

        var action = new SafeAutoConfirmAction(
            new ApprovalRuleSet { Rules = { allowedRule }, ExpectedPrompt = "Run this command?", ExpectedSelectedOption = "Yes, run command" },
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.ExactRules,
            timeout: TimeSpan.FromSeconds(2));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Equal(1, kb.SendEnterCallCount);
    }
}
