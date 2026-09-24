using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BackgroundAutomator.Win32;

/// <summary>
/// P/Invoke definitions for kernel32.dll and process helpers.
/// </summary>
public static class Kernel32
{
    private const string Kernel32Dll = "kernel32.dll";

    [DllImport(Kernel32Dll, SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport(Kernel32Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport(Kernel32Dll, SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

    [DllImport(Kernel32Dll)]
    public static extern IntPtr GetCurrentProcess();

    [DllImport(Kernel32Dll)]
    public static extern uint GetCurrentProcessId();

    [DllImport(Kernel32Dll)]
    public static extern uint GetCurrentThreadId();

    [DllImport(Kernel32Dll)]
    public static extern uint GetLastError();

    /// <summary>
    /// Safely resolves a process executable/module name for a given process ID without crashing on access denied or exit.
    /// Returns process name (e.g. "notepad.exe" or "notepad") or "[Unknown]".
    /// </summary>
    public static string GetProcessNameSafe(uint processId)
    {
        if (processId == 0)
            return "[System Idle]";

        // First attempt via standard .NET Process
        try
        {
            using var proc = Process.GetProcessById((int)processId);
            string name = proc.ProcessName;
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
            }
        }
        catch
        {
            // Process may have exited or access denied; fallback to QueryFullProcessImageName
        }

        // Fallback: QueryFullProcessImageName via limited information handle
        IntPtr hProc = OpenProcess(NativeConstants.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (hProc != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                uint size = (uint)sb.Capacity;
                if (QueryFullProcessImageName(hProc, 0, sb, ref size))
                {
                    string fullPath = sb.ToString();
                    return Path.GetFileName(fullPath);
                }
            }
            catch
            {
                // Ignore failure
            }
            finally
            {
                CloseHandle(hProc);
            }
        }

        return "[Unknown]";
    }
}
