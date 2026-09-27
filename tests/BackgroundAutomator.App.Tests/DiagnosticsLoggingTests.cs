using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackgroundAutomator.App.Models;
using BackgroundAutomator.App.Services;
using BackgroundAutomator.App.ViewModels;
using BackgroundAutomator.App.Views;
using BackgroundAutomator.Core.Logging;
using Xunit;

namespace BackgroundAutomator.App.Tests;

public class DiagnosticsLoggingTests
{
    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void InvokeAsync(Action action) => action();
    }

    private sealed class DedicatedThreadDispatcher : IUiDispatcher, IDisposable
    {
        private readonly BlockingCollection<Action> _queue = new();
        private readonly Thread _uiThread;
        private readonly CancellationTokenSource _cts = new();

        public int UiThreadId => _uiThread.ManagedThreadId;
        public List<int> MutationThreadIds { get; } = new();
        private readonly object _lock = new();

        public DedicatedThreadDispatcher()
        {
            _uiThread = new Thread(ProcessQueue)
            {
                IsBackground = true,
                Name = "SimulatedWpfUiThread"
            };
            _uiThread.Start();
        }

        private void ProcessQueue()
        {
            try
            {
                foreach (var action in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    lock (_lock)
                    {
                        MutationThreadIds.Add(Thread.CurrentThread.ManagedThreadId);
                    }
                    action();
                }
            }
            catch (OperationCanceledException) { }
        }

        public void InvokeAsync(Action action)
        {
            if (!_cts.IsCancellationRequested)
            {
                try
                {
                    _queue.Add(action);
                }
                catch (InvalidOperationException) { }
            }
        }

        public async Task<bool> WaitForDrainedAsync(DiagnosticsViewModel vm, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                if (vm.PendingQueueCount == 0 && _queue.Count == 0)
                {
                    var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    InvokeAsync(() => tcs.TrySetResult());
                    await Task.WhenAny(tcs.Task, Task.Delay(2000));
                    if (tcs.Task.IsCompletedSuccessfully && vm.PendingQueueCount == 0 && _queue.Count == 0)
                    {
                        return true;
                    }
                }
                await Task.Delay(15);
            }
            return false;
        }

        public void Dispose()
        {
            _cts.Cancel();
            _queue.CompleteAdding();
            _uiThread.Join(TimeSpan.FromSeconds(3));
            _queue.Dispose();
            _cts.Dispose();
        }
    }

    [Fact]
    public async Task Stress_8ConcurrentProducers_2000Messages_AllMutationsSerializedOnDispatcher()
    {
        IAppLogger logger = new InMemoryLogger(maxEntries: 5000);
        using var dispatcher = new DedicatedThreadDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 1000);

        const int producerCount = 8;
        const int messagesPerProducer = 250;
        var startGate = new ManualResetEventSlim(false);

        var tasks = Enumerable.Range(0, producerCount).Select(producerId => Task.Run(() =>
        {
            startGate.Wait();
            for (int i = 0; i < messagesPerProducer; i++)
            {
                logger.Info($"Producer {producerId} - Message {i}");
            }
        })).ToArray();

        startGate.Set();
        await Task.WhenAll(tasks);

        bool drained = await dispatcher.WaitForDrainedAsync(vm, TimeSpan.FromSeconds(10));
        Assert.True(drained, "Queue should drain within 10 seconds under 2000 message load.");

        Assert.Equal(1000, vm.AllEntries.Count);
        Assert.Equal(1000, vm.FilteredEntries.Count);
        Assert.Equal(0, vm.PendingQueueCount);

        // Verify all mutations executed strictly on the dedicated UI thread
        lock (dispatcher.MutationThreadIds)
        {
            Assert.NotEmpty(dispatcher.MutationThreadIds);
            Assert.All(dispatcher.MutationThreadIds, tid => Assert.Equal(dispatcher.UiThreadId, tid));
        }
    }

    [Fact]
    public async Task AddRemoveConsistency_TrimmingClearAndReAdd_MaintainsExactState()
    {
        IAppLogger logger = new InMemoryLogger(maxEntries: 5000);
        using var dispatcher = new DedicatedThreadDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 200);

        // Step 1: Enqueue 300 messages -> trimming should cap at 200
        for (int i = 0; i < 300; i++)
        {
            logger.Info($"Batch1-{i}");
        }
        Assert.True(await dispatcher.WaitForDrainedAsync(vm, TimeSpan.FromSeconds(5)));
        Assert.Equal(200, vm.AllEntries.Count);
        Assert.Equal("Batch1-100", vm.AllEntries[0].Message);
        Assert.Equal("Batch1-299", vm.AllEntries[^1].Message);

        // Step 2: ClearLog command
        vm.ClearLogCommand.Execute(null);
        Assert.True(await dispatcher.WaitForDrainedAsync(vm, TimeSpan.FromSeconds(5)));
        Assert.Empty(vm.AllEntries);
        Assert.Empty(vm.FilteredEntries);

        // Step 3: Enqueue more messages after clear
        for (int i = 0; i < 50; i++)
        {
            logger.Info($"Batch2-{i}");
        }
        Assert.True(await dispatcher.WaitForDrainedAsync(vm, TimeSpan.FromSeconds(5)));
        Assert.Equal(50, vm.AllEntries.Count);
        Assert.Equal("Batch2-0", vm.AllEntries[0].Message);
        Assert.Equal("Batch2-49", vm.AllEntries[^1].Message);
    }

    [Fact]
    public async Task FilterStress_ConcurrentLoggingWhileFilterChanges_RemainsConsistent()
    {
        IAppLogger logger = new InMemoryLogger(maxEntries: 5000);
        using var dispatcher = new DedicatedThreadDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 1000);

        var cts = new CancellationTokenSource();
        var produceTask = Task.Run(() =>
        {
            int i = 0;
            while (!cts.IsCancellationRequested && i < 1000)
            {
                var level = (LogLevel)(i % 4);
                logger.Log(level, $"Message {i} level {level}");
                i++;
                Thread.Sleep(1);
            }
        });

        // Rapidly change filter from another thread while logs are arriving
        string[] filters = ["All", "Info", "Warning", "Error", "Debug"];
        for (int i = 0; i < 30; i++)
        {
            vm.SelectedFilter = filters[i % filters.Length];
            await Task.Delay(15);
        }

        cts.Cancel();
        await produceTask;

        // Switch back to "All" and verify consistency
        vm.SelectedFilter = "All";
        Assert.True(await dispatcher.WaitForDrainedAsync(vm, TimeSpan.FromSeconds(10)));

        Assert.True(vm.AllEntries.Count > 0);
        Assert.Equal(vm.AllEntries.Count, vm.FilteredEntries.Count);
    }

    [Fact]
    public void AutoScroll_True_BatchOfVisibleItems_RaisesExactlyOneScrollRequestPerBatch()
    {
        IAppLogger logger = new InMemoryLogger();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 500)
        {
            AutoScroll = true
        };

        var scrollTargets = new List<LogMessageItem>();
        vm.ScrollRequested += item => scrollTargets.Add(item);

        logger.Info("Message 1");
        logger.Info("Message 2");
        logger.Info("Message 3");

        Assert.Equal(3, scrollTargets.Count);
        Assert.Equal("Message 1", scrollTargets[0].Message);
        Assert.Equal("Message 2", scrollTargets[1].Message);
        Assert.Equal("Message 3", scrollTargets[2].Message);
    }

    [Fact]
    public void AutoScroll_False_ZeroScrollRequestsRaised()
    {
        IAppLogger logger = new InMemoryLogger();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 500)
        {
            AutoScroll = false
        };

        int scrollCount = 0;
        vm.ScrollRequested += _ => scrollCount++;

        logger.Info("Message 1");
        logger.Info("Message 2");
        logger.Info("Message 3");

        Assert.Equal(0, scrollCount);
    }

    [Fact]
    public void AutoScroll_FilteredHiddenMessages_ZeroScrollRequestsRaised()
    {
        IAppLogger logger = new InMemoryLogger();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 500)
        {
            SelectedFilter = "Error",
            AutoScroll = true
        };

        int scrollCount = 0;
        vm.ScrollRequested += _ => scrollCount++;

        // Info message does not match Error filter
        logger.Info("Info message (should be hidden)");
        Assert.Equal(0, scrollCount);
        Assert.Empty(vm.FilteredEntries);

        // Error message matches Error filter
        logger.Error("Error message (should be visible)");
        Assert.Equal(1, scrollCount);
        Assert.Single(vm.FilteredEntries);
        Assert.Equal("Error message (should be visible)", vm.FilteredEntries[0].Message);
    }

    [Fact]
    public void AutoScroll_ToggleFromFalseToTrue_ImmediatelyRequestsScrollToLatestVisible()
    {
        IAppLogger logger = new InMemoryLogger();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 500)
        {
            AutoScroll = false
        };

        logger.Info("Message 1");
        logger.Info("Message 2");

        LogMessageItem? requestedItem = null;
        vm.ScrollRequested += item => requestedItem = item;

        // Toggle AutoScroll ON
        vm.AutoScroll = true;

        Assert.NotNull(requestedItem);
        Assert.Equal("Message 2", requestedItem.Message);
    }

    [Fact]
    public void ClearLog_WhileLogsPending_EmptiesQueueAndCollectionsWithoutStaleReplay()
    {
        IAppLogger logger = new InMemoryLogger();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 500);

        logger.Info("Old Message 1");
        logger.Info("Old Message 2");
        Assert.Equal(2, vm.AllEntries.Count);

        vm.ClearLogCommand.Execute(null);

        Assert.Empty(vm.AllEntries);
        Assert.Empty(vm.FilteredEntries);
        Assert.Equal(0, vm.PendingQueueCount);

        logger.Info("New Message after clear");
        Assert.Single(vm.AllEntries);
        Assert.Equal("New Message after clear", vm.AllEntries[0].Message);
    }

    [Fact]
    public void Filtering_PreservesAllRetainedEntriesWhenSwitchingBackToAll()
    {
        IAppLogger logger = new InMemoryLogger();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 10);

        logger.Info("Info 1");
        logger.Warning("Warn 1");
        logger.Error("Err 1");
        logger.Debug("Dbg 1");

        Assert.Equal(4, vm.AllEntries.Count);
        Assert.Equal(4, vm.FilteredEntries.Count);

        // Filter to Warning only
        vm.SelectedFilter = "Warning";
        Assert.Single(vm.FilteredEntries);
        Assert.Equal("Warn 1", vm.FilteredEntries[0].Message);

        // Filter back to All
        vm.SelectedFilter = "All";
        Assert.Equal(4, vm.FilteredEntries.Count);
        Assert.Equal("Info 1", vm.FilteredEntries[0].Message);
        Assert.Equal("Dbg 1", vm.FilteredEntries[3].Message);
    }

    [Fact]
    public void RealWpfDiagnosticsPage_VisualTree_2500MessagesStress_NoItemsControlCrash()
    {
        Exception? threadEx = null;
        var thread = new Thread(() =>
        {
            try
            {
                IAppLogger logger = new InMemoryLogger(maxEntries: 5000);
                var dispatcher = new WpfUiDispatcher(System.Windows.Threading.Dispatcher.CurrentDispatcher);
                using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 1000)
                {
                    AutoScroll = true
                };

                var page = new DiagnosticsPage(vm);
                var window = new System.Windows.Window
                {
                    Content = page,
                    Width = 800,
                    Height = 600,
                    ShowInTaskbar = false
                };

                window.Show();
                PumpDispatcher();

                // Concurrently produce 2500 messages (> 2x MaxEntries of 1000) from 4 worker threads
                const int threads = 4;
                const int perThread = 625;
                var tasks = Enumerable.Range(0, threads).Select(t => Task.Run(() =>
                {
                    for (int i = 0; i < perThread; i++)
                    {
                        logger.Info($"STA Test Thread {t} - Message {i}");
                        if (i % 25 == 0) Thread.Sleep(1);
                    }
                })).ToArray();

                // Pump dispatcher while producers run to trigger layout and virtualization
                while (!Task.WaitAll(tasks, 50))
                {
                    PumpDispatcher();
                }

                // Drain all pending items
                var sw = Stopwatch.StartNew();
                while ((vm.PendingQueueCount > 0 || vm.AllEntries.Count < 1000) && sw.Elapsed < TimeSpan.FromSeconds(10))
                {
                    PumpDispatcher();
                    Thread.Sleep(10);
                }

                page.UpdateLayout();
                PumpDispatcher();

                // Assert: No crash, max entries capped at 1000, real ListBox reflects exact count
                Assert.Equal(1000, vm.AllEntries.Count);
                Assert.Equal(1000, page.LogItemsControl.Items.Count);

                // Clear while bound
                vm.ClearLogCommand.Execute(null);
                PumpDispatcher();
                page.UpdateLayout();

                Assert.Empty(vm.AllEntries);
                Assert.Empty(page.LogItemsControl.Items);

                // Re-add 100 items
                for (int i = 0; i < 100; i++)
                {
                    logger.Info($"Post-Clear Message {i}");
                }

                sw.Restart();
                while (vm.PendingQueueCount > 0 && sw.Elapsed < TimeSpan.FromSeconds(5))
                {
                    PumpDispatcher();
                    Thread.Sleep(10);
                }

                page.UpdateLayout();
                PumpDispatcher();

                Assert.Equal(100, vm.AllEntries.Count);
                Assert.Equal(100, page.LogItemsControl.Items.Count);

                window.Close();
                PumpDispatcher();
            }
            catch (Exception ex)
            {
                threadEx = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(30));

        if (threadEx != null)
        {
            throw new AggregateException("STA thread encountered an exception", threadEx);
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new Action<System.Windows.Threading.DispatcherFrame>(f => f.Continue = false),
            frame);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
