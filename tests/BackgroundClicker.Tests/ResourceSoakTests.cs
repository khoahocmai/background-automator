using System.Windows.Forms;
using BackgroundClicker.Core.Capture;
using BackgroundClicker.Win32;
using Xunit;

namespace BackgroundClicker.Tests;

public class ResourceSoakTests
{
    [Fact]
    public void WindowCapture_SoakTest_ZeroGdiLeaksUnderHeavyWorkload()
    {
        using var form = new Form
        {
            Width = 200,
            Height = 200,
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(50, 50)
        };
        form.Show();
        Application.DoEvents();

        using var captureService = new GdiWindowCaptureService();
        IntPtr hCurrentProcess = Kernel32.GetCurrentProcess();

        // Warmup: run a few captures so one-time initialization completes
        for (int i = 0; i < 10; i++)
        {
            using var warmup = captureService.CaptureClientArea(form.Handle);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        uint initialGdi = User32.GetGuiResources(hCurrentProcess, User32.GR_GDIOBJECTS);
        uint initialUser = User32.GetGuiResources(hCurrentProcess, User32.GR_USEROBJECTS);

        // Heavy capture soak: 500 consecutive full captures
        const int iterations = 500;
        for (int i = 0; i < iterations; i++)
        {
            using var cap = captureService.CaptureClientArea(form.Handle);
            Assert.NotNull(cap);
            // Exercise pixel reads
            var color = cap.GetPixel(5, 5);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        uint finalGdi = User32.GetGuiResources(hCurrentProcess, User32.GR_GDIOBJECTS);
        uint finalUser = User32.GetGuiResources(hCurrentProcess, User32.GR_USEROBJECTS);

        int gdiDelta = (int)finalGdi - (int)initialGdi;
        int userDelta = (int)finalUser - (int)initialUser;

        // If there were a leak of HBITMAP or HDC, gdiDelta would be ~500.
        // A delta of <= 1 verifies no leaks occurred.
        Assert.True(gdiDelta <= 1, $"GDI objects leaked during 500 captures! Initial: {initialGdi}, Final: {finalGdi}, Delta: {gdiDelta}");
        Assert.True(userDelta <= 1, $"USER objects leaked during 500 captures! Initial: {initialUser}, Final: {finalUser}, Delta: {userDelta}");

        form.Close();
    }
}
