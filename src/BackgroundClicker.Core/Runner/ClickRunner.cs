using System.Diagnostics;
using BackgroundClicker.Core.Clicking;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Targeting;

namespace BackgroundClicker.Core.Runner;

/// <summary>
/// Asynchronous click runner that executes ordered click points against target HWNDs.
/// Operates on background tasks with responsive CancellationToken-based cancellation.
/// </summary>
public sealed class ClickRunner : IDisposable
{
    private readonly IBackgroundClicker _clicker;
    private readonly IAppLogger? _logger;
    private readonly object _stateLock = new();

    private CancellationTokenSource? _cts;
    private Task? _runningTask;

    /// <summary>
    /// Current execution lifecycle state.
    /// </summary>
    public RunnerState State { get; private set; } = RunnerState.Idle;

    /// <summary>
    /// Fired when the runner state transitions between Idle, Running, and Stopping.
    /// </summary>
    public event EventHandler<RunnerState>? StateChanged;

    /// <summary>
    /// Fired each time a single click point is dispatched to a target.
    /// </summary>
    public event EventHandler<ClickPoint>? PointExecuted;

    /// <summary>
    /// Fired when an entire sequence of configured click points finishes a full iteration.
    /// Passes the 1-based cycle index.
    /// </summary>
    public event EventHandler<int>? CycleCompleted;

    /// <summary>
    /// Fired when execution is interrupted by an error (such as target window being closed).
    /// </summary>
    public event EventHandler<string>? ErrorOccurred;

    public ClickRunner(IBackgroundClicker clicker, IAppLogger? logger = null)
    {
        _clicker = clicker ?? throw new ArgumentNullException(nameof(clicker));
        _logger = logger;
    }

    /// <summary>
    /// Starts execution of the specified click points.
    /// Returns true if execution started, or false if already running or config is invalid.
    /// </summary>
    public bool Start(ClickRunnerConfig config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));

        config.Validate();

        lock (_stateLock)
        {
            if (State != RunnerState.Idle)
            {
                _logger?.Warning("Runner is already active. Duplicate start request ignored.");
                return false;
            }

            State = RunnerState.Running;
            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;

            _logger?.Info($"ClickRunner started: {config.Points.Count} points, {config.IntervalMilliseconds}ms interval, Mode: {config.RepeatMode}");
            _runningTask = Task.Run(async () => await RunLoopAsync(config, token));
        }

        OnStateChanged(RunnerState.Running);
        return true;
    }

    /// <summary>
    /// Requests cancellation of the running loop promptly without blocking.
    /// </summary>
    public void Stop()
    {
        lock (_stateLock)
        {
            if (State != RunnerState.Running)
            {
                return;
            }

            State = RunnerState.Stopping;
            _logger?.Info("ClickRunner stop requested.");
            _cts?.Cancel();
        }

        OnStateChanged(RunnerState.Stopping);
    }

    /// <summary>
    /// Requests cancellation and asynchronously awaits clean loop shutdown.
    /// </summary>
    public async Task StopAsync()
    {
        Task? taskToWait = null;

        lock (_stateLock)
        {
            if (State == RunnerState.Running)
            {
                State = RunnerState.Stopping;
                _logger?.Info("ClickRunner stop requested.");
                _cts?.Cancel();
                taskToWait = _runningTask;
            }
            else if (State == RunnerState.Stopping)
            {
                taskToWait = _runningTask;
            }
        }

        if (State == RunnerState.Stopping)
        {
            OnStateChanged(RunnerState.Stopping);
        }

        if (taskToWait != null)
        {
            try
            {
                await taskToWait;
            }
            catch (OperationCanceledException)
            {
                // Expected on clean cancellation
            }
            catch (Exception ex)
            {
                _logger?.Error("Unexpected error awaiting runner task shutdown", ex);
            }
        }
    }

    private async Task RunLoopAsync(ClickRunnerConfig config, CancellationToken token)
    {
        int cycle = 0;
        bool targetUnavailable = false;

        try
        {
            while (!token.IsCancellationRequested)
            {
                // Execute ordered points
                for (int i = 0; i < config.Points.Count; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    ClickPoint point = config.Points[i];

                    // Dispatch click
                    ClickResult result = point.ClickType == ClickType.Double
                        ? _clicker.DoubleClick(point.TargetPoint)
                        : _clicker.Click(point.TargetPoint);

                    if (result == ClickResult.InvalidTarget)
                    {
                        targetUnavailable = true;
                        string msg = $"Target unavailable: HWND {HwndFormatter.Format(point.Hwnd)} is closed or invalid";
                        _logger?.Warning(msg);
                        OnErrorOccurred(msg);
                        break;
                    }

                    if (result == ClickResult.PostFailed)
                    {
                        _logger?.Warning($"PostMessage failed for HWND {HwndFormatter.Format(point.Hwnd)}");
                    }

                    OnPointExecuted(point);

                    // Delay before next point or next cycle
                    try
                    {
                        await Task.Delay(config.IntervalMilliseconds, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                if (token.IsCancellationRequested || targetUnavailable)
                {
                    break;
                }

                cycle++;
                OnCycleCompleted(cycle);

                if (config.RepeatMode == RepeatMode.Count && cycle >= config.RepeatCount)
                {
                    _logger?.Info($"ClickRunner completed requested count of {config.RepeatCount} cycles.");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger?.Info("ClickRunner loop cancelled.");
        }
        catch (Exception ex)
        {
            _logger?.Error("Unexpected error in click runner loop", ex);
            OnErrorOccurred($"Unexpected error: {ex.Message}");
        }
        finally
        {
            lock (_stateLock)
            {
                State = RunnerState.Idle;
                _cts?.Dispose();
                _cts = null;
                _runningTask = null;
            }

            _logger?.Info("ClickRunner transitioned to Idle.");
            OnStateChanged(RunnerState.Idle);
        }
    }

    private void OnStateChanged(RunnerState state) => StateChanged?.Invoke(this, state);
    private void OnPointExecuted(ClickPoint point) => PointExecuted?.Invoke(this, point);
    private void OnCycleCompleted(int cycle) => CycleCompleted?.Invoke(this, cycle);
    private void OnErrorOccurred(string error) => ErrorOccurred?.Invoke(this, error);

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (State != RunnerState.Idle)
            {
                _cts?.Cancel();
            }
            _cts?.Dispose();
            _cts = null;
        }
    }
}
