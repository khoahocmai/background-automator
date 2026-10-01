using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BackgroundAutomator.App.Models;
using BackgroundAutomator.App.Services;
using BackgroundAutomator.Core.Logging;

namespace BackgroundAutomator.App.ViewModels;

public sealed partial class DiagnosticsViewModel : ObservableObject, IDisposable
{
    public const int DefaultMaxEntries = 1000;
    public const int BatchSize = 100;

    private readonly IAppLogger _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IClipboardService _clipboardService;
    private readonly ConcurrentQueue<LogMessageItem> _pendingQueue = new();
    private int _drainScheduled;
    private int _maxEntries;

    public int MaxEntries
    {
        get => _maxEntries;
        set
        {
            if (value > 0)
                _maxEntries = value;
        }
    }

    public ObservableCollection<LogMessageItem> AllEntries { get; } = new();
    public ObservableCollection<LogMessageItem> FilteredEntries { get; } = new();

    [ObservableProperty]
    private string _selectedFilter = "All";

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private string? _copyFeedback;

    public IReadOnlyList<string> FilterOptions { get; } = new[] { "All", "Info", "Warning", "Error", "Debug" };

    /// <summary>
    /// Delegate to retrieve currently selected log items from UI list box.
    /// </summary>
    public Func<IEnumerable<LogMessageItem>>? SelectedItemsProvider { get; set; }

    /// <summary>
    /// Event raised when SelectAll command is executed from menu or shortcut.
    /// </summary>
    public event Action? SelectAllRequested;

    /// <summary>
    /// Event raised when new visible log entries are added and auto-scroll is enabled,
    /// or when auto-scroll / filter is changed with existing visible items.
    /// </summary>
    public event Action<LogMessageItem>? ScrollRequested;

    public int PendingQueueCount => _pendingQueue.Count;
    public IClipboardService ClipboardService => _clipboardService;

    public DiagnosticsViewModel(
        IAppLogger logger,
        IUiDispatcher? uiDispatcher = null,
        int maxEntries = DefaultMaxEntries,
        IClipboardService? clipboardService = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxEntries = maxEntries > 0 ? maxEntries : DefaultMaxEntries;
        _uiDispatcher = uiDispatcher ?? new WpfUiDispatcher();
        _clipboardService = clipboardService ?? new WpfClipboardService();
        _logger.MessageLogged += OnMessageLogged;
    }

    partial void OnSelectedFilterChanged(string value)
    {
        _uiDispatcher.InvokeAsync(ApplyFilter);
    }

    partial void OnAutoScrollChanged(bool value)
    {
        if (value)
        {
            _uiDispatcher.InvokeAsync(() =>
            {
                if (AutoScroll && FilteredEntries.Count > 0)
                {
                    ScrollRequested?.Invoke(FilteredEntries[^1]);
                }
            });
        }
    }

    private void OnMessageLogged(LogEntry entry)
    {
        var item = new LogMessageItem(entry);
        _pendingQueue.Enqueue(item);
        ScheduleDrain();
    }

    private void ScheduleDrain()
    {
        if (Interlocked.CompareExchange(ref _drainScheduled, 1, 0) == 0)
        {
            _uiDispatcher.InvokeAsync(DrainPendingLogs);
        }
    }

    private void DrainPendingLogs()
    {
        LogMessageItem? lastAddedVisibleItem = null;

        try
        {
            int processedCount = 0;
            while (processedCount < BatchSize && _pendingQueue.TryDequeue(out var item))
            {
                processedCount++;
                AllEntries.Add(item);

                if (MatchesFilter(item))
                {
                    FilteredEntries.Add(item);
                    lastAddedVisibleItem = item;
                }

                while (AllEntries.Count > _maxEntries)
                {
                    AllEntries.RemoveAt(0);
                }

                while (FilteredEntries.Count > _maxEntries)
                {
                    FilteredEntries.RemoveAt(0);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _drainScheduled, 0);
        }

        if (lastAddedVisibleItem != null && AutoScroll)
        {
            ScrollRequested?.Invoke(lastAddedVisibleItem);
        }

        if (!_pendingQueue.IsEmpty)
        {
            ScheduleDrain();
        }
    }

    private bool MatchesFilter(LogMessageItem item)
    {
        if (SelectedFilter == "All")
            return true;

        return SelectedFilter switch
        {
            "Info" => item.Level == LogLevel.Info,
            "Warning" => item.Level == LogLevel.Warning,
            "Error" => item.Level == LogLevel.Error,
            "Debug" => item.Level == LogLevel.Debug,
            _ => true
        };
    }

    private void ApplyFilter()
    {
        FilteredEntries.Clear();
        foreach (var item in AllEntries)
        {
            if (MatchesFilter(item))
            {
                FilteredEntries.Add(item);
            }
        }

        if (AutoScroll && FilteredEntries.Count > 0)
        {
            ScrollRequested?.Invoke(FilteredEntries[^1]);
        }
    }

    [RelayCommand]
    public void CopySelected()
    {
        _uiDispatcher.InvokeAsync(() =>
        {
            var items = SelectedItemsProvider?.Invoke();
            if (items != null)
            {
                CopySelectedInternal(items);
            }
        });
    }

    public void CopySelected(IEnumerable<LogMessageItem> selectedItems)
    {
        CopySelectedInternal(selectedItems);
    }

    private void CopySelectedInternal(IEnumerable<LogMessageItem> items)
    {
        var list = items?.ToList() ?? new List<LogMessageItem>();
        if (list.Count == 0)
        {
            return;
        }

        var ordered = list.OrderBy(x => x.Timestamp).ToList();
        string formatted = string.Join(Environment.NewLine, ordered.Select(x => x.FormatForClipboard()));

        bool ok = _clipboardService.SetText(formatted);
        if (ok)
        {
            CopyFeedback = $"Copied {ordered.Count} selected log {(ordered.Count == 1 ? "line" : "lines")}.";
        }
        else
        {
            CopyFeedback = "Failed to copy to clipboard (clipboard locked).";
        }
    }

    [RelayCommand]
    public void CopyVisible()
    {
        _uiDispatcher.InvokeAsync(() =>
        {
            var list = FilteredEntries.ToList();
            if (list.Count == 0)
            {
                return;
            }

            var ordered = list.OrderBy(x => x.Timestamp).ToList();
            string formatted = string.Join(Environment.NewLine, ordered.Select(x => x.FormatForClipboard()));

            bool ok = _clipboardService.SetText(formatted);
            if (ok)
            {
                CopyFeedback = $"Copied {ordered.Count} visible log {(ordered.Count == 1 ? "line" : "lines")}.";
            }
            else
            {
                CopyFeedback = "Failed to copy to clipboard (clipboard locked).";
            }
        });
    }

    [RelayCommand]
    public void CopyAll()
    {
        _uiDispatcher.InvokeAsync(() =>
        {
            var list = AllEntries.ToList();
            if (list.Count == 0)
            {
                return;
            }

            var ordered = list.OrderBy(x => x.Timestamp).ToList();
            string formatted = string.Join(Environment.NewLine, ordered.Select(x => x.FormatForClipboard()));

            bool ok = _clipboardService.SetText(formatted);
            if (ok)
            {
                CopyFeedback = $"Copied all {ordered.Count} retained log {(ordered.Count == 1 ? "line" : "lines")}.";
            }
            else
            {
                CopyFeedback = "Failed to copy to clipboard (clipboard locked).";
            }
        });
    }

    [RelayCommand]
    public void SelectAll()
    {
        SelectAllRequested?.Invoke();
    }

    [RelayCommand]
    public void ClearLog()
    {
        _uiDispatcher.InvokeAsync(() =>
        {
            while (_pendingQueue.TryDequeue(out _)) { }

            _logger.Clear();
            AllEntries.Clear();
            FilteredEntries.Clear();
        });
    }

    /// <summary>
    /// Explicitly flushes/drains all currently pending items (useful for tests and synchronization).
    /// </summary>
    public void FlushDrain()
    {
        DrainPendingLogs();
    }

    public void Dispose()
    {
        _logger.MessageLogged -= OnMessageLogged;
    }
}
