namespace BackgroundClicker.Core.Capture;

/// <summary>
/// Service providing background visual capture of a window's client area without moving
/// the physical mouse or bringing the window to foreground.
/// </summary>
public interface IWindowCaptureService
{
    /// <summary>
    /// Captures the client area of the target window using Win32 PrintWindow.
    /// Returns a new <see cref="WindowCapture"/> instance if successful, or null if the window is invalid,
    /// minimized, has zero area, or the capture operation failed.
    /// Caller is responsible for disposing the returned <see cref="WindowCapture"/>.
    /// </summary>
    /// <param name="hWnd">HWND of the target window.</param>
    /// <returns>A disposable <see cref="WindowCapture"/> or null.</returns>
    WindowCapture? CaptureClientArea(IntPtr hWnd);
}
