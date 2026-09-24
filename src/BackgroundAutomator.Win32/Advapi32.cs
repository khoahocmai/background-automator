using System.Runtime.InteropServices;

namespace BackgroundAutomator.Win32;

/// <summary>
/// P/Invoke definitions for advapi32.dll (process tokens, elevation).
/// </summary>
public static class Advapi32
{
    private const string Advapi32Dll = "advapi32.dll";

    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenElevation = 20;

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_ELEVATION
    {
        public int TokenIsElevated;
    }

    [DllImport(Advapi32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport(Advapi32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetTokenInformation(
        IntPtr TokenHandle,
        int TokenInformationClass,
        IntPtr TokenInformation,
        uint TokenInformationLength,
        out uint ReturnLength);
}
