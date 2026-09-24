using BackgroundAutomator.Core.Targeting;

namespace BackgroundAutomator.Core.Clicking;

/// <summary>
/// Abstraction for dispatching background mouse messages directly to target HWND client coordinates
/// without moving or disrupting the physical mouse cursor.
/// </summary>
public interface IBackgroundClicker
{
    /// <summary>
    /// Delivers a background single-click message sequence (WM_MOUSEMOVE -> WM_LBUTTONDOWN -> WM_LBUTTONUP)
    /// to the specified target HWND and client coordinates.
    /// </summary>
    /// <param name="target">The HWND-bound target client coordinate.</param>
    /// <returns>A ClickResult indicating success, invalid target, or post failure.</returns>
    ClickResult Click(TargetPoint target);

    /// <summary>
    /// Delivers a background double-click message sequence (WM_MOUSEMOVE -> WM_LBUTTONDOWN -> WM_LBUTTONUP -> WM_LBUTTONDBLCLK -> WM_LBUTTONUP)
    /// to the specified target HWND and client coordinates.
    /// </summary>
    /// <param name="target">The HWND-bound target client coordinate.</param>
    /// <returns>A ClickResult indicating success, invalid target, or post failure.</returns>
    ClickResult DoubleClick(TargetPoint target);
}
