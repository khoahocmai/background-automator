using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.TextDetection;
using BackgroundAutomator.Win32;
using Xunit;

using BackgroundAutomator.Core.Targeting;

namespace BackgroundAutomator.Tests;

public class WaitForTextMacroTests
{
    private class FakeTextDetector : ITextDetectionService
    {
        private readonly Func<IntPtr, TextDetectionRequest, CancellationToken, Task<TextDetectionResult>> _handler;

        public FakeTextDetector(Func<IntPtr, TextDetectionRequest, CancellationToken, Task<TextDetectionResult>> handler)
        {
            _handler = handler;
        }

        public Task<TextDetectionResult> DetectAsync(IntPtr targetHwnd, TextDetectionRequest request, CancellationToken ct = default)
        {
            return _handler(targetHwnd, request, ct);
        }
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

    private static MacroExecutionContext CreateContext(ITextDetectionService detector, IntPtr targetHwnd)
    {
        return new MacroExecutionContext(
            new NullClicker(),
            new NullCaptureService(),
            new InMemoryLogger(),
            targetHwnd,
            new BackgroundKeyboardEngine(),
            detector);
    }

    [Fact]
    public async Task WaitForText_Succeeds_Immediately_When_Text_Matches()
    {
        IntPtr validHwnd = User32.GetDesktopWindow();

        var detector = new FakeTextDetector((hwnd, req, ct) =>
        {
            return Task.FromResult(TextDetectionResult.Success("Run this command? > 1. Yes, run command"));
        });

        var context = CreateContext(detector, validHwnd);
        var action = new WaitForTextAction("Run this command?", TextMatchMode.Contains, timeout: TimeSpan.FromSeconds(2), pollInterval: TimeSpan.FromMilliseconds(50));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Contains("Matched text", result.Message);
    }

    [Fact]
    public async Task WaitForText_Succeeds_After_Polling_Multiple_Times()
    {
        IntPtr validHwnd = User32.GetDesktopWindow();
        int attempts = 0;

        var detector = new FakeTextDetector((hwnd, req, ct) =>
        {
            attempts++;
            if (attempts >= 3)
            {
                return Task.FromResult(TextDetectionResult.Success("Run this command?"));
            }
            return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
        });

        var context = CreateContext(detector, validHwnd);
        var action = new WaitForTextAction("Run this command?", TextMatchMode.Contains, timeout: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.True(attempts >= 3, $"Expected at least 3 attempts, got {attempts}");
    }

    [Fact]
    public async Task WaitForText_TimesOut_When_Text_Never_Appears()
    {
        IntPtr validHwnd = User32.GetDesktopWindow();

        var detector = new FakeTextDetector((hwnd, req, ct) =>
        {
            return Task.FromResult(TextDetectionResult.NotFound("Running command..."));
        });

        var context = CreateContext(detector, validHwnd);
        var action = new WaitForTextAction("Run this command?", TextMatchMode.Contains, timeout: TimeSpan.FromMilliseconds(100), pollInterval: TimeSpan.FromMilliseconds(20));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Timeout, result.Status);
        Assert.Contains("Timed out", result.Message);
    }

    [Fact]
    public async Task WaitForText_Cancels_Cleanly_When_CancellationToken_Fired()
    {
        IntPtr validHwnd = User32.GetDesktopWindow();

        var detector = new FakeTextDetector(async (hwnd, req, ct) =>
        {
            await Task.Delay(100, ct);
            return TextDetectionResult.NotFound();
        });

        var context = CreateContext(detector, validHwnd);
        var action = new WaitForTextAction("Run this command?", TextMatchMode.Contains, timeout: TimeSpan.FromSeconds(5), pollInterval: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(60);

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task WaitForText_Returns_TargetUnavailable_When_Hwnd_Invalid()
    {
        var detector = new FakeTextDetector((hwnd, req, ct) =>
            Task.FromResult(TextDetectionResult.Success("any")));

        var context = CreateContext(detector, IntPtr.Zero);
        var action = new WaitForTextAction("Run this command?");

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.TargetUnavailable, result.Status);
    }

    [Fact]
    public async Task WaitForText_Returns_InvalidConfiguration_When_ExpectedText_Empty()
    {
        IntPtr validHwnd = User32.GetDesktopWindow();
        var detector = new FakeTextDetector((hwnd, req, ct) => Task.FromResult(TextDetectionResult.Success("")));
        var context = CreateContext(detector, validHwnd);

        var action = new WaitForTextAction("   ");

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.InvalidConfiguration, result.Status);
    }

    [Fact]
    public async Task WaitForText_Returns_InvalidConfiguration_When_Timeout_NonPositive()
    {
        IntPtr validHwnd = User32.GetDesktopWindow();
        var detector = new FakeTextDetector((hwnd, req, ct) => Task.FromResult(TextDetectionResult.Success("")));
        var context = CreateContext(detector, validHwnd);

        var action = new WaitForTextAction("Run this command?", timeout: TimeSpan.Zero);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.InvalidConfiguration, result.Status);
    }

    [Fact]
    public async Task WaitForText_DetectorFailure_Returns_TextDetectionFailed()
    {
        IntPtr validHwnd = User32.GetDesktopWindow();
        var detector = new FakeTextDetector((hwnd, req, ct) =>
            throw new InvalidOperationException("Simulated detector fault"));

        var context = CreateContext(detector, validHwnd);
        var action = new WaitForTextAction("Run this command?", timeout: TimeSpan.FromSeconds(2));

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.TextDetectionFailed, result.Status);
        Assert.Contains("Simulated detector fault", result.Message);
    }

    [Fact]
    public async Task MacroRunner_Executes_WaitForText_Then_PressKey()
    {
        IntPtr validHwnd = User32.GetDesktopWindow();

        var detector = new FakeTextDetector((hwnd, req, ct) =>
            Task.FromResult(TextDetectionResult.Success("Run this command? > 1. Yes, run command")));

        var context = CreateContext(detector, validHwnd);
        using var runner = new MacroRunner(new InMemoryLogger());

        var actions = new IMacroAction[]
        {
            new WaitForTextAction("Run this command?", TextMatchMode.Contains, timeout: TimeSpan.FromSeconds(2)),
            new PressKeyAction(BackgroundKey.Enter)
        };

        var result = await runner.RunAsync(actions, context);

        Assert.True(result.IsSuccess);
        Assert.Equal(MacroRunnerState.Idle, runner.State);
        Assert.Equal(2, result.CompletedActionsCount);
    }
}
