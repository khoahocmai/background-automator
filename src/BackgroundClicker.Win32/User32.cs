using System.Runtime.InteropServices;
using System.Text;

namespace BackgroundClicker.Win32;

/// <summary>
/// P/Invoke definitions for user32.dll and related windowing APIs.
/// </summary>
public static class User32
{
    private const string User32Dll = "user32.dll";
    private const string DwmapiDll = "dwmapi.dll";

    [DllImport(User32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport(User32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport(User32Dll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport(User32Dll, SetLastError = true)]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport(User32Dll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport(User32Dll, SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport(User32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport(User32Dll)]
    public static extern IntPtr WindowFromPoint(POINT point);

    [DllImport(User32Dll)]
    public static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, POINT pt, uint uFlags);

    [DllImport(User32Dll)]
    public static extern IntPtr RealChildWindowFromPoint(IntPtr hwndParent, POINT ptParentClientCoordinates);

    [DllImport(User32Dll, SetLastError = true)]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport(User32Dll)]
    public static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport(User32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [DllImport(User32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport(User32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport(User32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport(User32Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport(User32Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport(User32Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowEnabled(IntPtr hWnd);

    [DllImport(User32Dll, SetLastError = true)]
    public static extern IntPtr SetCapture(IntPtr hWnd);

    [DllImport(User32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();

    [DllImport(User32Dll)]
    public static extern IntPtr GetCapture();

    [DllImport(User32Dll, SetLastError = true)]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport(User32Dll, EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport(User32Dll, EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    [DllImport(User32Dll, CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport(User32Dll, CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport(User32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetProcessDpiAwarenessContext(IntPtr dpiFlag);

    [DllImport(User32Dll, SetLastError = true)]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport(DwmapiDll, PreserveSig = true)]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    /// <summary>
    /// Safely gets the window text/title for a given HWND. Returns empty string if failed or empty.
    /// </summary>
    public static string GetWindowTextSafe(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
            return string.Empty;

        int length = GetWindowTextLength(hWnd);
        if (length <= 0)
            return string.Empty;

        var sb = new StringBuilder(length + 1);
        int copied = GetWindowText(hWnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Safely gets the window class name for a given HWND.
    /// </summary>
    public static string GetClassNameSafe(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !IsWindow(hWnd))
            return string.Empty;

        var sb = new StringBuilder(256);
        int copied = GetClassName(hWnd, sb, sb.Capacity);
        return copied > 0 ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Checks if a window is cloaked by Desktop Window Manager (e.g. suspended UWP/hidden virtual desktop).
    /// </summary>
    public static bool IsWindowCloaked(IntPtr hWnd)
    {
        try
        {
            int hr = DwmGetWindowAttribute(hWnd, NativeConstants.DWMWA_CLOAKED, out int cloaked, sizeof(int));
            return hr == 0 && cloaked != 0;
        }
        catch
        {
            return false;
        }
    }
}
