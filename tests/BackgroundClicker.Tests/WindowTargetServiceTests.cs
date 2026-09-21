using System.Drawing;
using System.Windows.Forms;
using BackgroundClicker.Core.Coordinates;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;
using Xunit;

namespace BackgroundClicker.Tests;

public class WindowTargetServiceTests
{
    private readonly CoordinateService _coordinateService;
    private readonly WindowTargetService _targetService;
    private readonly InMemoryLogger _logger;

    public WindowTargetServiceTests()
    {
        User32.SetProcessDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        _logger = new InMemoryLogger();
        _coordinateService = new CoordinateService(_logger);
        _targetService = new WindowTargetService(_coordinateService, _logger);
    }

    [Fact]
    public void EnumerateTopLevelWindows_ReturnsWindows_AndExcludesCurrentProcess()
    {
        var candidates = _targetService.EnumerateTopLevelWindows();

        Assert.NotNull(candidates);
        foreach (var c in candidates)
        {
            Assert.NotEqual(IntPtr.Zero, c.Hwnd);
            Assert.False(string.IsNullOrWhiteSpace(c.ProcessName));
        }

        int currentPid = Environment.ProcessId;
        Assert.DoesNotContain(candidates, c => c.ProcessId == currentPid);
    }

    [Fact]
    public void ResolveTargetFromScreenPoint_Form_Panel_Button_ResolvesAppropriateLevels()
    {
        // Setup Form -> Panel -> Button hierarchy
        using var form = new Form
        {
            Text = "Hierarchy Test Form",
            StartPosition = FormStartPosition.Manual,
            Location = new Point(100, 100),
            Size = new Size(500, 400),
            ShowInTaskbar = false
        };

        using var panel = new Panel
        {
            Location = new Point(40, 40),
            Size = new Size(300, 200),
            BorderStyle = BorderStyle.FixedSingle
        };

        using var button = new Button
        {
            Text = "Target Button",
            Location = new Point(30, 30),
            Size = new Size(120, 50)
        };

        panel.Controls.Add(button);
        form.Controls.Add(panel);

        form.Show();
        Application.DoEvents();

        // Level 1: Hover over Button
        Point buttonScreenPt = _coordinateService.ClientToScreen(new TargetPoint(button.Handle, 20, 20));
        var targetOnButton = _targetService.ResolveTargetFromScreenPoint(buttonScreenPt, allowCurrentProcess: true);

        Assert.NotNull(targetOnButton);
        Assert.Equal(form.Handle, targetOnButton.RootHwnd);
        Assert.Equal(button.Handle, targetOnButton.TargetHwnd);
        Assert.Equal(panel.Handle, targetOnButton.ParentHwnd);
        Assert.Equal(20, targetOnButton.ClientPoint.ClientX);
        Assert.Equal(20, targetOnButton.ClientPoint.ClientY);

        // Level 2: Hover over Panel (outside Button, e.g. at panel local 200, 100)
        Point panelScreenPt = _coordinateService.ClientToScreen(new TargetPoint(panel.Handle, 200, 100));
        var targetOnPanel = _targetService.ResolveTargetFromScreenPoint(panelScreenPt, allowCurrentProcess: true);

        Assert.NotNull(targetOnPanel);
        Assert.Equal(form.Handle, targetOnPanel.RootHwnd);
        Assert.Equal(panel.Handle, targetOnPanel.TargetHwnd);
        Assert.Equal(form.Handle, targetOnPanel.ParentHwnd);
        Assert.Equal(200, targetOnPanel.ClientPoint.ClientX);
        Assert.Equal(100, targetOnPanel.ClientPoint.ClientY);

        // Level 3: Hover over Form directly (outside Panel, e.g. at form local 380, 20)
        Point formScreenPt = _coordinateService.ClientToScreen(new TargetPoint(form.Handle, 380, 20));
        var targetOnForm = _targetService.ResolveTargetFromScreenPoint(formScreenPt, allowCurrentProcess: true);

        Assert.NotNull(targetOnForm);
        Assert.Equal(form.Handle, targetOnForm.RootHwnd);
        Assert.Equal(form.Handle, targetOnForm.TargetHwnd);
        Assert.Equal(IntPtr.Zero, targetOnForm.ParentHwnd);

        form.Close();
    }

    [Fact]
    public void RefreshTarget_WhenWindowMoved_UpdatesScreenCoordinatesAccurately()
    {
        using var form = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(150, 150),
            Size = new Size(350, 250),
            ShowInTaskbar = false
        };

        using var button = new Button
        {
            Location = new Point(25, 25),
            Size = new Size(100, 40)
        };
        form.Controls.Add(button);
        form.Show();
        Application.DoEvents();

        // Initial target selection on button at (15, 15)
        Point initialScreen = _coordinateService.ClientToScreen(new TargetPoint(button.Handle, 15, 15));
        var target = _targetService.ResolveTargetFromScreenPoint(initialScreen, allowCurrentProcess: true);
        Assert.NotNull(target);
        Assert.Equal(15, target.ClientPoint.ClientX);
        Assert.Equal(15, target.ClientPoint.ClientY);

        // Move window by (100, 80)
        form.Location = new Point(250, 230);
        Application.DoEvents();

        // Refresh coordinates
        var refreshed = _targetService.RefreshTarget(target);
        Assert.NotNull(refreshed);

        // Client coordinate must remain unchanged at (15, 15) relative to button
        Assert.Equal(15, refreshed.ClientPoint.ClientX);
        Assert.Equal(15, refreshed.ClientPoint.ClientY);
        Assert.Equal(button.Handle, refreshed.ClientPoint.Hwnd);

        // Screen coordinate must shift by the move delta
        Assert.Equal(initialScreen.X + 100, refreshed.ScreenPoint.X);
        Assert.Equal(initialScreen.Y + 80, refreshed.ScreenPoint.Y);

        form.Close();
    }

    [Fact]
    public void RefreshTarget_WhenWindowResized_PreservesClientCoordinates()
    {
        using var form = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(150, 150),
            Size = new Size(300, 200),
            ShowInTaskbar = false
        };

        using var button = new Button
        {
            Location = new Point(25, 25),
            Size = new Size(100, 40)
        };
        form.Controls.Add(button);
        form.Show();
        Application.DoEvents();

        Point screenPt = _coordinateService.ClientToScreen(new TargetPoint(button.Handle, 12, 14));
        var target = _targetService.ResolveTargetFromScreenPoint(screenPt, allowCurrentProcess: true);
        Assert.NotNull(target);

        // Resize form
        form.Size = new Size(500, 400);
        Application.DoEvents();

        var refreshed = _targetService.RefreshTarget(target);
        Assert.NotNull(refreshed);
        Assert.Equal(12, refreshed.ClientPoint.ClientX);
        Assert.Equal(14, refreshed.ClientPoint.ClientY);

        form.Close();
    }

    [Fact]
    public void RefreshTarget_WhenTargetClosed_ReturnsNullGracefully()
    {
        var target = new WindowTarget
        {
            RootHwnd = new IntPtr(0xABCDEF),
            TargetHwnd = new IntPtr(0xABCDEF),
            ClientPoint = new TargetPoint(new IntPtr(0xABCDEF), 10, 10)
        };

        var refreshed = _targetService.RefreshTarget(target);
        Assert.Null(refreshed);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }
}
