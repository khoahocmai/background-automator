using System.Diagnostics;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.Core.Keyboard;

/// <summary>
/// Production Win32 implementation of <see cref="IWindowForegroundService"/>.
/// </summary>
public sealed class Win32WindowForegroundService : IWindowForegroundService
{
    private readonly IAppLogger? _logger;

    public Win32WindowForegroundService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public IntPtr GetForegroundWindow() => User32.GetForegroundWindow();

    public bool ActivateWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !User32.IsWindow(hWnd))
            return false;

        return User32.SetForegroundWindow(hWnd);
    }

    public bool RestoreForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !User32.IsWindow(hWnd))
            return false;

        return User32.SetForegroundWindow(hWnd);
    }

    public bool IsWindowMinimized(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !User32.IsWindow(hWnd))
            return false;

        return User32.IsIconic(hWnd);
    }

    public bool IsWindow(IntPtr hWnd) => User32.IsWindow(hWnd);

    public bool IsWindowVisible(IntPtr hWnd) => User32.IsWindowVisible(hWnd);

    public string GetProcessName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !User32.IsWindow(hWnd))
            return string.Empty;

        try
        {
            User32.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid != 0)
            {
                using var proc = Process.GetProcessById((int)pid);
                return proc.ProcessName;
            }
        }
        catch (Exception ex)
        {
            _logger?.Debug($"Failed to get process name for HWND 0x{hWnd.ToInt64():X8}: {ex.Message}");
        }

        return string.Empty;
    }

    public string GetWindowClass(IntPtr hWnd) => User32.GetClassNameSafe(hWnd);

    public IntPtr GetRootWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !User32.IsWindow(hWnd))
            return IntPtr.Zero;

        IntPtr root = User32.GetAncestor(hWnd, NativeConstants.GA_ROOT);
        return root != IntPtr.Zero ? root : hWnd;
    }
}
