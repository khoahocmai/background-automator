using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;

namespace BackgroundClicker.Core.Clicking;

/// <summary>
/// Production engine delivering background mouse messages to target HWNDs via Win32 PostMessage.
/// Guarantees that the user's physical mouse cursor (GetCursorPos/SetCursorPos) remains untouched.
/// </summary>
public sealed class BackgroundClickerEngine : IBackgroundClicker
{
    private readonly IAppLogger? _logger;

    /// <summary>
    /// Milliseconds to delay between discrete posted Win32 mouse messages.
    /// Default is 0 (no delay).
    /// </summary>
    public int MessagePacingMilliseconds { get; set; }

    public BackgroundClickerEngine(IAppLogger? logger = null, int messagePacingMilliseconds = 0)
    {
        _logger = logger;
        MessagePacingMilliseconds = messagePacingMilliseconds;
    }

    /// <inheritdoc />
    public ClickResult Click(TargetPoint target)
    {
        if (target.Hwnd == IntPtr.Zero || !User32.IsWindow(target.Hwnd))
        {
            _logger?.Warning($"Cannot click: target HWND {HwndFormatter.Format(target.Hwnd)} is invalid or closed");
            return ClickResult.InvalidTarget;
        }

        IntPtr lParam = MouseMessageHelper.MakeMouseLParam(target.ClientX, target.ClientY);

        // 1. WM_MOUSEMOVE (hover coordinate preparation)
        if (!User32.PostMessage(target.Hwnd, NativeConstants.WM_MOUSEMOVE, IntPtr.Zero, lParam))
        {
            _logger?.Warning($"PostMessage WM_MOUSEMOVE failed for HWND {HwndFormatter.Format(target.Hwnd)}");
            return ClickResult.PostFailed;
        }

        ApplyPacing();

        // 2. WM_LBUTTONDOWN (MK_LBUTTON flag set in wParam)
        if (!User32.PostMessage(target.Hwnd, NativeConstants.WM_LBUTTONDOWN, (IntPtr)NativeConstants.MK_LBUTTON, lParam))
        {
            _logger?.Warning($"PostMessage WM_LBUTTONDOWN failed for HWND {HwndFormatter.Format(target.Hwnd)}");
            return ClickResult.PostFailed;
        }

        ApplyPacing();

        // 3. WM_LBUTTONUP (button released, wParam = 0)
        if (!User32.PostMessage(target.Hwnd, NativeConstants.WM_LBUTTONUP, IntPtr.Zero, lParam))
        {
            _logger?.Warning($"PostMessage WM_LBUTTONUP failed for HWND {HwndFormatter.Format(target.Hwnd)}");
            return ClickResult.PostFailed;
        }

        return ClickResult.Success;
    }

    /// <inheritdoc />
    public ClickResult DoubleClick(TargetPoint target)
    {
        if (target.Hwnd == IntPtr.Zero || !User32.IsWindow(target.Hwnd))
        {
            _logger?.Warning($"Cannot double-click: target HWND {HwndFormatter.Format(target.Hwnd)} is invalid or closed");
            return ClickResult.InvalidTarget;
        }

        IntPtr lParam = MouseMessageHelper.MakeMouseLParam(target.ClientX, target.ClientY);

        // Explicit Windows double-click sequence:
        // WM_MOUSEMOVE -> WM_LBUTTONDOWN -> WM_LBUTTONUP -> WM_LBUTTONDBLCLK -> WM_LBUTTONUP

        // 1. WM_MOUSEMOVE
        if (!User32.PostMessage(target.Hwnd, NativeConstants.WM_MOUSEMOVE, IntPtr.Zero, lParam))
        {
            _logger?.Warning($"PostMessage WM_MOUSEMOVE failed for HWND {HwndFormatter.Format(target.Hwnd)}");
            return ClickResult.PostFailed;
        }

        ApplyPacing();

        // 2. WM_LBUTTONDOWN (MK_LBUTTON)
        if (!User32.PostMessage(target.Hwnd, NativeConstants.WM_LBUTTONDOWN, (IntPtr)NativeConstants.MK_LBUTTON, lParam))
        {
            _logger?.Warning($"PostMessage WM_LBUTTONDOWN failed for HWND {HwndFormatter.Format(target.Hwnd)}");
            return ClickResult.PostFailed;
        }

        ApplyPacing();

        // 3. WM_LBUTTONUP
        if (!User32.PostMessage(target.Hwnd, NativeConstants.WM_LBUTTONUP, IntPtr.Zero, lParam))
        {
            _logger?.Warning($"PostMessage WM_LBUTTONUP failed for HWND {HwndFormatter.Format(target.Hwnd)}");
            return ClickResult.PostFailed;
        }

        ApplyPacing();

        // 4. WM_LBUTTONDBLCLK (MK_LBUTTON)
        if (!User32.PostMessage(target.Hwnd, NativeConstants.WM_LBUTTONDBLCLK, (IntPtr)NativeConstants.MK_LBUTTON, lParam))
        {
            _logger?.Warning($"PostMessage WM_LBUTTONDBLCLK failed for HWND {HwndFormatter.Format(target.Hwnd)}");
            return ClickResult.PostFailed;
        }

        ApplyPacing();

        // 5. WM_LBUTTONUP
        if (!User32.PostMessage(target.Hwnd, NativeConstants.WM_LBUTTONUP, IntPtr.Zero, lParam))
        {
            _logger?.Warning($"PostMessage second WM_LBUTTONUP failed for HWND {HwndFormatter.Format(target.Hwnd)}");
            return ClickResult.PostFailed;
        }

        return ClickResult.Success;
    }

    private void ApplyPacing()
    {
        if (MessagePacingMilliseconds > 0)
        {
            Thread.Sleep(MessagePacingMilliseconds);
        }
    }
}

