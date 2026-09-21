using System.Windows.Forms;
using BackgroundClicker.TestTarget.Forms;
using BackgroundClicker.Win32;

namespace BackgroundClicker.TestTarget;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Enforce PerMonitorV2 high-DPI scaling from earliest possible point
        try
        {
            User32.SetProcessDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            // Fallback for older OS if needed
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Application.Run(new TestTargetForm());
    }
}