using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BackgroundAutomator.App.Models;
using BackgroundAutomator.App.Services;
using BackgroundAutomator.App.ViewModels;
using BackgroundAutomator.Core.Logging;
using Xunit;

namespace BackgroundAutomator.App.Tests;

public class DiagnosticsCopyTests
{
    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void InvokeAsync(Action action) => action();
    }

    private sealed class FakeClipboardService : IClipboardService
    {
        public string? LastText { get; private set; }
        public int SetTextCallCount { get; private set; }
        public bool ShouldFail { get; set; }

        public bool SetText(string text)
        {
            SetTextCallCount++;
            if (ShouldFail)
                return false;

            LastText = text;
            return true;
        }

        public string? GetText() => LastText;
    }

    // Part D #28: Selected Rows (2, 4, 7) copied in chronological order with full text
    [Fact]
    public void PartD_28_SelectedRows_CopiesExactRowsInChronologicalOrder_WithFullText()
    {
        IAppLogger logger = new InMemoryLogger();
        var clipboard = new FakeClipboardService();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 100, clipboardService: clipboard);

        for (int i = 0; i < 10; i++)
        {
            logger.Info($"Log entry message {i}");
        }
        vm.FlushDrain();

        // Pass entries in non-chronological order: 7, 2, 4
        var selected = new List<LogMessageItem>
        {
            vm.AllEntries[7],
            vm.AllEntries[2],
            vm.AllEntries[4]
        };

        vm.CopySelected(selected);

        Assert.NotNull(clipboard.LastText);
        var lines = clipboard.LastText.Split(Environment.NewLine);
        Assert.Equal(3, lines.Length);

        // Must be in chronological order: 2, 4, 7
        Assert.Contains("Log entry message 2", lines[0]);
        Assert.Contains("Log entry message 4", lines[1]);
        Assert.Contains("Log entry message 7", lines[2]);

        Assert.Contains("Copied 3 selected log lines.", vm.CopyFeedback);
    }

    // Part D #29: Visible Filter (DEBUG, INFO, WARN, ERROR) -> filter WARN -> Copy Visible copies WARN only
    [Fact]
    public void PartD_29_VisibleFilter_CopiesOnlyEntriesMatchingFilter()
    {
        IAppLogger logger = new InMemoryLogger();
        var clipboard = new FakeClipboardService();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 100, clipboardService: clipboard);

        logger.Debug("Debug test message");
        logger.Info("Info test message");
        logger.Warning("Warning test message 1");
        logger.Warning("Warning test message 2");
        logger.Error("Error test message");
        vm.FlushDrain();

        vm.SelectedFilter = "Warning";

        vm.CopyVisible();

        Assert.NotNull(clipboard.LastText);
        var lines = clipboard.LastText.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, l =>
        {
            Assert.Contains("[WARN]", l);
            Assert.Contains("Warning test message", l);
        });

        Assert.Contains("Copied 2 visible log lines.", vm.CopyFeedback);
    }

    // Part D #30: Copy All While Filtered copies all retained entries regardless of filter
    [Fact]
    public void PartD_30_CopyAllWhileFiltered_CopiesAllRetainedEntries()
    {
        IAppLogger logger = new InMemoryLogger();
        var clipboard = new FakeClipboardService();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 100, clipboardService: clipboard);

        logger.Debug("Debug message");
        logger.Info("Info message");
        logger.Warning("Warning message");
        logger.Error("Error message");
        vm.FlushDrain();

        vm.SelectedFilter = "Warning";
        Assert.Single(vm.FilteredEntries);

        vm.CopyAll();

        Assert.NotNull(clipboard.LastText);
        var lines = clipboard.LastText.Split(Environment.NewLine);
        Assert.Equal(4, lines.Length);
        Assert.Contains(lines, l => l.Contains("[DEBUG]"));
        Assert.Contains(lines, l => l.Contains("[INFO]"));
        Assert.Contains(lines, l => l.Contains("[WARN]"));
        Assert.Contains(lines, l => l.Contains("[ERROR]"));

        Assert.Contains("Copied all 4 retained log lines.", vm.CopyFeedback);
    }

    // Part D #31: Long Message horizontally clipped in UI is copied in full
    [Fact]
    public void PartD_31_LongMessage_CopiesFullUnclippedMessage()
    {
        IAppLogger logger = new InMemoryLogger();
        var clipboard = new FakeClipboardService();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 100, clipboardService: clipboard);

        string veryLongMessage = "VeryLongMessage_" + new string('A', 3000) + "_EndLongMessage";
        logger.Info(veryLongMessage);
        vm.FlushDrain();

        vm.CopyAll();

        Assert.NotNull(clipboard.LastText);
        Assert.Contains(veryLongMessage, clipboard.LastText);
    }

    // Part D #32: Empty Logs -> Copy Selected/Visible/All are safe no-ops with no exception
    [Fact]
    public void PartD_32_EmptyLogs_SafeNoOps_NoException()
    {
        IAppLogger logger = new InMemoryLogger();
        var clipboard = new FakeClipboardService();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 100, clipboardService: clipboard);

        // When nothing selected
        vm.CopySelected();
        Assert.Equal(0, clipboard.SetTextCallCount);

        // When visible is empty
        vm.CopyVisible();
        Assert.Equal(0, clipboard.SetTextCallCount);

        // When all is empty
        vm.CopyAll();
        Assert.Equal(0, clipboard.SetTextCallCount);
    }

    // Part D #33: Concurrent Logging during Copy maintains stable snapshot and no collection-modified exception
    [Fact]
    public async Task PartD_33_ConcurrentLogging_MaintainsStableSnapshot_NoException()
    {
        IAppLogger logger = new InMemoryLogger(maxEntries: 10000);
        var clipboard = new FakeClipboardService();
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 5000, clipboardService: clipboard);

        for (int i = 0; i < 500; i++)
        {
            logger.Info($"Initial entry {i}");
        }
        vm.FlushDrain();

        var cts = new CancellationTokenSource();
        var backgroundLogTask = Task.Run(() =>
        {
            int count = 0;
            while (!cts.IsCancellationRequested && count < 2000)
            {
                logger.Info($"Concurrent entry {count++}");
                Thread.Sleep(1);
            }
        });

        // Perform multiple copies while logging continues
        for (int i = 0; i < 10; i++)
        {
            vm.CopyVisible();
            vm.CopyAll();
            await Task.Delay(5);
        }

        cts.Cancel();
        await backgroundLogTask;

        Assert.NotNull(clipboard.LastText);
        Assert.True(clipboard.SetTextCallCount >= 20);
    }

    // Section 23: Format specification HH:mm:ss.fff [LEVEL] message
    [Fact]
    public void FormatForClipboard_MatchesFormatSpecification()
    {
        var fixedTime = new DateTime(2026, 10, 1, 10, 44, 35, 333);
        var entry = new LogEntry(fixedTime, LogLevel.Info, "[AutoConfirm] approval prompt detected");
        var item = new LogMessageItem(entry);

        string formatted = item.FormatForClipboard();

        Assert.Equal("10:44:35.333 [INFO] [AutoConfirm] approval prompt detected", formatted);
    }

    // Section 26: Clipboard locked handling
    [Fact]
    public void ClipboardLocked_GracefulFailureNotification_DoesNotThrow()
    {
        IAppLogger logger = new InMemoryLogger();
        var clipboard = new FakeClipboardService { ShouldFail = true };
        var dispatcher = new ImmediateDispatcher();
        using var vm = new DiagnosticsViewModel(logger, dispatcher, maxEntries: 100, clipboardService: clipboard);

        logger.Info("Hello world");
        vm.FlushDrain();

        vm.CopyAll();

        Assert.Equal(1, clipboard.SetTextCallCount);
        Assert.Equal("Failed to copy to clipboard (clipboard locked).", vm.CopyFeedback);
    }
}
