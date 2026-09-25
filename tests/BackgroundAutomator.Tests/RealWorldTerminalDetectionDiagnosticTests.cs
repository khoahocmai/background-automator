using System.Diagnostics;
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
        var thread = new Thread(() =>
        {
            IntPtr hDesk = User32.OpenDesktop("default", 0, false, 0x01FF);
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
                if (hDesk != IntPtr.Zero) User32.CloseDesktop(hDesk);
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

            if (hDesk != IntPtr.Zero)
            {
                User32.CloseDesktop(hDesk);
            }
        });

        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
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
