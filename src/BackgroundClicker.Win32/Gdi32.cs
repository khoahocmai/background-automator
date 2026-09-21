using System.Runtime.InteropServices;

namespace BackgroundClicker.Win32;

/// <summary>
/// P/Invoke definitions for gdi32.dll.
/// </summary>
public static class Gdi32
{
    private const string Gdi32Dll = "gdi32.dll";

    public const int LOGPIXELSX = 88;
    public const int LOGPIXELSY = 90;

    [DllImport(Gdi32Dll, SetLastError = true)]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport(Gdi32Dll, SetLastError = true)]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport(Gdi32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteDC(IntPtr hdc);

    [DllImport(Gdi32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport(Gdi32Dll, SetLastError = true)]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
}
