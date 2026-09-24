using System.Drawing;
using System.Windows.Forms;
using BackgroundAutomator.Core.Coordinates;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Targeting;
using Xunit;

namespace BackgroundAutomator.Tests;

public class CoordinateServiceTests
{
    private readonly CoordinateService _coordinateService;
    private readonly InMemoryLogger _logger;

    public CoordinateServiceTests()
    {
        _logger = new InMemoryLogger();
        _coordinateService = new CoordinateService(_logger);
    }

    [Fact]
    public void ScreenToClient_WithInvalidHwnd_ReturnsZeroAndLogsWarning()
    {
        var result = _coordinateService.ScreenToClient(IntPtr.Zero, new Point(100, 200));

        Assert.Equal(IntPtr.Zero, result.Hwnd);
        Assert.Equal(0, result.ClientX);
        Assert.Equal(0, result.ClientY);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void ClientToScreen_WithInvalidHwnd_ReturnsEmptyPoint()
    {
        var result = _coordinateService.ClientToScreen(new TargetPoint(IntPtr.Zero, 50, 60));

        Assert.Equal(Point.Empty, result);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void CoordinateRoundTrip_OnRealWindow_SucceedsPrecisely()
    {
        // Run test with a real in-memory WinForms window
        using var form = new Form();
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(250, 250);
        form.Size = new Size(400, 300);
        _ = form.Handle; // Ensure HWND created

        using var button = new Button();
        button.Location = new Point(30, 40);
        button.Size = new Size(120, 35);
        form.Controls.Add(button);
        _ = button.Handle; // Ensure child HWND created

        // Pick an arbitrary client point on the button
        var initialClientPt = new TargetPoint(button.Handle, 15, 12);

        // Client -> Screen
        Point screenPt = _coordinateService.ClientToScreen(initialClientPt);
        Assert.True(screenPt.X > 0 && screenPt.Y > 0);

        // Screen -> Client
        TargetPoint roundTripClientPt = _coordinateService.ScreenToClient(button.Handle, screenPt);

        // Assert exact round-trip match
        Assert.Equal(initialClientPt.ClientX, roundTripClientPt.ClientX);
        Assert.Equal(initialClientPt.ClientY, roundTripClientPt.ClientY);
        Assert.Equal(initialClientPt.Hwnd, roundTripClientPt.Hwnd);

        // Verify roundtrip helper
        bool verified = _coordinateService.VerifyRoundTrip(button.Handle, screenPt, out var clientPt, out var roundTripScreen);
        Assert.True(verified);
        Assert.Equal(screenPt, roundTripScreen);
        Assert.Equal(initialClientPt, clientPt);
    }

    [Fact]
    public void WindowMove_RefreshesScreenCoordinates_WhileClientCoordinatesRemainStable()
    {
        using var form = new Form();
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(100, 100);
        form.Size = new Size(300, 200);
        _ = form.Handle;

        using var button = new Button();
        button.Location = new Point(20, 20);
        form.Controls.Add(button);
        _ = button.Handle;

        var clientPt = new TargetPoint(button.Handle, 10, 10);
        Point initialScreen = _coordinateService.ClientToScreen(clientPt);

        // Move form by 150px right, 100px down
        form.Location = new Point(250, 200);

        Point movedScreen = _coordinateService.ClientToScreen(clientPt);

        // Screen coordinate should have shifted by delta (150, 100)
        Assert.Equal(initialScreen.X + 150, movedScreen.X);
        Assert.Equal(initialScreen.Y + 100, movedScreen.Y);

        // And converting back from movedScreen returns the exact original client point (10, 10)
        TargetPoint backToClient = _coordinateService.ScreenToClient(button.Handle, movedScreen);
        Assert.Equal(clientPt.ClientX, backToClient.ClientX);
        Assert.Equal(clientPt.ClientY, backToClient.ClientY);
    }
}
