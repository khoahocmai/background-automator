using System.Diagnostics;
using System.Drawing;
using BackgroundClicker.Core.Capture;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;

namespace BackgroundClicker.Core.Macro;

/// <summary>
/// Macro action that polls a target window's client area until a specified pixel color
/// is observed at (ClientX, ClientY) within a given tolerance and timeout.
/// Non-blocking, cancellation-aware, and leaves the physical mouse cursor untouched.
/// </summary>
public sealed class WaitColorAction : IMacroAction
{
    public string Name => "WaitColor";

    public int ClientX { get; set; }
    public int ClientY { get; set; }
    public Color TargetColor { get; set; }
    public int Tolerance { get; set; }
    public TimeSpan Timeout { get; set; }
    public TimeSpan PollInterval { get; set; }
    public IntPtr OverrideHwnd { get; set; }

    public string DisplayString =>
        $"WaitColor at ({ClientX}, {ClientY}) for RGB({TargetColor.R},{TargetColor.G},{TargetColor.B}) tol={Tolerance} timeout={Timeout.TotalMilliseconds:F0}ms";

    public WaitColorAction(
        int clientX,
        int clientY,
        Color targetColor,
        int tolerance = 0,
        TimeSpan? timeout = null,
        TimeSpan? pollInterval = null,
        IntPtr overrideHwnd = default)
    {
        ClientX = clientX;
        ClientY = clientY;
        TargetColor = targetColor;
        Tolerance = Math.Max(0, tolerance);
        Timeout = timeout ?? TimeSpan.FromSeconds(5);
        PollInterval = pollInterval ?? TimeSpan.FromMilliseconds(50);
        OverrideHwnd = overrideHwnd;
    }

    public async Task<MacroActionResult> ExecuteAsync(MacroExecutionContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return MacroActionResult.Cancelled();
        }

        if (Timeout < TimeSpan.Zero)
        {
            return MacroActionResult.InvalidConfiguration($"Timeout cannot be negative: {Timeout}");
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            return MacroActionResult.InvalidConfiguration($"PollInterval must be positive: {PollInterval}");
        }

        IntPtr targetHwnd = OverrideHwnd != IntPtr.Zero ? OverrideHwnd : context.TargetHwnd;
        if (targetHwnd == IntPtr.Zero || !User32.IsWindow(targetHwnd))
        {
            return MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} is invalid or closed.");
        }

        var sw = Stopwatch.StartNew();

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                return MacroActionResult.Cancelled();
            }

            if (!User32.IsWindow(targetHwnd))
            {
                return MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} closed during WaitColor.");
            }

            WindowCapture? capture = await context.CaptureService.CaptureClientAreaAsync(targetHwnd, ct).ConfigureAwait(false);
            if (capture == null)
            {
                if (!User32.IsWindow(targetHwnd))
                {
                    return MacroActionResult.TargetUnavailable($"Target HWND {HwndFormatter.Format(targetHwnd)} closed during WaitColor.");
                }

                return MacroActionResult.CaptureFailed($"Capture failed for HWND {HwndFormatter.Format(targetHwnd)} during WaitColor.");
            }

            using (capture)
            {
                if (ClientX < 0 || ClientX >= capture.Width || ClientY < 0 || ClientY >= capture.Height)
                {
                    return MacroActionResult.InvalidConfiguration(
                        $"Client coordinate ({ClientX}, {ClientY}) is outside capture bounds ({capture.Width}x{capture.Height}) for HWND {HwndFormatter.Format(targetHwnd)}.");
                }

                if (capture.MatchesColor(ClientX, ClientY, TargetColor, Tolerance))
                {
                    return MacroActionResult.Success(
                        $"Matched color RGB({TargetColor.R},{TargetColor.G},{TargetColor.B}) at ({ClientX}, {ClientY}) in {sw.ElapsedMilliseconds}ms.");
                }
            }

            if (sw.Elapsed >= Timeout)
            {
                break;
            }

            TimeSpan remaining = Timeout - sw.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            TimeSpan delay = remaining < PollInterval ? remaining : PollInterval;
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                return MacroActionResult.Cancelled();
            }
        }

        return MacroActionResult.Timeout(
            $"Timed out: Color RGB({TargetColor.R},{TargetColor.G},{TargetColor.B}) (tol={Tolerance}) at ({ClientX}, {ClientY}) was not matched within {Timeout.TotalMilliseconds:F0}ms.");
    }
}
