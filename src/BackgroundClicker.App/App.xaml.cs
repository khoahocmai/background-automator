using System.Windows;
using BackgroundClicker.Win32;

namespace BackgroundClicker.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            User32.SetProcessDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        }
        catch
        {
            // Awareness already established by manifest or OS unsupported
        }

        base.OnStartup(e);
    }
}
