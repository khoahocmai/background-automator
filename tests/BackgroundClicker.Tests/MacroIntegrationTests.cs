using System.Diagnostics;
using System.Drawing;
using BackgroundClicker.Core.Capture;
using BackgroundClicker.Core.Clicking;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Macro;
using BackgroundClicker.Core.Targeting;
using Xunit;

namespace BackgroundClicker.Tests;

public class MacroIntegrationTests
{
    private readonly InMemoryLogger _logger = new();

    [Fact]
    public async Task WaitColor_ImmediateMatch_SucceedsFast()
    {
        using var fixture = new TestTargetFixture();
        Assert.NotEqual(IntPtr.Zero, fixture.ColorPanelHwnd);

        var clicker = new BackgroundClickerEngine(_logger);
        var captureService = new GdiWindowCaptureService(_logger);
        var context = new MacroExecutionContext(clicker, captureService, _logger, fixture.ColorPanelHwnd);

        // TestTarget ColorPanel defaults to Red (255, 0, 0)
        var action = new WaitColorAction(
            clientX: 20,
            clientY: 20,
            targetColor: Color.FromArgb(255, 0, 0),
            tolerance: 5,
            timeout: TimeSpan.FromSeconds(2),
            pollInterval: TimeSpan.FromMilliseconds(30));

        var sw = Stopwatch.StartNew();
        MacroActionResult result = await action.ExecuteAsync(context, CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Success, result.Status);
        Assert.True(sw.ElapsedMilliseconds < 500, $"Expected immediate match in <500ms, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task WaitColor_DelayedMatch_SucceedsAfterDelay()
    {
        // Start TestTarget configured to switch to green after 300ms
        using var fixture = new TestTargetFixture("--change-color-after 300:green");
        Assert.NotEqual(IntPtr.Zero, fixture.ColorPanelHwnd);

        var clicker = new BackgroundClickerEngine(_logger);
        var captureService = new GdiWindowCaptureService(_logger);
        var context = new MacroExecutionContext(clicker, captureService, _logger, fixture.ColorPanelHwnd);

        var action = new WaitColorAction(
            clientX: 20,
            clientY: 20,
            targetColor: Color.FromArgb(0, 255, 0),
            tolerance: 10,
            timeout: TimeSpan.FromSeconds(4),
            pollInterval: TimeSpan.FromMilliseconds(40));

        var sw = Stopwatch.StartNew();
        MacroActionResult result = await action.ExecuteAsync(context, CancellationToken.None);
        sw.Stop();

        Assert.True(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Success, result.Status);
        // It must have waited at least 250ms for the color change
        Assert.True(sw.ElapsedMilliseconds >= 200, $"Expected delay >= 200ms, got {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task WaitColor_Timeout_FailsGracefully()
    {
        using var fixture = new TestTargetFixture();
        Assert.NotEqual(IntPtr.Zero, fixture.ColorPanelHwnd);

        var clicker = new BackgroundClickerEngine(_logger);
        var captureService = new GdiWindowCaptureService(_logger);
        var context = new MacroExecutionContext(clicker, captureService, _logger, fixture.ColorPanelHwnd);

        // ColorPanel is Red; wait for Magenta (255, 0, 255) with 250ms timeout
        var action = new WaitColorAction(
            clientX: 20,
            clientY: 20,
            targetColor: Color.FromArgb(255, 0, 255),
            tolerance: 5,
            timeout: TimeSpan.FromMilliseconds(250),
            pollInterval: TimeSpan.FromMilliseconds(30));

        MacroActionResult result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Timeout, result.Status);
        Assert.NotNull(result.Message);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WaitColor_Cancellation_CancelsImmediately()
    {
        using var fixture = new TestTargetFixture();
        Assert.NotEqual(IntPtr.Zero, fixture.ColorPanelHwnd);

        var clicker = new BackgroundClickerEngine(_logger);
        var captureService = new GdiWindowCaptureService(_logger);
        var context = new MacroExecutionContext(clicker, captureService, _logger, fixture.ColorPanelHwnd);

        var action = new WaitColorAction(
            clientX: 20,
            clientY: 20,
            targetColor: Color.FromArgb(0, 0, 255),
            tolerance: 5,
            timeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(50));

        using var cts = new CancellationTokenSource(60);

        MacroActionResult result = await action.ExecuteAsync(context, cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task FullDeterministicMacroPipeline_Click_Delay_WaitColor_DoubleClick()
    {
        using var fixture = new TestTargetFixture();
        Assert.NotEqual(IntPtr.Zero, fixture.DelayGreenButtonHwnd);
        Assert.NotEqual(IntPtr.Zero, fixture.ColorPanelHwnd);
        Assert.NotEqual(IntPtr.Zero, fixture.ButtonHwnd);

        var clicker = new BackgroundClickerEngine(_logger);
        var captureService = new GdiWindowCaptureService(_logger);
        var context = new MacroExecutionContext(clicker, captureService, _logger, fixture.MainWindowHandle);

        using var runner = new MacroRunner(_logger);

        // Sequence of actions:
        // 1. Click "Delay Green 400ms" button (triggers TestTarget to switch ColorPanel to green after 400ms)
        // 2. Delay 100ms
        // 3. WaitColor waiting for Green on ColorPanel
        // 4. DoubleClick on "Target Button"
        var actions = new List<IMacroAction>
        {
            new ClickAction(15, 12, overrideHwnd: fixture.DelayGreenButtonHwnd),
            new DelayAction(100),
            new WaitColorAction(20, 20, Color.FromArgb(0, 255, 0), tolerance: 10, timeout: TimeSpan.FromSeconds(4), overrideHwnd: fixture.ColorPanelHwnd),
            new DoubleClickAction(25, 18, overrideHwnd: fixture.ButtonHwnd)
        };

        var completedActions = new List<string>();
        runner.ActionCompleted += (idx, act, res) =>
        {
            completedActions.Add($"{idx}:{act.Name}:{res.Status}");
        };

        MacroExecutionResult executionResult = await runner.RunAsync(actions, context);

        Assert.True(executionResult.IsSuccess, $"Macro failed with message: {executionResult.Message}");
        Assert.Equal(MacroActionStatus.Success, executionResult.FinalStatus);
        Assert.Equal(4, executionResult.CompletedActionsCount);
        Assert.Equal(4, executionResult.TotalActionsCount);

        Assert.Equal(4, completedActions.Count);
        Assert.Equal("0:Click:Success", completedActions[0]);
        Assert.Equal("1:Delay:Success", completedActions[1]);
        Assert.Equal("2:WaitColor:Success", completedActions[2]);
        Assert.Equal("3:DoubleClick:Success", completedActions[3]);
    }
}
