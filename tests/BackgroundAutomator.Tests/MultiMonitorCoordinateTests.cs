using System.Drawing;
using System.Windows.Forms;
using BackgroundAutomator.Core.Coordinates;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Win32;
using Xunit;

namespace BackgroundAutomator.Tests;

public class MultiMonitorCoordinateTests
{
    [Fact]
    public void CoordinateService_SupportsNegativeCoordinatesRoundTrip()
    {
        using var form = new Form
        {
            Width = 300,
            Height = 200,
            StartPosition = FormStartPosition.Manual
        };
        form.Show();

        // Simulate multi-monitor negative coordinate positioning (e.g. secondary monitor to the left/top)
        // Position window at (-500, -300)
        User32.SetWindowPos(
            form.Handle,
            IntPtr.Zero,
            -500,
            -300,
            300,
            200,
            NativeConstants.SWP_NOZORDER | NativeConstants.SWP_NOACTIVATE);

        var service = new CoordinateService();

        // 1. Client coordinate (50, 60) -> Screen coordinate (which will be negative!)
        var clientPt = new TargetPoint(form.Handle, 50, 60);
        Point screenPt = service.ClientToScreen(clientPt);

        // Verify round-trip: client -> screen -> client
        bool roundTripClient = service.VerifyRoundTrip(clientPt, out Point computedScreen, out TargetPoint roundTripClientPt);
        Assert.True(roundTripClient);
        Assert.Equal(50, roundTripClientPt.ClientX);
        Assert.Equal(60, roundTripClientPt.ClientY);

        // 2. Screen coordinate -> Client coordinate -> Screen coordinate
        bool roundTripScreen = service.VerifyRoundTrip(form.Handle, screenPt, out TargetPoint computedClient, out Point roundTripScreenPt);
        Assert.True(roundTripScreen);
        Assert.Equal(screenPt.X, roundTripScreenPt.X);
        Assert.Equal(screenPt.Y, roundTripScreenPt.Y);

        form.Close();
    }

    [Theory]
    [InlineData(-100, -50)]
    [InlineData(0, 0)]
    [InlineData(-1920, -1080)]
    [InlineData(2560, 1440)]
    public void CoordinateService_MathematicalConsistency_ClientToScreenAndBack(int clientX, int clientY)
    {
        using var form = new Form
        {
            Width = 400,
            Height = 400,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(100, 100)
        };
        form.Show();

        var service = new CoordinateService();
        var clientPt = new TargetPoint(form.Handle, clientX, clientY);

        Point screenPt = service.ClientToScreen(clientPt);
        TargetPoint backToClient = service.ScreenToClient(form.Handle, screenPt);

        Assert.Equal(clientX, backToClient.ClientX);
        Assert.Equal(clientY, backToClient.ClientY);

        form.Close();
    }
}
