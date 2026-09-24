using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BackgroundAutomator.App.Models;
using BackgroundAutomator.Core.Logging;

namespace BackgroundAutomator.App.ViewModels;

public sealed partial class DiagnosticsViewModel : ObservableObject
{
    private readonly IAppLogger _logger;
    private readonly object _lock = new();

    public ObservableCollection<LogMessageItem> AllEntries { get; } = new();
    public ObservableCollection<LogMessageItem> FilteredEntries { get; } = new();

    [ObservableProperty]
    private string _selectedFilter = "All";

    [ObservableProperty]
    private bool _autoScroll = true;

    public IReadOnlyList<string> FilterOptions { get; } = new[] { "All", "Info", "Warning", "Error", "Debug" };

    public DiagnosticsViewModel(IAppLogger logger)
    {
        _logger = logger;
        _logger.MessageLogged += OnMessageLogged;
    }

    partial void OnSelectedFilterChanged(string value)
    {
        ApplyFilter();
    }

    private void OnMessageLogged(LogEntry entry)
    {
        var item = new LogMessageItem(entry);

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted)
            return;

        dispatcher.InvokeAsync(() =>
        {
            AllEntries.Add(item);
            if (AllEntries.Count > 500)
            {
                AllEntries.RemoveAt(0);
            }

            if (MatchesFilter(item))
            {
                FilteredEntries.Add(item);
                if (FilteredEntries.Count > 500)
                {
                    FilteredEntries.RemoveAt(0);
                }
            }
        });
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
    }

    [RelayCommand]
    private void ClearLog()
    {
        _logger.Clear();
        AllEntries.Clear();
        FilteredEntries.Clear();
    }
}
