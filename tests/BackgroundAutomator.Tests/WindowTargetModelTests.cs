using System.Drawing;
using BackgroundAutomator.Core.Targeting;
using Xunit;

namespace BackgroundAutomator.Tests;

public class WindowTargetModelTests
{
    [Fact]
    public void WindowTarget_StoresAllRequiredProperties()
    {
        var rootHwnd = new IntPtr(0x1000);
        var targetHwnd = new IntPtr(0x2000);
        var parentHwnd = new IntPtr(0x1000);

        var target = new WindowTarget
        {
            RootHwnd = rootHwnd,
            TargetHwnd = targetHwnd,
            ParentHwnd = parentHwnd,
            ProcessId = 12345,
            ThreadId = 6789,
            ProcessName = "TestTarget.exe",
            WindowTitle = "BackgroundAutomator Test Target",
            WindowClass = "WindowsForms10.Window.8.app.0",
            ScreenPoint = new Point(1250, 640),
            ClientPoint = new TargetPoint(targetHwnd, 17, 14)
        };

        Assert.Equal(rootHwnd, target.RootHwnd);
        Assert.Equal(targetHwnd, target.TargetHwnd);
        Assert.Equal(parentHwnd, target.ParentHwnd);
        Assert.Equal(12345, target.ProcessId);
        Assert.Equal(6789u, target.ThreadId);
        Assert.Equal("TestTarget.exe", target.ProcessName);
        Assert.Equal("BackgroundAutomator Test Target", target.WindowTitle);
        Assert.Equal("WindowsForms10.Window.8.app.0", target.WindowClass);
        Assert.Equal(new Point(1250, 640), target.ScreenPoint);
        Assert.Equal(17, target.ClientPoint.ClientX);
        Assert.Equal(14, target.ClientPoint.ClientY);
        Assert.Equal(targetHwnd, target.ClientPoint.Hwnd);
    }

    [Fact]
    public void WindowTarget_DisplayText_IncludesProcessAndTitle()
    {
        var target = new WindowTarget
        {
            RootHwnd = new IntPtr(0x1203AA),
            ProcessName = "notepad.exe",
            WindowTitle = "Untitled - Notepad"
        };

        string display = target.DisplayText;
        Assert.Contains("notepad.exe", display);
        Assert.Contains("Untitled - Notepad", display);
        Assert.Contains("0x001203AA", display);
    }

    [Fact]
    public void WindowTarget_DisplayText_FallbackWhenTitleEmpty()
    {
        var target = new WindowTarget
        {
            RootHwnd = new IntPtr(0x5555),
            ProcessName = "TestApp.exe",
            WindowTitle = "",
            WindowClass = "CustomHostClass"
        };

        string display = target.DisplayText;
        Assert.Contains("TestApp.exe", display);
        Assert.Contains("[CustomHostClass]", display);
    }
}
