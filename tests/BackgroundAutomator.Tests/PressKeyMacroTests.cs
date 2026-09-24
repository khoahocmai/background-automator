using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Targeting;
using Xunit;

namespace BackgroundAutomator.Tests;

public class PressKeyMacroTests
{
    private class FakeClicker : IBackgroundClicker
    {
        public ClickResult Click(TargetPoint target) => ClickResult.Success;
        public ClickResult DoubleClick(TargetPoint target) => ClickResult.Success;
    }

    private class FakeCapture : IWindowCaptureService
    {
        public WindowCapture? CaptureClientArea(IntPtr hWnd) => null;
        public Task<WindowCapture?> CaptureClientAreaAsync(IntPtr hWnd, CancellationToken ct = default) => Task.FromResult<WindowCapture?>(null);
    }

    private class FakeKeyboard : IBackgroundKeyboard
    {
        public KeyPressResult NextResult { get; set; } = KeyPressResult.Success;
        public List<(IntPtr Hwnd, BackgroundKey Key)> KeyHistory { get; } = new();

        public KeyPressResult PressKey(IntPtr hwnd, BackgroundKey key)
        {
            KeyHistory.Add((hwnd, key));
            return NextResult;
        }

        public KeyPressResult PressKey(IntPtr hwnd, uint virtualKey)
        {
            return NextResult;
        }
    }

    [Fact]
    public async Task PressKeyAction_CancelledToken_ReturnsCancelled()
    {
        var action = new PressKeyAction(BackgroundKey.Enter);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var context = new MacroExecutionContext(new FakeClicker(), new FakeCapture(), keyboard: new FakeKeyboard());
        var result = await action.ExecuteAsync(context, cts.Token);

        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task PressKeyAction_ZeroHwnd_ReturnsTargetUnavailable()
    {
        var action = new PressKeyAction(BackgroundKey.Enter);
        var context = new MacroExecutionContext(new FakeClicker(), new FakeCapture(), keyboard: new FakeKeyboard(), targetHwnd: IntPtr.Zero);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.Equal(MacroActionStatus.TargetUnavailable, result.Status);
    }

    [Fact]
    public void PressKeyAction_DisplayString_FormatsCorrectly()
    {
        var action1 = new PressKeyAction(BackgroundKey.Enter);
        Assert.Equal("Press Key Enter", action1.DisplayString);

        var action2 = new PressKeyAction(BackgroundKey.Tab, (IntPtr)0x1234);
        Assert.Contains("Press Key Tab on HWND", action2.DisplayString);
    }

    [Fact]
    public async Task MacroRunner_ExecutesSequenceWithPressKey_Successfully()
    {
        var fakeKeyboard = new FakeKeyboard();
        IntPtr validHwnd = Win32.User32.GetDesktopWindow();
        var fakeClicker = new FakeClicker();
        var fakeCapture = new FakeCapture();
        var context = new MacroExecutionContext(fakeClicker, fakeCapture, keyboard: fakeKeyboard, targetHwnd: validHwnd);
        var runner = new MacroRunner();

        var actions = new List<IMacroAction>
        {
            new ClickAction(10, 20),
            new DelayAction(10),
            new PressKeyAction(BackgroundKey.Enter),
            new PressKeyAction(BackgroundKey.ArrowDown)
        };

        var result = await runner.RunAsync(actions, context);

        Assert.True(result.IsSuccess, $"FinalStatus: {result.FinalStatus}, Message: {result.Message}");
        Assert.Equal(4, result.CompletedActionsCount);
        Assert.Equal(2, fakeKeyboard.KeyHistory.Count);
        Assert.Equal(BackgroundKey.Enter, fakeKeyboard.KeyHistory[0].Key);
        Assert.Equal(BackgroundKey.ArrowDown, fakeKeyboard.KeyHistory[1].Key);
        Assert.Equal(validHwnd, fakeKeyboard.KeyHistory[0].Hwnd);
    }
}
