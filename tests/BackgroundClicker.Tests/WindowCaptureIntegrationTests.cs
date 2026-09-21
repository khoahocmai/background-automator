using System.Drawing;
using BackgroundClicker.Core.Capture;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Win32;
using Xunit;

namespace BackgroundClicker.Tests;

public class WindowCaptureIntegrationTests
{
    private readonly InMemoryLogger _logger = new();

    [Fact]
    public void CaptureClientArea_CapturesTestTargetColorPanelAccurately()
    {
        using var fixture = new TestTargetFixture();
        Assert.NotEqual(IntPtr.Zero, fixture.ColorPanelHwnd);

        var captureService = new GdiWindowCaptureService(_logger);
        using WindowCapture? capture = captureService.CaptureClientArea(fixture.ColorPanelHwnd);

        Assert.NotNull(capture);
        Assert.True(capture.Width > 0, "Color panel width must be greater than zero.");
        Assert.True(capture.Height > 0, "Color panel height must be greater than zero.");

        // Initial default color of TestTarget's ColorPanel is Solid Red (255, 0, 0)
        int centerX = capture.Width / 2;
        int centerY = capture.Height / 2;

        Color pixel = capture.GetPixel(centerX, centerY);
        Assert.Equal(255, pixel.R);
        Assert.True(pixel.G <= 10, $"Expected Red G <= 10, got {pixel.G}");
        Assert.True(pixel.B <= 10, $"Expected Red B <= 10, got {pixel.B}");

        Assert.True(capture.MatchesColor(centerX, centerY, Color.FromArgb(255, 0, 0), tolerance: 5));
    }

    [Fact]
    public void CaptureClientArea_WindowMoveInvariance()
    {
        using var fixture = new TestTargetFixture();
        Assert.NotEqual(IntPtr.Zero, fixture.ColorPanelHwnd);

        var captureService = new GdiWindowCaptureService(_logger);

        // Capture 1: Initial position
        Color initialPixel;
        using (WindowCapture? capture1 = captureService.CaptureClientArea(fixture.ColorPanelHwnd))
        {
            Assert.NotNull(capture1);
            initialPixel = capture1.GetPixel(15, 15);
        }

        // Move window to (150, 150)
        User32.SetWindowPos(
            fixture.MainWindowHandle,
            IntPtr.Zero,
            150,
            150,
            0,
            0,
            NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);

        Thread.Sleep(100);

        // Capture 2: After move
        using (WindowCapture? capture2 = captureService.CaptureClientArea(fixture.ColorPanelHwnd))
        {
            Assert.NotNull(capture2);
            Color movedPixel = capture2.GetPixel(15, 15);
            Assert.Equal(initialPixel.R, movedPixel.R);
            Assert.Equal(initialPixel.G, movedPixel.G);
            Assert.Equal(initialPixel.B, movedPixel.B);
        }

        // Move window to (350, 250)
        User32.SetWindowPos(
            fixture.MainWindowHandle,
            IntPtr.Zero,
            350,
            250,
            0,
            0,
            NativeConstants.SWP_NOSIZE | NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);

        Thread.Sleep(100);

        // Capture 3: After second move
        using (WindowCapture? capture3 = captureService.CaptureClientArea(fixture.ColorPanelHwnd))
        {
            Assert.NotNull(capture3);
            Color movedAgainPixel = capture3.GetPixel(15, 15);
            Assert.Equal(initialPixel.R, movedAgainPixel.R);
            Assert.Equal(initialPixel.G, movedAgainPixel.G);
            Assert.Equal(initialPixel.B, movedAgainPixel.B);
        }
    }
}
