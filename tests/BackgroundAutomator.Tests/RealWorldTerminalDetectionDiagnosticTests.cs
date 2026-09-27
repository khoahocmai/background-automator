using System.Diagnostics;
using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.TextDetection;
using BackgroundAutomator.Win32;
using Xunit;

namespace BackgroundAutomator.Tests;

/// <summary>
/// Real-world diagnostic test against live Windows Terminal / Antigravity CLI.
/// Per Phase 3 requirements (Section 19), this test safely checks for an active terminal
/// and runs non-intrusive diagnostic verification without requiring interactive user input.
/// </summary>
public class RealWorldTerminalDetectionDiagnosticTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public RealWorldTerminalDetectionDiagnosticTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "ManualDiagnostic")]
    public void RealWorld_Terminal_PromptDetection_Diagnostics()
    {
        if (Environment.GetEnvironmentVariable("BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS") != "1")
        {
            _output.WriteLine("[OPT-IN] Skipped. Set BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS=1 to run interactive terminal diagnostics.");
            return;
        }

        var thread = new Thread(() =>
        {
            IntPtr hDesk = User32.OpenDesktop("default", 0, false, 0x01FF);
            try
            {
                if (hDesk != IntPtr.Zero)
                {
                    User32.SetThreadDesktop(hDesk);
                }

                IAppLogger logger = new InMemoryLogger();
                logger.MessageLogged += entry => _output.WriteLine(entry.ToString());

                IntPtr cascadiaHwnd = IntPtr.Zero;
                string terminalTitle = "";
                uint terminalPid = 0;

                User32.EnumWindows((hWnd, lParam) =>
                {
                    string cls = User32.GetClassNameSafe(hWnd);
                    if (cls.Contains("CASCADIA", StringComparison.OrdinalIgnoreCase))
                    {
                        cascadiaHwnd = hWnd;
                        terminalTitle = User32.GetWindowTextSafe(hWnd);
                        User32.GetWindowThreadProcessId(hWnd, out terminalPid);
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (cascadiaHwnd == IntPtr.Zero)
                {
                    _output.WriteLine("[SKIPPED] No live Windows Terminal (CASCADIA) window found on current desktop.");
                    return;
                }

                IntPtr fg = User32.GetForegroundWindow();
                bool isForeground = (fg == cascadiaHwnd);

                _output.WriteLine($"[DIAGNOSTIC] Found Windows Terminal HWND=0x{cascadiaHwnd.ToInt64():X8}, PID={terminalPid}, Title='{terminalTitle}', Foreground={isForeground}");

                var detector = new UiAutomationTextDetectionService(logger);

                // Case A: Query text with VisibleOnly=false (e.g. "Antigravity")
                var requestFull = new TextDetectionRequest("Antigravity", TextMatchMode.Contains, VisibleOnly: false);
                var resFull = detector.DetectAsync(cascadiaHwnd, requestFull, CancellationToken.None).GetAwaiter().GetResult();
                _output.WriteLine($"[DIAGNOSTIC] Detect 'Antigravity' (VisibleOnly=false): Matched={resFull.Matched}");

                // Diagnostic: Dump TermControl VisibleRanges raw text
                try
                {
                    var rootElem = System.Windows.Automation.AutomationElement.FromHandle(cascadiaHwnd);
                    var termCond = new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.ClassNameProperty, "TermControl");
                    var termControl = rootElem.FindFirst(System.Windows.Automation.TreeScope.Descendants, termCond);
                    if (termControl != null && termControl.TryGetCurrentPattern(System.Windows.Automation.TextPattern.Pattern, out object tpObj) && tpObj is System.Windows.Automation.TextPattern tp)
                    {
                        var visRanges = tp.GetVisibleRanges();
                        _output.WriteLine($"[DIAGNOSTIC] TermControl visible ranges count: {visRanges.Length}");
                        for (int i = 0; i < visRanges.Length; i++)
                        {
                            string txt = visRanges[i].GetText(-1);
                            _output.WriteLine($"--- VISIBLE RANGE [{i}] (Length={txt.Length}) ---");
                            // Split by lines and output each line
                            var lines = txt.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                            for (int l = 0; l < lines.Length; l++)
                            {
                                _output.WriteLine($"L{l:D3}: {lines[l]}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _output.WriteLine($"[DIAGNOSTIC] Visible range dump exception: {ex.Message}");
                }

                // Case B: Negative test — text that does not exist must NOT match
                var requestNegative = new TextDetectionRequest("NonExistentPrompt_XYZ_987654321", TextMatchMode.Contains, VisibleOnly: true);
                var resNeg = detector.DetectAsync(cascadiaHwnd, requestNegative, CancellationToken.None).GetAwaiter().GetResult();
                _output.WriteLine($"[DIAGNOSTIC] Negative test 'NonExistentPrompt...': Matched={resNeg.Matched}");
                Assert.False(resNeg.Matched, "Non-existent prompt text must never match");

                // Case C: Macro WaitForText with timeout against non-existent text
                var waitAction = new WaitForTextAction(
                    "NonExistentPrompt_XYZ_987654321",
                    TextMatchMode.Contains,
                    timeout: TimeSpan.FromMilliseconds(300),
                    pollInterval: TimeSpan.FromMilliseconds(50));

                var context = new MacroExecutionContext(
                    new NullClicker(),
                    new NullCaptureService(),
                    logger,
                    cascadiaHwnd,
                    textDetector: detector);

                var macroResult = waitAction.ExecuteAsync(context, CancellationToken.None).GetAwaiter().GetResult();
                _output.WriteLine($"[DIAGNOSTIC] WaitForText timeout test result: {macroResult.Status}");
                Assert.Equal(MacroActionStatus.Timeout, macroResult.Status);
            }
            finally
            {
                if (hDesk != IntPtr.Zero)
                {
                    User32.CloseDesktop(hDesk);
                }
            }
        });

        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    [Trait("Category", "ManualDiagnostic")]
    public void RealWorld_Terminal_SafeAutoConfirm_ObserveOnly_Diagnostics()
    {
        if (Environment.GetEnvironmentVariable("BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS") != "1")
        {
            _output.WriteLine("[OPT-IN] Skipped. Set BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS=1 to run interactive terminal diagnostics.");
            return;
        }

        var thread = new Thread(() =>
        {
            IntPtr hDesk = User32.OpenDesktop("default", 0, false, 0x01FF);
            try
            {
                if (hDesk != IntPtr.Zero)
                {
                    User32.SetThreadDesktop(hDesk);
                }

                IAppLogger logger = new InMemoryLogger();
                logger.MessageLogged += entry => _output.WriteLine(entry.ToString());

                IntPtr cascadiaHwnd = IntPtr.Zero;
                string terminalTitle = "";
                uint terminalPid = 0;

                User32.EnumWindows((hWnd, lParam) =>
                {
                    string cls = User32.GetClassNameSafe(hWnd);
                    if (cls.Contains("CASCADIA", StringComparison.OrdinalIgnoreCase))
                    {
                        cascadiaHwnd = hWnd;
                        terminalTitle = User32.GetWindowTextSafe(hWnd);
                        User32.GetWindowThreadProcessId(hWnd, out terminalPid);
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);

                if (cascadiaHwnd == IntPtr.Zero)
                {
                    _output.WriteLine("[SKIPPED] No live Windows Terminal window found.");
                    return;
                }

                _output.WriteLine($"[DIAGNOSTIC] Running ObserveOnly check against HWND 0x{cascadiaHwnd.ToInt64():X8} (PID {terminalPid}, Title '{terminalTitle}')");

                var rule = new BackgroundAutomator.Core.Approval.CommandApprovalRule
                {
                    Name = "Harmless diagnostic check",
                    ExpectedProcess = "WindowsTerminal.exe",
                    ExpectedPrompt = "Run this command?",
                    ExpectedSelectedOption = "Yes, run command",
                    AllowedCommand = "dotnet test BackgroundAutomator.sln",
                    CommandMatchMode = BackgroundAutomator.Core.Approval.CommandMatchMode.Exact
                };

                var action = new SafeAutoConfirmAction(
                    rule,
                    executionMode: BackgroundAutomator.Core.Approval.AutoConfirmExecutionMode.ObserveOnly,
                    timeout: TimeSpan.FromMilliseconds(500),
                    pollInterval: TimeSpan.FromMilliseconds(100));

                var context = new MacroExecutionContext(
                    new NullClicker(),
                    new NullCaptureService(),
                    logger,
                    cascadiaHwnd);

                var result = action.ExecuteAsync(context, CancellationToken.None).GetAwaiter().GetResult();
                _output.WriteLine($"[DIAGNOSTIC] ObserveOnly result: Status={result.Status}, Message='{result.Message}'");
            }
            finally
            {
                if (hDesk != IntPtr.Zero)
                {
                    User32.CloseDesktop(hDesk);
                }
            }
        });

        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
    }

    [Fact]
    [Trait("Category", "ManualDiagnostic")]
    public void RealWorld_Live_MultilineCommand_Diagnostics()
    {
        var parser = new CommandPromptParser();
        string complexMultiline = @"
Requesting permission for:
git ls-files | Where-Object { $_ -match '(bin/|obj/|TestResults/|\.dll$|\.exe$|\.pdb$|\.dmp$|\.trx$|\.cache$|\.suo$|\.user$)' }

Run this command?
> 1. Yes, run command
  2. No, cancel
";
        var extraction = parser.Parse(complexMultiline, "Run this command?", "Yes, run command");
        _output.WriteLine($"[DIAGNOSTIC] Multiline Parse: Success={extraction.Success}, IsAmbiguous={extraction.IsAmbiguous}, EnvelopeNull={extraction.Envelope == null}");
        Assert.NotNull(extraction.Envelope);
        Assert.True(extraction.Envelope.IsStructurallyValid);
        Assert.Contains("git ls-files", extraction.Envelope.RawCommandBlock);

        var rule = new CommandApprovalRule
        {
            Name = "Strict Rule",
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command",
            AllowedCommand = "git status"
        };
        var snapshot = CommandPromptSnapshot.FromExtraction((IntPtr)0x1234, complexMultiline, extraction);
        var decision = CommandApprovalEvaluator.Evaluate(rule, snapshot, "WindowsTerminal.exe", "CASCADIA", ApprovalPolicyMode.FoolMode);
        _output.WriteLine($"[DIAGNOSTIC] FOOL MODE Decision: Allowed={decision.IsAllowed}, Explanation='{decision.Explanation}'");
        Assert.True(decision.IsAllowed);
        Assert.Contains("FOOL MODE", decision.Explanation);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE = 9;

    [Fact]
    [Trait("Category", "ManualDiagnostic")]
    public async Task RealWorld_Live_Minimize_Pause_Restore_Resumed_Diagnostics()
    {
        if (Environment.GetEnvironmentVariable("BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS") != "1")
        {
            _output.WriteLine("[OPT-IN] Skipped. Set BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS=1 to run interactive terminal diagnostics.");
            return;
        }

        using var fixture = new TestTargetFixture();
        IntPtr hwnd = fixture.MainWindowHandle;
        _output.WriteLine($"[DIAGNOSTIC] Launched TestTarget live HWND=0x{hwnd.ToInt64():X8}");

        var fgService = new Win32WindowForegroundService();
        Assert.True(fgService.IsWindow(hwnd));

        // Minimize the live window using Win32 API
        ShowWindow(hwnd, SW_MINIMIZE);
        Thread.Sleep(150);
        bool isMin = fgService.IsWindowMinimized(hwnd);
        _output.WriteLine($"[DIAGNOSTIC] Live Window Minimized: {isMin}");
        Assert.True(isMin, "Window should be minimized by Win32 API");

        var progressList = new List<string>();
        var logger = new InMemoryLogger();
        logger.MessageLogged += e => _output.WriteLine(e.ToString());

        var context = new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            logger,
            hwnd,
            foregroundService: fgService,
            textDetector: new SimpleTextDetector((h, r) => Task.FromResult(TextDetectionResult.NotFound())));
        context.ProgressCallback = p =>
        {
            progressList.Add(p);
            _output.WriteLine($"[DIAGNOSTIC PROGRESS] {p}");
        };

        var rule = new CommandApprovalRule
        {
            Name = "Live Minimize Test",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command",
            AllowedCommand = "Get-Date"
        };
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.ObserveOnly,
            timeout: TimeSpan.FromMilliseconds(800),
            pollInterval: TimeSpan.FromMilliseconds(50));

        // Background restore after 300ms
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            _output.WriteLine("[DIAGNOSTIC] Restoring live window via ShowWindow SW_RESTORE...");
            ShowWindow(hwnd, SW_RESTORE);
        });

        var result = await action.ExecuteAsync(context, CancellationToken.None);
        _output.WriteLine($"[DIAGNOSTIC] Action completed with Status={result.Status}");

        Assert.Contains(progressList, p => p.Contains("PAUSED — Target minimized"));
        Assert.Contains(progressList, p => p.Contains("Waiting — Prompt not visible")); // RESUMED evaluation!
    }

    [Fact]
    [Trait("Category", "ManualDiagnostic")]
    public void RealWorld_Live_ForegroundActivationFailed_Diagnostics()
    {
        if (Environment.GetEnvironmentVariable("BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS") != "1")
        {
            _output.WriteLine("[OPT-IN] Skipped. Set BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS=1 to run interactive terminal diagnostics.");
            return;
        }

        // Test if SetForegroundWindow fails live when invoked against a background window
        using var fixture = new TestTargetFixture();
        IntPtr hwnd = fixture.MainWindowHandle;
        var fgService = new Win32WindowForegroundService();

        bool activated = false;
        var t = new Thread(() =>
        {
            activated = fgService.ActivateWindow(hwnd);
        });
        t.Start();
        t.Join();

        _output.WriteLine($"[DIAGNOSTIC] Foreground activation result: {activated}");
        if (!activated)
        {
            _output.WriteLine("[DIAGNOSTIC] REPRODUCED LIVE: Windows denied SetForegroundWindow.");
        }
        else
        {
            _output.WriteLine("[DIAGNOSTIC] NOT REPRODUCED LIVE: SetForegroundWindow succeeded under current OS security context.");
        }
    }

    private class SimpleTextDetector : ITextDetectionService
    {
        private readonly Func<IntPtr, TextDetectionRequest, Task<TextDetectionResult>> _handler;
        public SimpleTextDetector(Func<IntPtr, TextDetectionRequest, Task<TextDetectionResult>> handler) => _handler = handler;
        public Task<TextDetectionResult> DetectAsync(IntPtr targetHwnd, TextDetectionRequest request, CancellationToken ct = default) => _handler(targetHwnd, request);
    }

    private class NullClicker : BackgroundAutomator.Core.Clicking.IBackgroundClicker
    {
        public BackgroundAutomator.Core.Clicking.ClickResult Click(BackgroundAutomator.Core.Targeting.TargetPoint target) => BackgroundAutomator.Core.Clicking.ClickResult.Success;
        public BackgroundAutomator.Core.Clicking.ClickResult DoubleClick(BackgroundAutomator.Core.Targeting.TargetPoint target) => BackgroundAutomator.Core.Clicking.ClickResult.Success;
    }

    private class NullCaptureService : BackgroundAutomator.Core.Capture.IWindowCaptureService
    {
        public BackgroundAutomator.Core.Capture.WindowCapture? CaptureClientArea(IntPtr hWnd) => null;
        public Task<BackgroundAutomator.Core.Capture.WindowCapture?> CaptureClientAreaAsync(IntPtr hWnd, CancellationToken ct = default) =>
            Task.FromResult<BackgroundAutomator.Core.Capture.WindowCapture?>(null);
    }
}
