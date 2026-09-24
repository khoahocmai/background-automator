using System.Diagnostics;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.Tests;

public sealed class TestTargetFixture : IDisposable
{
    public Process Process { get; }
    public IntPtr MainWindowHandle { get; private set; }
    public IntPtr ButtonHwnd { get; private set; }
    public IntPtr ColorPanelHwnd { get; private set; }
    public IntPtr DelayGreenButtonHwnd { get; private set; }
    public IntPtr SetRedButtonHwnd { get; private set; }
    public IntPtr SetGreenButtonHwnd { get; private set; }
    public string LogFilePath { get; }

    public TestTargetFixture(string? additionalArgs = null)
    {
        LogFilePath = Path.Combine(Path.GetTempPath(), $"testtarget_fixture_{Guid.NewGuid():N}.tsv");

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string testTargetExe = Path.GetFullPath(Path.Combine(
            baseDir,
            @"..\..\..\..\BackgroundAutomator.TestTarget\bin\Debug\net8.0-windows\BackgroundAutomator.TestTarget.exe"));

        if (!File.Exists(testTargetExe))
        {
            testTargetExe = Path.GetFullPath(Path.Combine(baseDir, "BackgroundAutomator.TestTarget.exe"));
        }

        if (!File.Exists(testTargetExe))
        {
            throw new FileNotFoundException($"Cannot locate TestTarget executable at {testTargetExe}");
        }

        string arguments = $"--log-file \"{LogFilePath}\" --log-mouse-move {additionalArgs ?? string.Empty}".Trim();
        var psi = new ProcessStartInfo
        {
            FileName = testTargetExe,
            Arguments = arguments,
            UseShellExecute = true
        };

        Process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch TestTarget process");

        // Wait for main window handle
        for (int i = 0; i < 50; i++)
        {
            Thread.Sleep(100);
            Process.Refresh();
            if (Process.MainWindowHandle != IntPtr.Zero)
            {
                MainWindowHandle = Process.MainWindowHandle;
                break;
            }
        }

        if (MainWindowHandle == IntPtr.Zero)
        {
            throw new TimeoutException("TestTarget did not create a main window handle within 5 seconds.");
        }

        // Resolve child handles
        ResolveChildHwnds();
    }

    private void ResolveChildHwnds()
    {
        for (int retry = 0; retry < 30; retry++)
        {
            User32.EnumChildWindows(MainWindowHandle, (hwnd, lParam) =>
            {
                string text = User32.GetWindowTextSafe(hwnd);
                if (text.Contains("Target Button", StringComparison.OrdinalIgnoreCase))
                {
                    ButtonHwnd = hwnd;
                }
                else if (text.Contains("Delay Green", StringComparison.OrdinalIgnoreCase))
                {
                    DelayGreenButtonHwnd = hwnd;
                }
                else if (text.Equals("Set Red", StringComparison.OrdinalIgnoreCase))
                {
                    SetRedButtonHwnd = hwnd;
                }
                else if (text.Equals("Set Green", StringComparison.OrdinalIgnoreCase))
                {
                    SetGreenButtonHwnd = hwnd;
                }
                else if (text.StartsWith("Process: BackgroundAutomator.TestTarget.exe", StringComparison.OrdinalIgnoreCase))
                {
                    // Parse Color HWND: 0x...
                    int idx = text.IndexOf("Color HWND: ", StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0)
                    {
                        string hex = text.Substring(idx + 12).Trim();
                        int end = hex.IndexOf(' ');
                        if (end > 0) hex = hex.Substring(0, end);
                        if (HwndFormatter.TryParse(hex, out IntPtr colHwnd))
                        {
                            ColorPanelHwnd = colHwnd;
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (ButtonHwnd != IntPtr.Zero && ColorPanelHwnd != IntPtr.Zero)
            {
                break;
            }

            Thread.Sleep(100);
        }
    }

    public void Dispose()
    {
        try
        {
            if (!Process.HasExited)
            {
                Process.Kill();
                Process.WaitForExit(3000);
            }
            Process.Dispose();
        }
        catch
        {
        }

        try
        {
            if (File.Exists(LogFilePath))
            {
                File.Delete(LogFilePath);
            }
        }
        catch
        {
        }
    }
}
