using System.Diagnostics;
using BackgroundAutomator.Core.Logging;

namespace BackgroundAutomator.Core.Macro;

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

    public int CurrentCycle { get; private set; }

    public event Action<MacroRunnerState>? StateChanged;
    public event Action<int, IMacroAction>? ActionStarting;
    public event Action<int, IMacroAction, string>? ActionProgress;
    public event Action<int, IMacroAction, MacroActionResult>? ActionCompleted;
    public event Action<MacroExecutionResult>? ExecutionCompleted;
    public event Action<int>? CycleStarting;
    public event Action<int>? CycleCompleted;

    public MacroRunner(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Starts executing the specified actions sequentially once.
    /// Throws <see cref="InvalidOperationException"/> if already running or stopping.
    /// </summary>
    public Task<MacroExecutionResult> RunAsync(
        IReadOnlyList<IMacroAction> actions,
        MacroExecutionContext context,
        CancellationToken externalToken = default)
        => RunAsync(actions, context, MacroRunnerSettings.Default, externalToken);

    /// <summary>
    /// Starts executing the specified actions sequentially according to the specified repeat settings.
    /// Throws <see cref="InvalidOperationException"/> if already running or stopping.
    /// </summary>
    /// <param name="actions">Ordered sequence of macro actions to run.</param>
    /// <param name="context">Macro execution context providing clicker, capture, and target HWND.</param>
    /// <param name="settings">Execution repeat and pacing settings.</param>
    /// <param name="externalToken">Optional external cancellation token.</param>
    /// <returns>A task representing the macro execution, returning a <see cref="MacroExecutionResult"/>.</returns>
    public async Task<MacroExecutionResult> RunAsync(
        IReadOnlyList<IMacroAction> actions,
        MacroExecutionContext context,
        MacroRunnerSettings settings,
        CancellationToken externalToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(context);
        settings ??= MacroRunnerSettings.Default;

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
            CurrentCycle = 0;
        }

        // Notify state changed outside lock
        StateChanged?.Invoke(MacroRunnerState.Running);
        _logger?.Info($"MacroRunner started with {actions.Count} action(s). RepeatMode={settings.RepeatMode}, RepeatCount={settings.RepeatCount}, CycleDelay={settings.CycleDelayMilliseconds}ms.");

        var sw = Stopwatch.StartNew();
        int totalCompletedActions = 0;
        int completedCycles = 0;
        MacroActionResult lastResult = MacroActionResult.Success();

        try
        {
            if (actions.Count == 0)
            {
                return new MacroExecutionResult(
                    FinalStatus: MacroActionStatus.Success,
                    CompletedActionsCount: 0,
                    TotalActionsCount: 0,
                    ElapsedTime: sw.Elapsed,
                    Message: "No actions configured.",
                    CompletedCyclesCount: 0);
            }

            int cycle = 0;
            while (!token.IsCancellationRequested)
            {
                if (settings.RepeatMode == MacroRepeatMode.Once && cycle >= 1)
                {
                    break;
                }
                if (settings.RepeatMode == MacroRepeatMode.Count && cycle >= settings.RepeatCount)
                {
                    break;
                }

                cycle++;
                CurrentCycle = cycle;
                CycleStarting?.Invoke(cycle);
                _logger?.Debug($"MacroRunner starting cycle {cycle}.");

                bool cycleFailed = false;

                for (int i = 0; i < actions.Count; i++)
                {
                    if (token.IsCancellationRequested)
                    {
                        lastResult = MacroActionResult.Cancelled();
                        cycleFailed = true;
                        break;
                    }

                    IMacroAction currentAction = actions[i];
                    ActionStarting?.Invoke(i, currentAction);
                    _logger?.Debug($"MacroRunner [Cycle {cycle}] executing action [{i + 1}/{actions.Count}]: {currentAction.DisplayString}");

                    int actionIndex = i;
                    context.ProgressCallback = progress => ActionProgress?.Invoke(actionIndex, currentAction, progress);

                    try
                    {
                        lastResult = await currentAction.ExecuteAsync(context, token).ConfigureAwait(false);
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
                    finally
                    {
                        context.ProgressCallback = null;
                    }

                    ActionCompleted?.Invoke(i, currentAction, lastResult);

                    if (!lastResult.IsSuccess)
                    {
                        _logger?.Warning($"MacroRunner stopped at cycle {cycle}, action [{i + 1}/{actions.Count}] with status {lastResult.Status}: {lastResult.Message}");
                        cycleFailed = true;
                        break;
                    }

                    totalCompletedActions++;
                }

                if (cycleFailed)
                {
                    break;
                }

                completedCycles++;
                CycleCompleted?.Invoke(cycle);

                bool hasMoreCycles = settings.RepeatMode switch
                {
                    MacroRepeatMode.Once => false,
                    MacroRepeatMode.Count => cycle < settings.RepeatCount,
                    MacroRepeatMode.UntilStopped => true,
                    _ => false
                };

                if (hasMoreCycles && !token.IsCancellationRequested)
                {
                    if (settings.CycleDelayMilliseconds > 0)
                    {
                        try
                        {
                            await Task.Delay(settings.CycleDelayMilliseconds, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            lastResult = MacroActionResult.Cancelled();
                            break;
                        }
                    }
                }
            }

            if (token.IsCancellationRequested && lastResult.IsSuccess)
            {
                lastResult = MacroActionResult.Cancelled();
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
                CurrentCycle = 0;
            }

            StateChanged?.Invoke(MacroRunnerState.Idle);
        }

        int targetTotalActions = settings.RepeatMode switch
        {
            MacroRepeatMode.Count => actions.Count * settings.RepeatCount,
            MacroRepeatMode.Once => actions.Count,
            _ => actions.Count
        };

        var executionResult = new MacroExecutionResult(
            FinalStatus: lastResult.Status,
            CompletedActionsCount: totalCompletedActions,
            TotalActionsCount: targetTotalActions,
            ElapsedTime: sw.Elapsed,
            Message: lastResult.Message,
            BlockerReason: lastResult.BlockerReason,
            CompletedCyclesCount: completedCycles);

        _logger?.Info($"MacroRunner finished: Status={executionResult.FinalStatus}, Cycles={completedCycles}, CompletedActions={totalCompletedActions}/{targetTotalActions}, Time={sw.ElapsedMilliseconds}ms");
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
