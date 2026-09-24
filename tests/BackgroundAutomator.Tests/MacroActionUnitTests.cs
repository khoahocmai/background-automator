using System.Diagnostics;
using System.Drawing;
using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Targeting;
using Xunit;

namespace BackgroundAutomator.Tests;

public class MacroActionUnitTests
{
    private class FakeClicker : IBackgroundClicker
    {
        public ClickResult NextResult { get; set; } = ClickResult.Success;
        public List<TargetPoint> ClickHistory { get; } = new();
        public List<TargetPoint> DoubleClickHistory { get; } = new();

        public ClickResult Click(TargetPoint target)
        {
            ClickHistory.Add(target);
            return NextResult;
        }

        public ClickResult DoubleClick(TargetPoint target)
        {
            DoubleClickHistory.Add(target);
            return NextResult;
        }
    }

    private class FakeCaptureService : IWindowCaptureService
    {
        public Func<IntPtr, WindowCapture?>? OnCapture { get; set; }

        public WindowCapture? CaptureClientArea(IntPtr hWnd)
        {
            return OnCapture?.Invoke(hWnd);
        }

        public Task<WindowCapture?> CaptureClientAreaAsync(IntPtr hWnd, CancellationToken ct = default)
        {
            return Task.FromResult(CaptureClientArea(hWnd));
        }
    }

    [Fact]
    public async Task ClickAction_CancelledToken_ReturnsCancelled()
    {
        var action = new ClickAction(10, 20);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCaptureService();
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, targetHwnd: (IntPtr)0x1234);

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
        Assert.Empty(fakeClicker.ClickHistory);
    }

    [Fact]
    public async Task ClickAction_InvalidTarget_ReturnsTargetUnavailable()
    {
        var action = new ClickAction(10, 20);
        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCaptureService();
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, targetHwnd: IntPtr.Zero);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(MacroActionStatus.TargetUnavailable, result.Status);
        Assert.Empty(fakeClicker.ClickHistory);
    }

    [Fact]
    public async Task DelayAction_ZeroDelay_ReturnsSuccessImmediately()
    {
        var action = new DelayAction(0);
        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCaptureService();
        var context = new MacroExecutionContext(fakeClicker, fakeCapture);

        var sw = Stopwatch.StartNew();
        var result = await action.ExecuteAsync(context, CancellationToken.None);
        sw.Stop();

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.True(sw.ElapsedMilliseconds < 50);
    }

    [Fact]
    public async Task DelayAction_NegativeDelay_ReturnsInvalidConfiguration()
    {
        var action = new DelayAction(-100);
        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCaptureService();
        var context = new MacroExecutionContext(fakeClicker, fakeCapture);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(MacroActionStatus.InvalidConfiguration, result.Status);
    }

    [Fact]
    public async Task DelayAction_Cancelled_ReturnsCancelled()
    {
        var action = new DelayAction(5000);
        using var cts = new CancellationTokenSource(50);
        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCaptureService();
        var context = new MacroExecutionContext(fakeClicker, fakeCapture);

        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task WaitColorAction_InvalidConfig_ReturnsInvalidConfiguration()
    {
        var actionNegTimeout = new WaitColorAction(10, 10, Color.Red, timeout: TimeSpan.FromSeconds(-1));
        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCaptureService();
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, targetHwnd: (IntPtr)0x1234);

        var res = await actionNegTimeout.ExecuteAsync(context, CancellationToken.None);
        Assert.Equal(MacroActionStatus.InvalidConfiguration, res.Status);

        var actionZeroPoll = new WaitColorAction(10, 10, Color.Red, pollInterval: TimeSpan.Zero);
        var resPoll = await actionZeroPoll.ExecuteAsync(context, CancellationToken.None);
        Assert.Equal(MacroActionStatus.InvalidConfiguration, resPoll.Status);
    }

    [Fact]
    public async Task WaitColorAction_ZeroHwnd_ReturnsTargetUnavailable()
    {
        var action = new WaitColorAction(10, 10, Color.Red);
        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCaptureService();
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, targetHwnd: IntPtr.Zero);

        var res = await action.ExecuteAsync(context, CancellationToken.None);
        Assert.Equal(MacroActionStatus.TargetUnavailable, res.Status);
    }

    [Fact]
    public async Task ClickAction_Success_DispatchesToClicker()
    {
        using var ctrl = new System.Windows.Forms.Control();
        ctrl.CreateControl();
        IntPtr hwnd = ctrl.Handle;

        var action = new ClickAction(25, 35);
        var fakeClicker = new FakeClicker { NextResult = ClickResult.Success };
        var fakeCapture = new FakeCaptureService();
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, targetHwnd: hwnd);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Single(fakeClicker.ClickHistory);
        Assert.Equal(hwnd, fakeClicker.ClickHistory[0].Hwnd);
        Assert.Equal(25, fakeClicker.ClickHistory[0].ClientX);
        Assert.Equal(35, fakeClicker.ClickHistory[0].ClientY);
    }

    [Fact]
    public async Task ClickAction_ClickerFails_ReturnsClickFailed()
    {
        using var ctrl = new System.Windows.Forms.Control();
        ctrl.CreateControl();
        IntPtr hwnd = ctrl.Handle;

        var action = new ClickAction(10, 15);
        var fakeClicker = new FakeClicker { NextResult = ClickResult.PostFailed };
        var fakeCapture = new FakeCaptureService();
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, targetHwnd: hwnd);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(MacroActionStatus.ClickFailed, result.Status);
    }

    [Fact]
    public async Task DoubleClickAction_Success_DispatchesToClicker()
    {
        using var ctrl = new System.Windows.Forms.Control();
        ctrl.CreateControl();
        IntPtr hwnd = ctrl.Handle;

        var action = new DoubleClickAction(40, 50);
        var fakeClicker = new FakeClicker { NextResult = ClickResult.Success };
        var fakeCapture = new FakeCaptureService();
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, targetHwnd: hwnd);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.Single(fakeClicker.DoubleClickHistory);
        Assert.Equal(hwnd, fakeClicker.DoubleClickHistory[0].Hwnd);
        Assert.Equal(40, fakeClicker.DoubleClickHistory[0].ClientX);
        Assert.Equal(50, fakeClicker.DoubleClickHistory[0].ClientY);
    }

    [Fact]
    public async Task WaitColorAction_ImmediateMatch_ReturnsSuccess()
    {
        using var ctrl = new System.Windows.Forms.Control();
        ctrl.CreateControl();
        IntPtr hwnd = ctrl.Handle;

        using var bmp = new Bitmap(100, 100);
        bmp.SetPixel(30, 40, Color.FromArgb(0, 200, 0));

        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCaptureService
        {
            OnCapture = h => new WindowCapture(h, new Bitmap(bmp))
        };
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, targetHwnd: hwnd);

        var action = new WaitColorAction(30, 40, Color.FromArgb(0, 200, 0), tolerance: 2);
        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task WaitColorAction_OutsideCaptureBounds_ReturnsInvalidConfiguration()
    {
        using var ctrl = new System.Windows.Forms.Control();
        ctrl.CreateControl();
        IntPtr hwnd = ctrl.Handle;

        using var bmp = new Bitmap(50, 50);

        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCaptureService
        {
            OnCapture = h => new WindowCapture(h, new Bitmap(bmp))
        };
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, targetHwnd: hwnd);

        // (150, 150) is outside the 50x50 capture bounds
        var action = new WaitColorAction(150, 150, Color.Red);
        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(MacroActionStatus.InvalidConfiguration, result.Status);
    }

    [Fact]
    public async Task WaitColorAction_TargetClosedDuringPoll_ReturnsTargetUnavailable()
    {
        var ctrl = new System.Windows.Forms.Control();
        ctrl.CreateControl();
        IntPtr hwnd = ctrl.Handle;

        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCaptureService
        {
            OnCapture = h =>
            {
                // Simulate window closing on capture attempt
                ctrl.Dispose();
                return null;
            }
        };
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, targetHwnd: hwnd);

        var action = new WaitColorAction(10, 10, Color.Red, timeout: TimeSpan.FromSeconds(2), pollInterval: TimeSpan.FromMilliseconds(50));
        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(MacroActionStatus.TargetUnavailable, result.Status);
    }

    [Fact]
    public async Task WaitColorAction_CaptureFailsOnLiveWindow_ReturnsCaptureFailed()
    {
        using var ctrl = new System.Windows.Forms.Control();
        ctrl.CreateControl();
        IntPtr hwnd = ctrl.Handle;

        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCaptureService
        {
            OnCapture = h => null // Fails to capture even though window is alive
        };
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, targetHwnd: hwnd);

        var action = new WaitColorAction(10, 10, Color.Red, timeout: TimeSpan.FromSeconds(2), pollInterval: TimeSpan.FromMilliseconds(50));
        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(MacroActionStatus.CaptureFailed, result.Status);
    }
}

