using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using Xunit;

namespace BackgroundAutomator.Tests;

public class BackgroundKeyboardEngineTests
{
    [Fact]
    public void PressKey_InvalidTargetHwnd_ReturnsInvalidTarget()
    {
        var logger = new InMemoryLogger();
        var engine = new BackgroundKeyboardEngine(logger);

        var result = engine.PressKey(IntPtr.Zero, BackgroundKey.Enter);
        Assert.Equal(KeyPressResult.InvalidTarget, result);

        var logs = logger.Entries;
        Assert.Contains(logs, l => l.Message.Contains("is invalid or closed"));
    }

    [Fact]
    public void PressKey_ClosedWindowHwnd_ReturnsInvalidTarget()
    {
        var engine = new BackgroundKeyboardEngine();

        // 0x12345678 is presumably not a valid live HWND
        var result = engine.PressKey((IntPtr)0x12345678, BackgroundKey.Enter);
        Assert.Equal(KeyPressResult.InvalidTarget, result);
    }
}
