namespace BackgroundAutomator.Core.Macro;

/// <summary>
/// Macro action that pauses execution asynchronously for a specified duration.
/// Does not block threads or busy-spin.
/// </summary>
public sealed class DelayAction : IMacroAction
{
    public string Name => "Delay";

    public int Milliseconds { get; set; }

    public string DisplayString => $"Delay {Milliseconds}ms";

    public DelayAction(int milliseconds)
    {
        Milliseconds = milliseconds;
    }

    public async Task<MacroActionResult> ExecuteAsync(MacroExecutionContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return MacroActionResult.Cancelled();
        }

        if (Milliseconds < 0)
        {
            return MacroActionResult.InvalidConfiguration($"Delay cannot be negative: {Milliseconds}ms");
        }

        if (Milliseconds == 0)
        {
            return MacroActionResult.Success();
        }

        try
        {
            await Task.Delay(Milliseconds, ct);
            return MacroActionResult.Success();
        }
        catch (OperationCanceledException)
        {
            return MacroActionResult.Cancelled();
        }
    }
}
