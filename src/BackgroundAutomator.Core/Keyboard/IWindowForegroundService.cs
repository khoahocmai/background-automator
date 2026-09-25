namespace BackgroundAutomator.Core.Keyboard;

/// <summary>
/// Abstraction for window foreground activation, inspection, and restoration.
/// Allows unit testing foreground pulses and race condition handling without desktop focus manipulation.
/// </summary>
public interface IWindowForegroundService
{
    /// <summary>
    /// Gets the handle of the current foreground window on the active desktop.
    /// </summary>
    IntPtr GetForegroundWindow();

    /// <summary>
    /// Requests foreground activation for the specified window handle.
    /// </summary>
    bool ActivateWindow(IntPtr hWnd);

    /// <summary>
    /// Restores foreground focus to a previously active window handle.
    /// </summary>
    bool RestoreForegroundWindow(IntPtr hWnd);

    /// <summary>
    /// Checks whether the specified window is currently minimized / iconic.
    /// </summary>
    bool IsWindowMinimized(IntPtr hWnd);

    /// <summary>
    /// Checks whether the window handle exists and is valid.
    /// </summary>
    bool IsWindow(IntPtr hWnd);

    /// <summary>
    /// Checks whether the window is visible.
    /// </summary>
    bool IsWindowVisible(IntPtr hWnd);

    /// <summary>
    /// Gets the process name associated with the window.
    /// </summary>
    string GetProcessName(IntPtr hWnd);

    /// <summary>
    /// Gets the process ID associated with the window.
    /// </summary>
    int GetProcessId(IntPtr hWnd);

    /// <summary>
    /// Gets the window class name.
    /// </summary>
    string GetWindowClass(IntPtr hWnd);

    /// <summary>
    /// Resolves the root / top-level ancestor window for a given child or control HWND.
    /// </summary>
    IntPtr GetRootWindow(IntPtr hWnd);
}
