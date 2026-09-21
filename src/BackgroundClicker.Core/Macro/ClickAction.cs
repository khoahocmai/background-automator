using BackgroundClicker.Core.Clicking;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;

namespace BackgroundClicker.Core.Macro;

/// <summary>
/// Macro action that performs a single background click at a client coordinate.
/// </summary>
public sealed class ClickAction : IMacroAction
{
    public string Name => "Click";

    public int ClientX { get; set; }
    public int ClientY { get; set; }
    public IntPtr OverrideHwnd { get; set; }

    public string DisplayString => OverrideHwnd != IntPtr.Zero
        ? $"Click at ({ClientX}, {ClientY}) on HWND {HwndFormatter.Format(OverrideHwnd)}"
        : $"Click at ({ClientX}, {ClientY})";

    public ClickAction(int clientX, int clientY, IntPtr overrideHwnd = default)
    {
        ClientX = clientX;
        ClientY = clientY;
        OverrideHwnd = overrideHwnd;
    }

    public ClickAction(TargetPoint targetPoint)
    {
        ClientX = targetPoint.ClientX;
        ClientY = targetPoint.ClientY;
        OverrideHwnd = targetPoint.Hwnd;
    }

    public Task<MacroActionResult> ExecuteAsync(MacroExecutionContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromResult(MacroActionResult.Cancelled());
        }

        IntPtr targetHwnd = OverrideHwnd != IntPtr.Zero ? OverrideHwnd : context.TargetHwnd;
        if (targetHwnd == IntPtr.Zero || !User32.IsWindow(targetHwnd))
        {
            return Task.FromResult(MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} is invalid or closed."));
        }

        var targetPoint = new TargetPoint(targetHwnd, ClientX, ClientY);
        ClickResult result = context.Clicker.Click(targetPoint);

        return Task.FromResult(result switch
        {
            ClickResult.Success => MacroActionResult.Success(),
            ClickResult.InvalidTarget => MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} became invalid."),
            _ => MacroActionResult.ClickFailed($"Clicker returned {result} for HWND {HwndFormatter.Format(targetHwnd)}.")
        });
    }
}
