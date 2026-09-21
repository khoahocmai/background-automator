using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Win32;

namespace BackgroundClicker.Core.Security;

public enum ElevationCompatibility
{
    Compatible,
    UipiMismatch,
    Unknown
}

public sealed class ElevationCheckResult
{
    public ElevationCompatibility Compatibility { get; }
    public bool CurrentProcessElevated { get; }
    public bool? TargetProcessElevated { get; }
    public string Message { get; }

    public ElevationCheckResult(
        ElevationCompatibility compatibility,
        bool currentProcessElevated,
        bool? targetProcessElevated,
        string message)
    {
        Compatibility = compatibility;
        CurrentProcessElevated = currentProcessElevated;
        TargetProcessElevated = targetProcessElevated;
        Message = message;
    }

    public static ElevationCheckResult CreateCompatible(bool currentElevated, bool? targetElevated) =>
        new(ElevationCompatibility.Compatible, currentElevated, targetElevated, "Process privileges are compatible.");

    public static ElevationCheckResult CreateUipiMismatch() =>
        new(ElevationCompatibility.UipiMismatch, false, true,
            "UIPI Warning: Target process is running with Administrator privileges while BackgroundClicker is not. Windows will drop background click messages. Restart BackgroundClicker as Administrator.");

    public static ElevationCheckResult CreateUnknown(bool currentElevated, string reason) =>
        new(ElevationCompatibility.Unknown, currentElevated, null,
            $"Target elevation could not be verified ({reason}). If clicks fail, consider running BackgroundClicker as Administrator.");
}

/// <summary>
/// Service for querying process elevation and diagnosing UIPI (User Interface Privilege Isolation) incompatibilities.
/// </summary>
public class ProcessElevationService
{
    private readonly IAppLogger? _logger;

    public ProcessElevationService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks whether the current BackgroundClicker process is running with administrative privileges.
    /// </summary>
    public virtual bool IsCurrentProcessElevated()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Failed to check current process elevation: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Checks whether the specified target process is running elevated.
    /// Returns null if elevation status cannot be determined (e.g. access denied).
    /// </summary>
    public virtual bool? IsProcessElevated(int processId)
    {
        if (processId <= 0)
            return null;

        if (processId == Environment.ProcessId)
            return IsCurrentProcessElevated();

        IntPtr hProcess = IntPtr.Zero;
        IntPtr hToken = IntPtr.Zero;
        IntPtr pElevation = IntPtr.Zero;

        try
        {
            hProcess = Kernel32.OpenProcess(NativeConstants.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
            if (hProcess == IntPtr.Zero)
            {
                _logger?.Debug($"OpenProcess failed for PID {processId}, error={Kernel32.GetLastError()}");
                return null;
            }

            if (!Advapi32.OpenProcessToken(hProcess, Advapi32.TOKEN_QUERY, out hToken) || hToken == IntPtr.Zero)
            {
                _logger?.Debug($"OpenProcessToken failed for PID {processId}, error={Kernel32.GetLastError()}");
                return null;
            }

            int elevationSize = Marshal.SizeOf<Advapi32.TOKEN_ELEVATION>();
            pElevation = Marshal.AllocHGlobal(elevationSize);

            if (Advapi32.GetTokenInformation(hToken, Advapi32.TokenElevation, pElevation, (uint)elevationSize, out _))
            {
                var elevation = Marshal.PtrToStructure<Advapi32.TOKEN_ELEVATION>(pElevation);
                return elevation.TokenIsElevated != 0;
            }
            else
            {
                _logger?.Debug($"GetTokenInformation failed for PID {processId}, error={Kernel32.GetLastError()}");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Exception determining elevation for PID {processId}: {ex.Message}");
            return null;
        }
        finally
        {
            if (pElevation != IntPtr.Zero)
                Marshal.FreeHGlobal(pElevation);
            if (hToken != IntPtr.Zero)
                Kernel32.CloseHandle(hToken);
            if (hProcess != IntPtr.Zero)
                Kernel32.CloseHandle(hProcess);
        }
    }

    /// <summary>
    /// Validates whether BackgroundClicker can post messages to the target process without UIPI blockage.
    /// </summary>
    public virtual ElevationCheckResult CheckCompatibility(int targetProcessId)
    {
        bool currentElevated = IsCurrentProcessElevated();

        // If current process is elevated, it can send messages to both elevated and non-elevated windows
        if (currentElevated)
        {
            return ElevationCheckResult.CreateCompatible(currentElevated, null);
        }

        bool? targetElevated = IsProcessElevated(targetProcessId);

        if (targetElevated == true)
        {
            _logger?.Warning($"UIPI mismatch detected: Target PID {targetProcessId} is elevated, but current process is not.");
            return ElevationCheckResult.CreateUipiMismatch();
        }

        if (targetElevated == false)
        {
            return ElevationCheckResult.CreateCompatible(false, false);
        }

        return ElevationCheckResult.CreateUnknown(false, "Access to target process information was denied or unavailable");
    }

    /// <summary>
    /// Attempts to restart the application as Administrator.
    /// </summary>
    public static bool RestartAsAdministrator(string? arguments = null)
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
                return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
