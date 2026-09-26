using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Security;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.App.ViewModels;

public sealed partial class TargetViewModel : ObservableObject
{
    private readonly WindowTargetService _targetService;
    private readonly ProcessElevationService _elevationService;
    private readonly IAppLogger _logger;
    private readonly Action<WindowTarget?> _onTargetCommitted;
    private readonly DispatcherTimer _validationTimer;

    private WindowTarget? _candidateTarget;
    private bool _isDraggingCrosshair;

    public ObservableCollection<WindowTargetCandidate> AvailableWindows { get; } = new();

    [ObservableProperty]
    private WindowTargetCandidate? _selectedCandidate;

    [ObservableProperty]
    private WindowTarget? _currentTarget;

    // Prominent fields
    [ObservableProperty]
    private string _processName = "-";

    [ObservableProperty]
    private string _windowTitle = "-";

    [ObservableProperty]
    private string _targetStatus = "No target selected";

    [ObservableProperty]
    private string _targetStatusColor = "#666666";

    [ObservableProperty]
    private string _clientCoordsText = "X: - | Y: -";

    // Collapsible Advanced details
    [ObservableProperty]
    private string _pidText = "-";

    [ObservableProperty]
    private string _threadIdText = "-";

    [ObservableProperty]
    private string _rootHwndText = "-";

    [ObservableProperty]
    private string _targetHwndText = "-";

    [ObservableProperty]
    private string _parentHwndText = "-";

    [ObservableProperty]
    private string _windowClassText = "-";

    [ObservableProperty]
    private string _screenCoordsText = "X: - | Y: -";

    // UIPI / Elevation
    [ObservableProperty]
    private bool _hasUipiMismatch;

    [ObservableProperty]
    private string _elevationText = "-";

    [ObservableProperty]
    private string _elevationTextColor = "#333333";

    [ObservableProperty]
    private bool _canRestartAdmin;

    [ObservableProperty]
    private bool _isTargetActive;

    public IntPtr MainWindowHwnd { get; set; } = IntPtr.Zero;

    public TargetViewModel(
        WindowTargetService targetService,
        ProcessElevationService elevationService,
        IAppLogger logger,
        Action<WindowTarget?> onTargetCommitted)
    {
        _targetService = targetService;
        _elevationService = elevationService;
        _logger = logger;
        _onTargetCommitted = onTargetCommitted;

        _validationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _validationTimer.Tick += OnValidationTimerTick;
        _validationTimer.Start();
    }

    public void Initialize()
    {
        RefreshWindows();
    }

    partial void OnSelectedCandidateChanged(WindowTargetCandidate? value)
    {
        if (_isDraggingCrosshair || value == null)
            return;

        if (CurrentTarget != null && CurrentTarget.RootHwnd == value.Hwnd)
            return;

        var target = _targetService.ResolveTargetFromCandidate(value);
        if (target != null)
        {
            CommitTarget(target);
        }
        else
        {
            TargetStatus = "Target unavailable (failed to resolve)";
            TargetStatusColor = "#D83B01";
        }
    }

    [RelayCommand]
    public void RefreshWindows()
    {
        try
        {
            var windows = _targetService.EnumerateTopLevelWindows(ignoreRootHwnd: MainWindowHwnd);
            AvailableWindows.Clear();
            foreach (var win in windows)
            {
                AvailableWindows.Add(win);
            }

            _logger.Info($"Enumerated {windows.Count} usable top-level windows");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to enumerate windows", ex);
        }
    }

    public void StartCrosshairDrag()
    {
        _isDraggingCrosshair = true;
        _candidateTarget = null;
        TargetStatus = "Dragging crosshair... Hover over target window/control";
        TargetStatusColor = "#D83B01";
    }

    public void UpdateCrosshairHover(POINT screenPt)
    {
        if (!_isDraggingCrosshair)
            return;

        Point pt = new(screenPt.X, screenPt.Y);
        var candidate = _targetService.ResolveTargetFromScreenPoint(pt, ignoreProcessRootHwnd: MainWindowHwnd);

        if (candidate != null)
        {
            _candidateTarget = candidate;
            DisplayTargetInfo(candidate, "Inspecting candidate...", "#0078D4");
        }
        else
        {
            ScreenCoordsText = $"X: {pt.X} | Y: {pt.Y}";
            TargetStatus = "Hovering over BackgroundAutomator (ignored)";
            TargetStatusColor = "#888888";
        }
    }

    public void FinishCrosshairDrag()
    {
        if (!_isDraggingCrosshair)
            return;

        _isDraggingCrosshair = false;
        if (_candidateTarget != null)
        {
            CommitTarget(_candidateTarget);
            _logger.Info($"Committed target: HWND {_candidateTarget.TargetHwnd} ({_candidateTarget.ProcessName})");
        }
        else if (CurrentTarget != null)
        {
            DisplayTargetInfo(CurrentTarget, "Target valid", "#107C10");
        }
        else
        {
            TargetStatus = "Crosshair released: No external target selected";
            TargetStatusColor = "#666666";
        }
    }

    public void CancelCrosshairDrag()
    {
        _isDraggingCrosshair = false;
        if (CurrentTarget != null)
        {
            DisplayTargetInfo(CurrentTarget, "Target valid", "#107C10");
        }
        else
        {
            TargetStatus = "Crosshair released: No target selected";
            TargetStatusColor = "#666666";
        }
    }

    public void CommitTarget(WindowTarget? target)
    {
        if (target == null)
        {
            ClearTarget();
            return;
        }

        if (CurrentTarget == target)
            return;

        SetCurrentTarget(target);
        _onTargetCommitted(target);
    }

    public void SetCurrentTarget(WindowTarget? target)
    {
        if (target != null)
        {
            if (CurrentTarget == target)
                return;

            CurrentTarget = target;
            DisplayTargetInfo(target, "Target valid", "#107C10");

            // Sync dropdown selection if present
            for (int i = 0; i < AvailableWindows.Count; i++)
            {
                if (AvailableWindows[i].Hwnd == target.RootHwnd)
                {
                    if (SelectedCandidate != AvailableWindows[i])
                    {
                        SelectedCandidate = AvailableWindows[i];
                    }
                    break;
                }
            }
        }
        else
        {
            if (CurrentTarget == null && !IsTargetActive)
                return;

            ClearTargetDisplay("No target selected", "#666666");
        }
    }

    public void ClearTargetDisplay(string status = "No target selected", string color = "#666666")
    {
        CurrentTarget = null;
        _candidateTarget = null;
        IsTargetActive = false;
        SelectedCandidate = null;
        ProcessName = "-";
        WindowTitle = "-";
        PidText = "-";
        ThreadIdText = "-";
        RootHwndText = "-";
        TargetHwndText = "-";
        ParentHwndText = "-";
        WindowClassText = "-";
        ScreenCoordsText = "X: - | Y: -";
        ClientCoordsText = "X: - | Y: -";
        TargetStatus = status;
        TargetStatusColor = color;
        ElevationText = "-";
        ElevationTextColor = "#333333";
        HasUipiMismatch = false;
        CanRestartAdmin = false;
    }

    private void DisplayTargetInfo(WindowTarget target, string statusText, string statusColor)
    {
        IsTargetActive = true;
        ProcessName = string.IsNullOrWhiteSpace(target.ProcessName) ? "[Unknown]" : target.ProcessName;
        WindowTitle = string.IsNullOrWhiteSpace(target.WindowTitle) ? "[No Title]" : target.WindowTitle;
        PidText = target.ProcessId.ToString();
        ThreadIdText = target.ThreadId.ToString();

        RootHwndText = HwndFormatter.Format(target.RootHwnd);
        TargetHwndText = HwndFormatter.Format(target.TargetHwnd);
        ParentHwndText = target.ParentHwnd == IntPtr.Zero ? "0x0000000000000000" : HwndFormatter.Format(target.ParentHwnd);
        WindowClassText = string.IsNullOrWhiteSpace(target.WindowClass) ? "[Unknown]" : target.WindowClass;

        ScreenCoordsText = $"X: {target.ScreenPoint.X} | Y: {target.ScreenPoint.Y}";
        ClientCoordsText = $"X: {target.ClientPoint.ClientX} | Y: {target.ClientPoint.ClientY}";

        TargetStatus = statusText;
        TargetStatusColor = statusColor;

        // UIPI / Elevation diagnostic check
        var elevationResult = _elevationService.CheckCompatibility(target.ProcessId);
        if (elevationResult.Compatibility == ElevationCompatibility.UipiMismatch)
        {
            ElevationText = "UIPI Mismatch: Target application is running as Administrator! Clicks will be blocked.";
            ElevationTextColor = "#E81123";
            HasUipiMismatch = true;
            CanRestartAdmin = true;
        }
        else if (elevationResult.Compatibility == ElevationCompatibility.Compatible)
        {
            ElevationText = elevationResult.CurrentProcessElevated ? "Compatible (Running as Admin)" : "Compatible (Standard User)";
            ElevationTextColor = "#107C10";
            HasUipiMismatch = false;
            CanRestartAdmin = false;
        }
        else
        {
            ElevationText = "Elevation Status Unknown (Access Denied)";
            ElevationTextColor = "#D83B01";
            HasUipiMismatch = true;
            CanRestartAdmin = true;
        }
    }

    [RelayCommand]
    public void ClearTarget()
    {
        if (CurrentTarget == null && !IsTargetActive)
            return;

        ClearTargetDisplay("Target cleared", "#666666");
        _onTargetCommitted(null);
        _logger.Info("Target cleared by user");
    }

    [RelayCommand]
    public void RefreshCoordinates()
    {
        if (CurrentTarget == null)
            return;

        if (!CurrentTarget.IsWindowValid())
        {
            TargetStatus = "Target unavailable (window closed)";
            TargetStatusColor = "#E81123";
            _logger.Warning($"Target window {HwndFormatter.Format(CurrentTarget.TargetHwnd)} is closed");
            return;
        }

        var refreshed = _targetService.RefreshTarget(CurrentTarget);
        if (refreshed != null)
        {
            CurrentTarget = refreshed;
            DisplayTargetInfo(refreshed, "Coordinates refreshed (Target valid)", "#107C10");
        }
    }

    [RelayCommand]
    public void RestartAsAdministrator()
    {
        if (ProcessElevationService.RestartAsAdministrator())
        {
            System.Windows.Application.Current.Shutdown();
        }
    }

    private void OnValidationTimerTick(object? sender, EventArgs e)
    {
        if (_isDraggingCrosshair || CurrentTarget == null)
            return;

        if (!CurrentTarget.IsWindowValid())
        {
            TargetStatus = "Target unavailable (window closed)";
            TargetStatusColor = "#E81123";
        }
        else
        {
            var refreshed = _targetService.RefreshTarget(CurrentTarget);
            if (refreshed != null)
            {
                CurrentTarget = refreshed;
                ScreenCoordsText = $"X: {refreshed.ScreenPoint.X} | Y: {refreshed.ScreenPoint.Y}";
                ClientCoordsText = $"X: {refreshed.ClientPoint.ClientX} | Y: {refreshed.ClientPoint.ClientY}";
            }
        }
    }

    public void Cleanup()
    {
        _validationTimer.Stop();
    }
}
