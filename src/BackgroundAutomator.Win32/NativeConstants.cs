namespace BackgroundAutomator.Win32;

/// <summary>
/// Centralized Win32 native constants.
/// </summary>
public static class NativeConstants
{
    // Window Messages
    public const uint WM_NULL = 0x0000;
    public const uint WM_CREATE = 0x0001;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_MOVE = 0x0003;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;
    public const uint WM_ENABLE = 0x000A;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;

    // Keyboard Messages
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_CHAR = 0x0102;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;

    // Mouse Messages
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_RBUTTONDBLCLK = 0x0206;
    public const uint WM_MBUTTONDOWN = 0x0207;
    public const uint WM_MBUTTONUP = 0x0208;
    public const uint WM_MBUTTONDBLCLK = 0x0209;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_CAPTURECHANGED = 0x0215;

    // Mouse Key States (wParam in mouse messages)
    public const int MK_LBUTTON = 0x0001;
    public const int MK_RBUTTON = 0x0002;
    public const int MK_SHIFT = 0x0004;
    public const int MK_CONTROL = 0x0008;
    public const int MK_MBUTTON = 0x0010;
    public const int MK_XBUTTON1 = 0x0020;
    public const int MK_XBUTTON2 = 0x0040;

    // Hotkey Messages and Modifiers
    public const uint WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    // Virtual Key Codes for Hotkeys
    public const uint VK_F6 = 0x75;
    public const uint VK_F7 = 0x76;

    // Command Messages
    public const uint WM_COMMAND = 0x0111;

    // GetAncestor Flags
    public const uint GA_PARENT = 1;
    public const uint GA_ROOT = 2;
    public const uint GA_ROOTOWNER = 3;

    // ChildWindowFromPointEx Flags
    public const uint CWP_ALL = 0x0000;
    public const uint CWP_SKIPINVISIBLE = 0x0001;
    public const uint CWP_SKIPDISABLED = 0x0002;
    public const uint CWP_SKIPTRANSPARENT = 0x0004;

    // Window Field Offsets (GetWindowLong / GetWindowLongPtr)
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int GWL_WNDPROC = -4;
    public const int GWL_HINSTANCE = -6;
    public const int GWL_HWNDPARENT = -8;
    public const int GWL_ID = -12;
    public const int GWL_USERDATA = -21;

    // Window Styles
    public const long WS_OVERLAPPED = 0x00000000L;
    public const long WS_POPUP = 0x80000000L;
    public const long WS_CHILD = 0x40000000L;
    public const long WS_MINIMIZE = 0x20000000L;
    public const long WS_VISIBLE = 0x10000000L;
    public const long WS_DISABLED = 0x08000000L;
    public const long WS_CLIPSIBLINGS = 0x04000000L;
    public const long WS_CLIPCHILDREN = 0x02000000L;
    public const long WS_MAXIMIZE = 0x01000000L;

    // Extended Window Styles
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_APPWINDOW = 0x00040000L;
    public const long WS_EX_TRANSPARENT = 0x00000020L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;

    // GetWindow Commands
    public const uint GW_HWNDFIRST = 0;
    public const uint GW_HWNDLAST = 1;
    public const uint GW_HWNDNEXT = 2;
    public const uint GW_HWNDPREV = 3;
    public const uint GW_OWNER = 4;
    public const uint GW_CHILD = 5;
    public const uint GW_ENABLEDPOPUP = 6;

    // Process Access Rights
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_VM_READ = 0x0010;

    // DWM Cloaked Attribute
    public const int DWMWA_CLOAKED = 14;
    public const int DWM_CLOAKED_APP = 0x0000001;
    public const int DWM_CLOAKED_SHELL = 0x0000002;
    public const int DWM_CLOAKED_INHERITED = 0x0000004;

    // DPI Awareness Contexts
    public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    // PrintWindow Flags
    public const uint PW_CLIENTONLY = 0x00000001;
    public const uint PW_RENDERFULLCONTENT = 0x00000002;

    // SetWindowPos Flags
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
}
