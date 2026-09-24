using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Targeting;
using Xunit;

namespace BackgroundAutomator.Tests;

public class MacroRunnerTests
{
    private class ActionTracker : IMacroAction
    {
        public string Name { get; }
        public string DisplayString => Name;
        public List<string> ExecutionLog { get; }
        public MacroActionResult ResultToReturn { get; set; }
        public int DelayBeforeCompleteMs { get; set; }

        public ActionTracker(string name, List<string> executionLog, MacroActionResult? result = null, int delayMs = 0)
        {
            Name = name;
            ExecutionLog = executionLog;
            ResultToReturn = result ?? MacroActionResult.Success();
            DelayBeforeCompleteMs = delayMs;
        }

        public async Task<MacroActionResult> ExecuteAsync(MacroExecutionContext context, CancellationToken ct)
        {
            ExecutionLog.Add($"Start:{Name}");
            if (DelayBeforeCompleteMs > 0)
            {
                await Task.Delay(DelayBeforeCompleteMs, ct);
            }
            ExecutionLog.Add($"End:{Name}");
            return ResultToReturn;
        }
    }

    private class DummyClicker : IBackgroundClicker
    {
        public ClickResult Click(TargetPoint target) => ClickResult.Success;
        public ClickResult DoubleClick(TargetPoint target) => ClickResult.Success;
    }

    private class DummyCaptureService : IWindowCaptureService
    {
        public WindowCapture? CaptureClientArea(IntPtr hWnd) => null;
        public Task<WindowCapture?> CaptureClientAreaAsync(IntPtr hWnd, CancellationToken ct = default) => Task.FromResult<WindowCapture?>(null);
    }

    [Fact]
    public async Task MacroRunner_ExecutesActionsSequentially_InExactOrder()
    {
        using var runner = new MacroRunner();
        var executionLog = new List<string>();

        var action1 = new ActionTracker("Action1", executionLog);
        var action2 = new ActionTracker("Action2", executionLog);
        var action3 = new ActionTracker("Action3", executionLog);

        var actions = new List<IMacroAction> { action1, action2, action3 };
        var context = new MacroExecutionContext(new DummyClicker(), new DummyCaptureService());

        var result = await runner.RunAsync(actions, context);

        Assert.True(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Success, result.FinalStatus);
        Assert.Equal(3, result.CompletedActionsCount);
        Assert.Equal(3, result.TotalActionsCount);

        var expectedLog = new[]
        {
            "Start:Action1", "End:Action1",
            "Start:Action2", "End:Action2",
            "Start:Action3", "End:Action3"
        };
        Assert.Equal(expectedLog, executionLog);
    }

    [Fact]
    public async Task MacroRunner_PreventsDoubleStart_ThrowsInvalidOperationException()
    {
        using var runner = new MacroRunner();
        var executionLog = new List<string>();

        var longAction = new ActionTracker("LongAction", executionLog, delayMs: 500);
        var context = new MacroExecutionContext(new DummyClicker(), new DummyCaptureService());

        Task<MacroExecutionResult> runTask = runner.RunAsync(new[] { longAction }, context);

        // Wait slightly for state to become Running
        await Task.Delay(50);
        Assert.Equal(MacroRunnerState.Running, runner.State);

        // Attempt second run while first is active
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.RunAsync(new[] { new ActionTracker("Second", executionLog) }, context));

        runner.Stop();
        await runTask;
    }

    [Fact]
    public async Task MacroRunner_Stop_CancelsExecutionAndTransitionsToIdle()
    {
        using var runner = new MacroRunner();
        var executionLog = new List<string>();

        var stateChanges = new List<MacroRunnerState>();
        runner.StateChanged += s => stateChanges.Add(s);

        var longAction = new ActionTracker("LongAction", executionLog, delayMs: 1000);
        var context = new MacroExecutionContext(new DummyClicker(), new DummyCaptureService());

        Task<MacroExecutionResult> runTask = runner.RunAsync(new[] { longAction }, context);

        await Task.Delay(50);
        runner.Stop();

        var result = await runTask;

        Assert.Equal(MacroActionStatus.Cancelled, result.FinalStatus);
        Assert.Equal(MacroRunnerState.Idle, runner.State);
        Assert.Contains(MacroRunnerState.Running, stateChanges);
        Assert.Contains(MacroRunnerState.Stopping, stateChanges);
        Assert.Contains(MacroRunnerState.Idle, stateChanges);
    }

    [Fact]
    public async Task MacroRunner_ShortCircuitsOnActionFailure()
    {
        using var runner = new MacroRunner();
        var executionLog = new List<string>();

        var action1 = new ActionTracker("Action1", executionLog);
        var actionFail = new ActionTracker("ActionFail", executionLog, result: MacroActionResult.Timeout("Timeout occurred"));
        var action3 = new ActionTracker("Action3", executionLog);

        var context = new MacroExecutionContext(new DummyClicker(), new DummyCaptureService());
        var result = await runner.RunAsync(new[] { action1, actionFail, action3 }, context);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.Timeout, result.FinalStatus);
        Assert.Equal(1, result.CompletedActionsCount);
        Assert.Equal(3, result.TotalActionsCount);

        // Action3 must NOT have run
        Assert.DoesNotContain("Start:Action3", executionLog);
    }

    [Fact]
    public async Task MacroRunner_Restartability_CanRunMultipleTimes()
    {
        using var runner = new MacroRunner();
        var log = new List<string>();
        var context = new MacroExecutionContext(new DummyClicker(), new DummyCaptureService());

        // First run
        var res1 = await runner.RunAsync(new[] { new ActionTracker("Run1", log) }, context);
        Assert.True(res1.IsSuccess);
        Assert.Equal(MacroRunnerState.Idle, runner.State);

        // Second run
        var res2 = await runner.RunAsync(new[] { new ActionTracker("Run2", log) }, context);
        Assert.True(res2.IsSuccess);
        Assert.Equal(MacroRunnerState.Idle, runner.State);

        Assert.Equal(new[] { "Start:Run1", "End:Run1", "Start:Run2", "End:Run2" }, log);
    }

    [Theory]
    [InlineData(MacroActionStatus.CaptureFailed)]
    [InlineData(MacroActionStatus.TargetUnavailable)]
    public async Task MacroRunner_ShortCircuitsOnTerminalCaptureStatuses(MacroActionStatus failureStatus)
    {
        using var runner = new MacroRunner();
        var executionLog = new List<string>();

        var failureResult = failureStatus == MacroActionStatus.CaptureFailed
            ? MacroActionResult.CaptureFailed("Capture failed")
            : MacroActionResult.TargetUnavailable("Target closed");

        var action1 = new ActionTracker("Action1", executionLog);
        var actionFail = new ActionTracker("ActionFail", executionLog, result: failureResult);
        var action3 = new ActionTracker("Action3", executionLog);

        var context = new MacroExecutionContext(new DummyClicker(), new DummyCaptureService());
        var result = await runner.RunAsync(new[] { action1, actionFail, action3 }, context);

        Assert.False(result.IsSuccess);
        Assert.Equal(failureStatus, result.FinalStatus);
        Assert.Equal(1, result.CompletedActionsCount);
        Assert.DoesNotContain("Start:Action3", executionLog);
    }
}
