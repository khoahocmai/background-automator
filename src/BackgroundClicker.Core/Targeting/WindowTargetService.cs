using System.Drawing;
using BackgroundClicker.Core.Coordinates;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Win32;

namespace BackgroundClicker.Core.Targeting;

/// <summary>
/// Service for enumerating usable top-level windows and resolving root & child targets.
/// </summary>
public class WindowTargetService
{
    private readonly CoordinateService _coordinateService;
    private readonly IAppLogger? _logger;

    public WindowTargetService(CoordinateService coordinateService, IAppLogger? logger = null)
    {
        _coordinateService = coordinateService;
        _logger = logger;
    }

    /// <summary>
    /// Enumerates usable top-level windows, safely handling race conditions (e.g. processes exiting).
    /// Filters out BackgroundClicker's own process, invisible windows, and empty system windows.
    /// </summary>
    public virtual IReadOnlyList<WindowTargetCandidate> EnumerateTopLevelWindows(IntPtr ignoreRootHwnd = default)
    {
        var candidates = new List<WindowTargetCandidate>();
        uint currentProcessId = Kernel32.GetCurrentProcessId();

        try
        {
            User32.EnumWindows((hWnd, lParam) =>
            {
                // Basic handle validation
                if (hWnd == IntPtr.Zero || !User32.IsWindow(hWnd))
                    return true;

                if (ignoreRootHwnd != IntPtr.Zero && hWnd == ignoreRootHwnd)
                    return true;

                // Check visibility
                if (!User32.IsWindowVisible(hWnd))
                    return true;

                // Check DWM cloaking (Windows 10/11 virtual desktops, minimized UWP, etc.)
                if (User32.IsWindowCloaked(hWnd))
                    return true;

                // Determine process and thread
                uint threadId = User32.GetWindowThreadProcessId(hWnd, out uint processId);
                if (processId == currentProcessId || processId == 0)
                    return true; // Ignore self and system idle

                // Check window rect size - must have positive area
                if (!User32.GetWindowRect(hWnd, out RECT rect) || rect.Width <= 0 || rect.Height <= 0)
                    return true;

                // Check window styles
                long style = User32.GetWindowLongPtr(hWnd, NativeConstants.GWL_STYLE).ToInt64();
                long exStyle = User32.GetWindowLongPtr(hWnd, NativeConstants.GWL_EXSTYLE).ToInt64();

                // Skip child windows in top-level enum
                if ((style & NativeConstants.WS_CHILD) != 0)
                    return true;

                // Skip tool windows unless they explicitly declare WS_EX_APPWINDOW
                bool isToolWindow = (exStyle & NativeConstants.WS_EX_TOOLWINDOW) != 0;
                bool isAppWindow = (exStyle & NativeConstants.WS_EX_APPWINDOW) != 0;
                if (isToolWindow && !isAppWindow)
                    return true;

                // Query process name, window title, class name safely
                string processName = Kernel32.GetProcessNameSafe(processId);
                string title = User32.GetWindowTextSafe(hWnd);
                string className = User32.GetClassNameSafe(hWnd);

                // Add candidate
                candidates.Add(new WindowTargetCandidate(
                    Hwnd: hWnd,
                    ProcessId: (int)processId,
                    ThreadId: threadId,
                    ProcessName: processName,
                    WindowTitle: title,
                    WindowClass: className));

                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _logger?.Error("Exception occurred during window enumeration", ex);
        }

        // Sort: Windows with non-empty titles first, then by ProcessName, then Title
        return candidates
            .OrderByDescending(c => !string.IsNullOrWhiteSpace(c.WindowTitle))
            .ThenBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.WindowTitle, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Resolves the root HWND and deepest useful child HWND beneath a specific screen point.
    /// </summary>
    /// <param name="screenPoint">Global screen coordinate.</param>
    /// <param name="ignoreProcessRootHwnd">Optional HWND of own app window to avoid self-selection.</param>
    /// <returns>A fully resolved WindowTarget, or null if cursor is outside valid external windows.</returns>
    public WindowTarget? ResolveTargetFromScreenPoint(Point screenPoint, IntPtr ignoreProcessRootHwnd = default, bool allowCurrentProcess = false)
    {
        POINT pt = new(screenPoint.X, screenPoint.Y);
        IntPtr hwndUnderCursor = User32.WindowFromPoint(pt);

        if (hwndUnderCursor == IntPtr.Zero || !User32.IsWindow(hwndUnderCursor))
        {
            _logger?.Debug($"No window found under screen point {screenPoint}");
            return null;
        }

        // Resolve root HWND
        IntPtr rootHwnd = User32.GetAncestor(hwndUnderCursor, NativeConstants.GA_ROOT);
        if (rootHwnd == IntPtr.Zero)
        {
            rootHwnd = hwndUnderCursor;
        }

        // Ignore BackgroundClicker self-window unless testing explicitly allows current process
        uint currentProcessId = Kernel32.GetCurrentProcessId();
        User32.GetWindowThreadProcessId(rootHwnd, out uint rootProcessId);
        if ((!allowCurrentProcess && rootProcessId == currentProcessId) || (ignoreProcessRootHwnd != IntPtr.Zero && rootHwnd == ignoreProcessRootHwnd))
        {
            _logger?.Debug("Crosshair selection ignored: cursor is over BackgroundClicker window");
            return null;
        }

        // Find the deepest useful child HWND beneath this screen coordinate
        IntPtr targetHwnd = FindDeepestChild(hwndUnderCursor, pt);

        // Resolve Parent HWND
        IntPtr parentHwnd = User32.GetParent(targetHwnd);
        if (parentHwnd == IntPtr.Zero && targetHwnd != rootHwnd)
        {
            parentHwnd = User32.GetAncestor(targetHwnd, NativeConstants.GA_PARENT);
        }

        // Retrieve process and thread info
        uint targetThreadId = User32.GetWindowThreadProcessId(targetHwnd, out uint targetProcessId);
        string processName = Kernel32.GetProcessNameSafe(targetProcessId);

        // Query texts
        string targetTitle = User32.GetWindowTextSafe(targetHwnd);
        string rootTitle = User32.GetWindowTextSafe(rootHwnd);
        string title = !string.IsNullOrWhiteSpace(targetTitle) ? targetTitle : rootTitle;
        string className = User32.GetClassNameSafe(targetHwnd);

        // Calculate client coordinates relative to target HWND
        TargetPoint clientPoint = _coordinateService.ScreenToClient(targetHwnd, screenPoint);

        // Verify round-trip conversion
        _coordinateService.VerifyRoundTrip(targetHwnd, screenPoint, out _, out _);

        return new WindowTarget
        {
            RootHwnd = rootHwnd,
            TargetHwnd = targetHwnd,
            ParentHwnd = parentHwnd,
            ProcessId = (int)targetProcessId,
            ThreadId = targetThreadId,
            ProcessName = processName,
            WindowTitle = title,
            WindowClass = className,
            ScreenPoint = screenPoint,
            ClientPoint = clientPoint
        };
    }

    /// <summary>
    /// Resolves a WindowTarget given a top-level window candidate.
    /// </summary>
    public WindowTarget? ResolveTargetFromCandidate(WindowTargetCandidate candidate)
    {
        if (candidate.Hwnd == IntPtr.Zero || !User32.IsWindow(candidate.Hwnd))
        {
            _logger?.Warning($"Target window {HwndFormatter.Format(candidate.Hwnd)} is no longer valid");
            return null;
        }

        // Default coordinate: center of client rectangle
        Point clientCenter = Point.Empty;
        if (User32.GetClientRect(candidate.Hwnd, out RECT clientRect))
        {
            clientCenter = new Point(clientRect.Width / 2, clientRect.Height / 2);
        }

        TargetPoint clientPoint = new(candidate.Hwnd, clientCenter.X, clientCenter.Y);
        Point screenPoint = _coordinateService.ClientToScreen(clientPoint);

        return new WindowTarget
        {
            RootHwnd = candidate.Hwnd,
            TargetHwnd = candidate.Hwnd,
            ParentHwnd = IntPtr.Zero,
            ProcessId = candidate.ProcessId,
            ThreadId = candidate.ThreadId,
            ProcessName = candidate.ProcessName,
            WindowTitle = candidate.WindowTitle,
            WindowClass = candidate.WindowClass,
            ScreenPoint = screenPoint,
            ClientPoint = clientPoint
        };
    }

    /// <summary>
    /// Refreshes target information and recalculates coordinates.
    /// If an updated screen point is provided, converts it to client coordinates.
    /// Otherwise, converts the stored client coordinates to current screen coordinates (e.g. after window move/resize).
    /// </summary>
    public WindowTarget? RefreshTarget(WindowTarget existing, Point? updatedScreenPoint = null)
    {
        if (!existing.IsWindowValid())
        {
            _logger?.Warning($"Cannot refresh target: HWND {HwndFormatter.Format(existing.TargetHwnd)} has been closed");
            return null;
        }

        Point screenPoint;
        TargetPoint clientPoint;

        if (updatedScreenPoint.HasValue)
        {
            screenPoint = updatedScreenPoint.Value;
            clientPoint = _coordinateService.ScreenToClient(existing.TargetHwnd, screenPoint);
        }
        else
        {
            clientPoint = existing.ClientPoint;
            screenPoint = _coordinateService.ClientToScreen(clientPoint);
        }

        // Re-query titles and process name in case window changed
        string targetTitle = User32.GetWindowTextSafe(existing.TargetHwnd);
        string rootTitle = User32.GetWindowTextSafe(existing.RootHwnd);
        string title = !string.IsNullOrWhiteSpace(targetTitle) ? targetTitle : rootTitle;

        return new WindowTarget
        {
            RootHwnd = existing.RootHwnd,
            TargetHwnd = existing.TargetHwnd,
            ParentHwnd = existing.ParentHwnd,
            ProcessId = existing.ProcessId,
            ThreadId = existing.ThreadId,
            ProcessName = existing.ProcessName,
            WindowTitle = title,
            WindowClass = existing.WindowClass,
            ScreenPoint = screenPoint,
            ClientPoint = clientPoint
        };
    }

    /// <summary>
    /// Recursively drills down to find the deepest visible, non-transparent child window containing the point.
    /// </summary>
    private static IntPtr FindDeepestChild(IntPtr parent, POINT screenPt)
    {
        if (parent == IntPtr.Zero || !User32.IsWindow(parent))
            return parent;

        IntPtr current = parent;

        while (true)
        {
            POINT clientPt = screenPt;
            if (!User32.ScreenToClient(current, ref clientPt))
                break;

            IntPtr child = User32.ChildWindowFromPointEx(
                current,
                clientPt,
                NativeConstants.CWP_SKIPINVISIBLE | NativeConstants.CWP_SKIPTRANSPARENT);

            if (child == IntPtr.Zero || child == current)
                break;

            current = child;
        }

        return current;
    }
}
