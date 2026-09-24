using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.Core.Macro;

/// <summary>
/// Macro action that performs a discrete background key press (WM_KEYDOWN -> WM_KEYUP)
/// on the target window without moving the mouse cursor or stealing active focus.
/// </summary>
public sealed class PressKeyAction : IMacroAction
{
    public string Name => "PressKey";

    public BackgroundKey Key { get; set; }
    public IntPtr OverrideHwnd { get; set; }

    public string DisplayString => OverrideHwnd != IntPtr.Zero
        ? $"Press Key {Key} on HWND {HwndFormatter.Format(OverrideHwnd)}"
        : $"Press Key {Key}";

    public PressKeyAction(BackgroundKey key, IntPtr overrideHwnd = default)
    {
        Key = key;
        OverrideHwnd = overrideHwnd;
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

        KeyPressResult result = context.Keyboard.PressKey(targetHwnd, Key);

        return Task.FromResult(result switch
        {
            KeyPressResult.Success => MacroActionResult.Success(),
            KeyPressResult.InvalidTarget => MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} became invalid."),
            _ => MacroActionResult.KeyPressFailed($"Keyboard engine returned {result} for HWND {HwndFormatter.Format(targetHwnd)} (Key: {Key}).")
        });
    }
}
