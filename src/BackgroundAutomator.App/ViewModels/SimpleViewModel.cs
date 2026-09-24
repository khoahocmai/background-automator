using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BackgroundAutomator.App.Models;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Profiles;
using BackgroundAutomator.Core.Runner;
using BackgroundAutomator.Core.Targeting;

namespace BackgroundAutomator.App.ViewModels;

public sealed partial class SimpleViewModel : ObservableObject
{
    private readonly ClickRunner _runner;
    private readonly IAppLogger _logger;
    private readonly Action<RunnerState, int, int> _onRunnerStateChanged;

    private WindowTarget? _currentTarget;
    private int _cyclesCompleted;
    private int _totalClicksExecuted;

    public ObservableCollection<SimpleClickPointItem> Points { get; } = new();

    [ObservableProperty]
    private SimpleClickPointItem? _selectedPoint;

    [ObservableProperty]
    private string _targetSummaryText = "Target: [No target selected in Target Inspector]";

    [ObservableProperty]
    private int _customX = 0;

    [ObservableProperty]
    private int _customY = 0;

    [ObservableProperty]
    private bool _isSingleClick = true;

    [ObservableProperty]
    private bool _isDoubleClick;

    [ObservableProperty]
    private int _intervalMilliseconds = 500;

    [ObservableProperty]
    private bool _isUntilStopped = true;

    [ObservableProperty]
    private bool _isCount;

    [ObservableProperty]
    private int _repeatCount = 10;

    [ObservableProperty]
    private string _runnerStatusText = "State: IDLE | Cycles: 0 | Clicks: 0";

    [ObservableProperty]
    private string _runnerStatusColor = "#666666";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _canStart = true;

    [ObservableProperty]
    private bool _canStop = false;

    public SimpleViewModel(
        ClickRunner runner,
        IAppLogger logger,
        Action<RunnerState, int, int> onRunnerStateChanged)
    {
        _runner = runner;
        _logger = logger;
        _onRunnerStateChanged = onRunnerStateChanged;

        WireEvents();
    }

    public void SetCurrentTarget(WindowTarget? target)
    {
        _currentTarget = target;
        if (target != null)
        {
            TargetSummaryText = $"Target: {target.ProcessName} | HWND: {HwndFormatter.FormatShort(target.TargetHwnd)} | Local: ({target.ClientPoint.ClientX}, {target.ClientPoint.ClientY})";
            CustomX = Math.Clamp(target.ClientPoint.ClientX, -32768, 32767);
            CustomY = Math.Clamp(target.ClientPoint.ClientY, -32768, 32767);
        }
        else
        {
            TargetSummaryText = "Target: [No target selected in Target Inspector]";
        }
    }

    private void WireEvents()
    {
        _runner.StateChanged += (s, state) =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() => OnStateChanged(state));
        };

        _runner.PointExecuted += (s, point) =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                _totalClicksExecuted++;
                UpdateRunnerStatusText(RunnerState.Running);
                _onRunnerStateChanged(RunnerState.Running, _cyclesCompleted, _totalClicksExecuted);
            });
        };

        _runner.CycleCompleted += (s, cycle) =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                _cyclesCompleted = cycle;
                UpdateRunnerStatusText(RunnerState.Running);
                _onRunnerStateChanged(RunnerState.Running, _cyclesCompleted, _totalClicksExecuted);
            });
        };

        _runner.ErrorOccurred += (s, error) =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                RunnerStatusText = $"State: IDLE (Error: {error})";
                RunnerStatusColor = "#E81123";
                _logger.Error($"Runner error: {error}");
            });
        };
    }

    private void OnStateChanged(RunnerState state)
    {
        switch (state)
        {
            case RunnerState.Running:
                IsRunning = true;
                CanStart = false;
                CanStop = true;
                RunnerStatusColor = "#107C10";
                UpdateRunnerStatusText(state);
                break;
            case RunnerState.Stopping:
                IsRunning = false;
                CanStart = false;
                CanStop = false;
                RunnerStatusText = "State: STOPPING...";
                RunnerStatusColor = "#D83B01";
                break;
            case RunnerState.Idle:
                IsRunning = false;
                CanStart = true;
                CanStop = false;
                RunnerStatusColor = "#666666";
                UpdateRunnerStatusText(state);
                break;
        }

        _onRunnerStateChanged(state, _cyclesCompleted, _totalClicksExecuted);
    }

    private void UpdateRunnerStatusText(RunnerState state)
    {
        string stateStr = state == RunnerState.Running ? "RUNNING" : "IDLE";
        RunnerStatusText = $"State: {stateStr} | Cycles: {_cyclesCompleted} | Clicks: {_totalClicksExecuted}";
    }

    [RelayCommand]
    public void AddTargetPoint()
    {
        if (_currentTarget == null || !_currentTarget.IsWindowValid())
        {
            MessageBox.Show("Please inspect and lock onto a valid target window/control first in Target Inspector.",
                "No Valid Target", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var clickType = IsDoubleClick ? ClickType.Double : ClickType.Single;
        var pt = new ClickPoint(_currentTarget.TargetHwnd, _currentTarget.ClientPoint.ClientX, _currentTarget.ClientPoint.ClientY, clickType);
        Points.Add(new SimpleClickPointItem(Points.Count + 1, pt));
        _logger.Info($"Added point #{Points.Count}: HWND {HwndFormatter.FormatShort(pt.Hwnd)} at ({pt.ClientX}, {pt.ClientY}) [{pt.ClickType}]");
    }

    [RelayCommand]
    public void AddCustomPoint()
    {
        if (_currentTarget == null || !_currentTarget.IsWindowValid())
        {
            MessageBox.Show("Please select a target window/control first so the coordinates attach to a valid HWND.",
                "No Valid Target", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var clickType = IsDoubleClick ? ClickType.Double : ClickType.Single;
        var pt = new ClickPoint(_currentTarget.TargetHwnd, CustomX, CustomY, clickType);
        Points.Add(new SimpleClickPointItem(Points.Count + 1, pt));
        _logger.Info($"Added custom point #{Points.Count}: HWND {HwndFormatter.FormatShort(pt.Hwnd)} at ({pt.ClientX}, {pt.ClientY}) [{pt.ClickType}]");
    }

    [RelayCommand]
    public void RemovePoint(SimpleClickPointItem? item)
    {
        var target = item ?? SelectedPoint;
        if (target != null)
        {
            int index = Points.IndexOf(target);
            Points.Remove(target);
            ReindexPoints();
            if (index < Points.Count)
            {
                SelectedPoint = Points[index];
            }
            else if (Points.Count > 0)
            {
                SelectedPoint = Points[^1];
            }
        }
    }

    [RelayCommand]
    public void MovePointUp(SimpleClickPointItem? item)
    {
        var target = item ?? SelectedPoint;
        if (target == null)
            return;

        int index = Points.IndexOf(target);
        if (index > 0)
        {
            Points.RemoveAt(index);
            Points.Insert(index - 1, target);
            ReindexPoints();
            SelectedPoint = target;
        }
    }

    [RelayCommand]
    public void MovePointDown(SimpleClickPointItem? item)
    {
        var target = item ?? SelectedPoint;
        if (target == null)
            return;

        int index = Points.IndexOf(target);
        if (index >= 0 && index < Points.Count - 1)
        {
            Points.RemoveAt(index);
            Points.Insert(index + 1, target);
            ReindexPoints();
            SelectedPoint = target;
        }
    }

    [RelayCommand]
    public void ClearAllPoints()
    {
        Points.Clear();
    }

    private void ReindexPoints()
    {
        for (int i = 0; i < Points.Count; i++)
        {
            Points[i].Index = i + 1;
        }
    }

    [RelayCommand]
    public void StartRunner()
    {
        if (_runner.State != RunnerState.Idle)
            return;

        if (Points.Count == 0)
        {
            MessageBox.Show("Please add at least one click point before starting.",
                "No Points Configured", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var config = new ClickRunnerConfig
        {
            Points = Points.Select(p => p.ToClickPoint()).ToList(),
            IntervalMilliseconds = IntervalMilliseconds,
            RepeatMode = IsCount ? RepeatMode.Count : RepeatMode.UntilStopped,
            RepeatCount = RepeatCount
        };

        _cyclesCompleted = 0;
        _totalClicksExecuted = 0;

        bool started = _runner.Start(config);
        if (!started)
        {
            _logger.Warning("Runner failed to start (already active).");
        }
    }

    [RelayCommand]
    public void StopRunner()
    {
        if (_runner.State == RunnerState.Running)
        {
            _runner.Stop();
        }
    }

    public void ToggleRunner()
    {
        if (_runner.State == RunnerState.Running)
        {
            StopRunner();
        }
        else if (_runner.State == RunnerState.Idle)
        {
            StartRunner();
        }
    }

    public void LoadFromProfile(List<ClickPoint> points, ClickRunnerSettingsConfig? settings)
    {
        Points.Clear();
        for (int i = 0; i < points.Count; i++)
        {
            Points.Add(new SimpleClickPointItem(i + 1, points[i]));
        }

        if (settings != null)
        {
            IntervalMilliseconds = Math.Clamp(settings.IntervalMs, 10, 60000);
            if (settings.RepeatMode == RepeatMode.Count)
            {
                IsCount = true;
                IsUntilStopped = false;
                RepeatCount = Math.Clamp(settings.RepeatCount, 1, 1000000);
            }
            else
            {
                IsUntilStopped = true;
                IsCount = false;
            }
        }
    }

    public List<ClickPoint> GetPoints() => Points.Select(p => p.ToClickPoint()).ToList();
}
