namespace BackgroundClicker.Core.Macro;

/// <summary>
/// Represents an executable step within a macro sequence.
/// </summary>
public interface IMacroAction
{
    /// <summary>
    /// Type or short name of the action (e.g. "Click", "Delay", "WaitColor").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable summary of the action and its configured parameters.
    /// </summary>
    string DisplayString { get; }

    /// <summary>
    /// Executes the action asynchronously using the provided context and cancellation token.
    /// </summary>
    /// <param name="context">Macro runtime execution context.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result of executing this action.</returns>
    Task<MacroActionResult> ExecuteAsync(MacroExecutionContext context, CancellationToken ct);
}
