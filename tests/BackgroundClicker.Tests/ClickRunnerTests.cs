using System.Diagnostics;
using BackgroundClicker.Core.Clicking;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Runner;
using BackgroundClicker.Core.Targeting;
using Xunit;

namespace BackgroundClicker.Tests;

public class ClickRunnerTests
{
    private sealed class MockClicker : IBackgroundClicker
    {
        public List<TargetPoint> ExecutedClicks { get; } = new();
        public List<TargetPoint> ExecutedDoubleClicks { get; } = new();
        public Func<TargetPoint, ClickResult>? CustomClickBehavior { get; set; }

        public ClickResult Click(TargetPoint target)
        {
            ExecutedClicks.Add(target);
            return CustomClickBehavior != null ? CustomClickBehavior(target) : ClickResult.Success;
        }

        public ClickResult DoubleClick(TargetPoint target)
        {
            ExecutedDoubleClicks.Add(target);
            return CustomClickBehavior != null ? CustomClickBehavior(target) : ClickResult.Success;
        }
    }

    [Fact]
    public async Task ClickRunner_ExecutesSinglePoint_CountModeStopsAfterSpecifiedCycles()
    {
        var mockClicker = new MockClicker();
        var logger = new InMemoryLogger();
        using var runner = new ClickRunner(mockClicker, logger);

        var hwnd = new IntPtr(0x1234);
        var point = new ClickPoint(hwnd, 50, 60, ClickType.Single);

        var config = new ClickRunnerConfig
        {
            Points = new[] { point },
            IntervalMilliseconds = 15,
            RepeatMode = RepeatMode.Count,
            RepeatCount = 3
        };

        var completedTcs = new TaskCompletionSource<bool>();
        runner.StateChanged += (s, state) =>
        {
            if (state == RunnerState.Idle && mockClicker.ExecutedClicks.Count == 3)
            {
                completedTcs.TrySetResult(true);
            }
        };

        bool started = runner.Start(config);
        Assert.True(started);
        Assert.Equal(RunnerState.Running, runner.State);

        var delayTask = Task.Delay(2000);
        var completed = await Task.WhenAny(completedTcs.Task, delayTask);
        Assert.Same(completedTcs.Task, completed);

        Assert.Equal(RunnerState.Idle, runner.State);
        Assert.Equal(3, mockClicker.ExecutedClicks.Count);
        Assert.All(mockClicker.ExecutedClicks, p =>
        {
            Assert.Equal(hwnd, p.Hwnd);
            Assert.Equal(50, p.ClientX);
            Assert.Equal(60, p.ClientY);
        });
    }

    [Fact]
    public async Task ClickRunner_ExecutesMultiplePoints_InExactPreservedOrder()
    {
        var mockClicker = new MockClicker();
        using var runner = new ClickRunner(mockClicker);

        var hwnd1 = new IntPtr(0x1000);
        var hwnd2 = new IntPtr(0x2000);
        var hwnd3 = new IntPtr(0x3000);

        var p1 = new ClickPoint(hwnd1, 10, 20, ClickType.Single);
        var p2 = new ClickPoint(hwnd2, 30, 40, ClickType.Double);
        var p3 = new ClickPoint(hwnd3, 50, 60, ClickType.Single);

        var config = new ClickRunnerConfig
        {
            Points = new[] { p1, p2, p3 },
            IntervalMilliseconds = 15,
            RepeatMode = RepeatMode.Count,
            RepeatCount = 2
        };

        var cycleCount = 0;
        var tcs = new TaskCompletionSource<bool>();
        runner.CycleCompleted += (s, c) =>
        {
            cycleCount = c;
            if (c == 2)
            {
                tcs.TrySetResult(true);
            }
        };

        runner.Start(config);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, completed);

        await runner.StopAsync();

        Assert.Equal(2, cycleCount);
        Assert.Equal(4, mockClicker.ExecutedClicks.Count); // p1, p3 on cycle 1 & 2
        Assert.Equal(2, mockClicker.ExecutedDoubleClicks.Count); // p2 on cycle 1 & 2

        // Verify order across cycle 1
        Assert.Equal(hwnd1, mockClicker.ExecutedClicks[0].Hwnd);
        Assert.Equal(hwnd2, mockClicker.ExecutedDoubleClicks[0].Hwnd);
        Assert.Equal(hwnd3, mockClicker.ExecutedClicks[1].Hwnd);

        // Cycle 2
        Assert.Equal(hwnd1, mockClicker.ExecutedClicks[2].Hwnd);
        Assert.Equal(hwnd2, mockClicker.ExecutedDoubleClicks[1].Hwnd);
        Assert.Equal(hwnd3, mockClicker.ExecutedClicks[3].Hwnd);
    }

    [Fact]
    public async Task ClickRunner_DoubleStart_RejectedAndDoesNotSpawnDuplicateTasks()
    {
        var mockClicker = new MockClicker();
        using var runner = new ClickRunner(mockClicker);

        var config = new ClickRunnerConfig
        {
            Points = new[] { new ClickPoint(new IntPtr(0x1234), 10, 10) },
            IntervalMilliseconds = 50,
            RepeatMode = RepeatMode.UntilStopped
        };

        bool firstStart = runner.Start(config);
        bool secondStart = runner.Start(config);

        Assert.True(firstStart);
        Assert.False(secondStart);
        Assert.Equal(RunnerState.Running, runner.State);

        await runner.StopAsync();
        Assert.Equal(RunnerState.Idle, runner.State);
    }

    [Fact]
    public async Task ClickRunner_Cancellation_StopsActiveDelayPromptly()
    {
        var mockClicker = new MockClicker();
        using var runner = new ClickRunner(mockClicker);

        // Long interval (5000ms)
        var config = new ClickRunnerConfig
        {
            Points = new[] { new ClickPoint(new IntPtr(0x1234), 10, 10) },
            IntervalMilliseconds = 5000,
            RepeatMode = RepeatMode.UntilStopped
        };

        var sw = Stopwatch.StartNew();
        runner.Start(config);

        // Wait a tiny bit to ensure the loop entered Task.Delay
        await Task.Delay(50);

        // Request stop
        await runner.StopAsync();
        sw.Stop();

        Assert.Equal(RunnerState.Idle, runner.State);
        // Ensure shutdown was prompt and didn't wait anywhere near 5000ms
        Assert.True(sw.ElapsedMilliseconds < 1500, $"Shutdown took {sw.ElapsedMilliseconds}ms, expected < 1500ms");
    }

    [Fact]
    public async Task ClickRunner_CanRestart_AfterCancellation()
    {
        var mockClicker = new MockClicker();
        using var runner = new ClickRunner(mockClicker);

        var config = new ClickRunnerConfig
        {
            Points = new[] { new ClickPoint(new IntPtr(0x1234), 10, 10) },
            IntervalMilliseconds = 15,
            RepeatMode = RepeatMode.UntilStopped
        };

        // Run 1
        runner.Start(config);
        await Task.Delay(60);
        await runner.StopAsync();
        Assert.Equal(RunnerState.Idle, runner.State);

        int clicksAfterRun1 = mockClicker.ExecutedClicks.Count;
        Assert.True(clicksAfterRun1 > 0);

        // Run 2
        runner.Start(config);
        await Task.Delay(60);
        await runner.StopAsync();
        Assert.Equal(RunnerState.Idle, runner.State);

        int clicksAfterRun2 = mockClicker.ExecutedClicks.Count;
        Assert.True(clicksAfterRun2 > clicksAfterRun1);
    }

    [Fact]
    public async Task ClickRunner_TargetClosedDuringExecution_StopsSafelyAndFiresError()
    {
        var mockClicker = new MockClicker();
        var logger = new InMemoryLogger();
        using var runner = new ClickRunner(mockClicker, logger);

        var callCount = 0;
        mockClicker.CustomClickBehavior = pt =>
        {
            callCount++;
            if (callCount >= 2)
            {
                return ClickResult.InvalidTarget; // Simulates window being closed on second click
            }
            return ClickResult.Success;
        };

        var config = new ClickRunnerConfig
        {
            Points = new[] { new ClickPoint(new IntPtr(0x1234), 10, 10) },
            IntervalMilliseconds = 15,
            RepeatMode = RepeatMode.UntilStopped
        };

        string? receivedError = null;
        var tcs = new TaskCompletionSource<bool>();
        runner.ErrorOccurred += (s, err) =>
        {
            receivedError = err;
        };

        runner.StateChanged += (s, state) =>
        {
            if (state == RunnerState.Idle && callCount >= 2)
            {
                tcs.TrySetResult(true);
            }
        };

        runner.Start(config);

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(2000));
        Assert.Same(tcs.Task, completed);

        Assert.Equal(RunnerState.Idle, runner.State);
        Assert.NotNull(receivedError);
        Assert.Contains("Target unavailable", receivedError);
    }
}
