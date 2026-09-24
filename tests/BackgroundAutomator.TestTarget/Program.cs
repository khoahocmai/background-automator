using System.Windows.Forms;
using BackgroundAutomator.TestTarget.Forms;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.TestTarget;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // DPI awareness source of truth is declared via app.manifest (PerMonitorV2)
        // and project configuration (<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>).
        // Best-effort runtime attempt for environments where manifest is not loaded; ignore if already configured.
        try
        {
            User32.SetProcessDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            // Ignored: awareness already established by manifest or OS unsupported
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TestTargetForm(args));
    }
}