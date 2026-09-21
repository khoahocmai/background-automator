using System.Diagnostics;
using BackgroundClicker.Core.Logging;

namespace BackgroundClicker.Core.Macro;

/// <summary>
/// Orchestrates the sequential execution of a list of <see cref="IMacroAction"/>s.
/// Guarantees thread-safe lifecycle transitions (Idle -> Running -> Stopping -> Idle),
/// prevents duplicate starts, supports cancellation, and notifies observers via events.
/// </summary>
public sealed class MacroRunner : IDisposable
{
    private readonly object _syncLock = new();
    private readonly IAppLogger? _logger;
    private CancellationTokenSource? _cts;
    private MacroRunnerState _state = MacroRunnerState.Idle;

    public MacroRunnerState State
    {
        get
        {
            lock (_syncLock)
            {
                return _state;
            }
        }
        private set
        {
            lock (_syncLock)
            {
                _state = value;
            }
            StateChanged?.Invoke(value);
        }
    }

    public event Action<MacroRunnerState>? StateChanged;
    public event Action<int, IMacroAction>? ActionStarting;
    public event Action<int, IMacroAction, MacroActionResult>? ActionCompleted;
    public event Action<MacroExecutionResult>? ExecutionCompleted;

    public MacroRunner(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Starts executing the specified actions sequentially.
    /// Throws <see cref="InvalidOperationException"/> if already running or stopping.
    /// </summary>
    /// <param name="actions">Ordered sequence of macro actions to run.</param>
    /// <param name="context">Macro execution context providing clicker, capture, and target HWND.</param>
    /// <param name="externalToken">Optional external cancellation token.</param>
    /// <returns>A task representing the macro execution, returning a <see cref="MacroExecutionResult"/>.</returns>
    public async Task<MacroExecutionResult> RunAsync(
        IReadOnlyList<IMacroAction> actions,
        MacroExecutionContext context,
        CancellationToken externalToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(context);

        CancellationToken token;

        lock (_syncLock)
        {
            if (_state != MacroRunnerState.Idle)
            {
                throw new InvalidOperationException($"Cannot start MacroRunner while state is {_state}.");
            }

            _cts = externalToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(externalToken)
                : new CancellationTokenSource();

            token = _cts.Token;
            _state = MacroRunnerState.Running;
        }

        // Notify state changed outside lock
        StateChanged?.Invoke(MacroRunnerState.Running);
        _logger?.Info($"MacroRunner started with {actions.Count} action(s).");

        var sw = Stopwatch.StartNew();
        int completedCount = 0;
        MacroActionResult lastResult = MacroActionResult.Success();

        try
        {
            for (int i = 0; i < actions.Count; i++)
            {
                if (token.IsCancellationRequested)
                {
                    lastResult = MacroActionResult.Cancelled();
                    break;
                }

                IMacroAction currentAction = actions[i];
                ActionStarting?.Invoke(i, currentAction);
                _logger?.Debug($"MacroRunner executing action [{i + 1}/{actions.Count}]: {currentAction.DisplayString}");

                try
                {
                    lastResult = await currentAction.ExecuteAsync(context, token);
                }
                catch (OperationCanceledException)
                {
                    lastResult = MacroActionResult.Cancelled();
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Macro action {currentAction.Name} threw unhandled exception: {ex.Message}");
                    lastResult = MacroActionResult.ClickFailed($"Unhandled exception: {ex.Message}");
                }

                ActionCompleted?.Invoke(i, currentAction, lastResult);

                if (!lastResult.IsSuccess)
                {
                    _logger?.Warning($"MacroRunner stopped at action [{i + 1}/{actions.Count}] with status {lastResult.Status}: {lastResult.Message}");
                    break;
                }

                completedCount++;
            }
        }
        finally
        {
            sw.Stop();

            lock (_syncLock)
            {
                _cts?.Dispose();
                _cts = null;
                _state = MacroRunnerState.Idle;
            }

            StateChanged?.Invoke(MacroRunnerState.Idle);
        }

        var executionResult = new MacroExecutionResult(
            FinalStatus: lastResult.Status,
            CompletedActionsCount: completedCount,
            TotalActionsCount: actions.Count,
            ElapsedTime: sw.Elapsed,
            Message: lastResult.Message);

        _logger?.Info($"MacroRunner finished: Status={executionResult.FinalStatus}, Completed={completedCount}/{actions.Count}, Time={sw.ElapsedMilliseconds}ms");
        ExecutionCompleted?.Invoke(executionResult);

        return executionResult;
    }

    /// <summary>
    /// Signals the runner to cancel the current execution gracefully.
    /// Safe to call from any thread or when already idle.
    /// </summary>
    public void Stop()
    {
        CancellationTokenSource? ctsToCancel = null;

        lock (_syncLock)
        {
            if (_state != MacroRunnerState.Running)
            {
                return;
            }

            _state = MacroRunnerState.Stopping;
            ctsToCancel = _cts;
        }

        StateChanged?.Invoke(MacroRunnerState.Stopping);
        _logger?.Info("MacroRunner stop requested.");

        try
        {
            ctsToCancel?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // CTS already disposed; no-op
        }
    }

    public void Dispose()
    {
        Stop();
        lock (_syncLock)
        {
            _cts?.Dispose();
            _cts = null;
        }
    }
}
