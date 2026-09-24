using System.Drawing;
using BackgroundAutomator.Core.Coordinates;
using BackgroundAutomator.Core.Targeting;
using Xunit;

namespace BackgroundAutomator.Tests;

public class TargetResolverTests
{
    private class FakeWindowTargetService : WindowTargetService
    {
        public List<WindowTargetCandidate> Candidates { get; set; } = new();

        public FakeWindowTargetService(CoordinateService coordService) : base(coordService) { }

        public override IReadOnlyList<WindowTargetCandidate> EnumerateTopLevelWindows(IntPtr ignoreRootHwnd = default)
        {
            return Candidates;
        }
    }

    [Fact]
    public void TargetDescriptor_DoesNotStoreLiveHwnd()
    {
        // Invariant: Stored identity must NEVER be a raw HWND handle
        var windowTarget = new WindowTarget
        {
            RootHwnd = (IntPtr)0x1234,
            TargetHwnd = (IntPtr)0x5678,
            ProcessName = "TestProcess.exe",
            WindowTitle = "Test Title",
            WindowClass = "TestClass",
            ClientPoint = new TargetPoint((IntPtr)0x5678, 100, 200)
        };

        var descriptor = TargetDescriptor.FromWindowTarget(windowTarget);

        // Verify descriptor properties
        Assert.Equal("TestProcess.exe", descriptor.ProcessName);
        Assert.Equal("Test Title", descriptor.WindowTitle);
        Assert.Equal("TestClass", descriptor.WindowClass);
        Assert.Equal(100, descriptor.SavedClientX);
        Assert.Equal(200, descriptor.SavedClientY);

        // Ensure TargetDescriptor type has no HWND property
        var properties = typeof(TargetDescriptor).GetProperties();
        Assert.DoesNotContain(properties, p => p.Name.Contains("Hwnd", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Resolve_SingleMatchingWindow_ReturnsSuccess()
    {
        using var ctrl = new System.Windows.Forms.Control();
        ctrl.CreateControl();
        IntPtr hwnd = ctrl.Handle;

        var coordService = new CoordinateService();
        var targetService = new FakeWindowTargetService(coordService)
        {
            Candidates = new List<WindowTargetCandidate>
            {
                new(hwnd, 1234, 5678, "app.exe", "Main Window Title", "AppWindowClass")
            }
        };

        var resolver = new TargetResolver(targetService, coordService);
        var descriptor = new TargetDescriptor
        {
            ProcessName = "app",
            WindowTitle = "Main Window",
            MatchMode = TitleMatchMode.Contains
        };

        var result = resolver.Resolve(descriptor);

        Assert.True(result.IsSuccess);
        Assert.Equal(TargetResolutionStatus.Success, result.Status);
        Assert.NotNull(result.Target);
        Assert.Equal(hwnd, result.Target.TargetHwnd);
        Assert.Equal("app.exe", result.Target.ProcessName);
    }

    [Fact]
    public void Resolve_ZeroMatches_ReturnsNotFound()
    {
        var coordService = new CoordinateService();
        var targetService = new FakeWindowTargetService(coordService)
        {
            Candidates = new List<WindowTargetCandidate>()
        };

        var resolver = new TargetResolver(targetService, coordService);
        var descriptor = new TargetDescriptor
        {
            ProcessName = "nonexistent.exe",
            WindowTitle = "Missing Window"
        };

        var result = resolver.Resolve(descriptor);

        Assert.False(result.IsSuccess);
        Assert.Equal(TargetResolutionStatus.NotFound, result.Status);
        Assert.Null(result.Target);
    }

    [Fact]
    public void Resolve_MultipleMatches_ReturnsAmbiguous()
    {
        var coordService = new CoordinateService();
        var targetService = new FakeWindowTargetService(coordService)
        {
            Candidates = new List<WindowTargetCandidate>
            {
                new((IntPtr)0x1111, 1001, 10, "notepad.exe", "Untitled - Notepad", "Notepad"),
                new((IntPtr)0x2222, 1002, 20, "notepad.exe", "Untitled - Notepad", "Notepad")
            }
        };

        var resolver = new TargetResolver(targetService, coordService);
        var descriptor = new TargetDescriptor
        {
            ProcessName = "notepad",
            WindowTitle = "Untitled - Notepad",
            MatchMode = TitleMatchMode.Exact
        };

        var result = resolver.Resolve(descriptor);

        Assert.False(result.IsSuccess);
        Assert.Equal(TargetResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.Target);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains(result.Candidates, c => c.Hwnd == (IntPtr)0x1111);
        Assert.Contains(result.Candidates, c => c.Hwnd == (IntPtr)0x2222);
    }

    [Theory]
    [InlineData("notepad", "notepad.exe", true)]
    [InlineData("notepad.exe", "notepad", true)]
    [InlineData("NOTEPAD.EXE", "notepad", true)]
    [InlineData("calc", "notepad.exe", false)]
    public void Resolve_ProcessNameNormalization(string descProc, string candProc, bool expectedMatch)
    {
        using var ctrl = new System.Windows.Forms.Control();
        ctrl.CreateControl();

        var coordService = new CoordinateService();
        var targetService = new FakeWindowTargetService(coordService)
        {
            Candidates = new List<WindowTargetCandidate>
            {
                new(ctrl.Handle, 100, 200, candProc, "My Title", "MyClass")
            }
        };

        var resolver = new TargetResolver(targetService, coordService);
        var descriptor = new TargetDescriptor
        {
            ProcessName = descProc,
            MatchMode = TitleMatchMode.Any
        };

        var result = resolver.Resolve(descriptor);

        if (expectedMatch)
        {
            Assert.Equal(TargetResolutionStatus.Success, result.Status);
        }
        else
        {
            Assert.Equal(TargetResolutionStatus.NotFound, result.Status);
        }
    }

    [Theory]
    [InlineData(TitleMatchMode.Exact, "My App Window", true)]
    [InlineData(TitleMatchMode.Exact, "My App", false)]
    [InlineData(TitleMatchMode.Contains, "App", true)]
    [InlineData(TitleMatchMode.Contains, "NotFound", false)]
    [InlineData(TitleMatchMode.StartsWith, "My App", true)]
    [InlineData(TitleMatchMode.StartsWith, "Window", false)]
    [InlineData(TitleMatchMode.Any, "Whatever", true)]
    public void Resolve_TitleMatchModes(TitleMatchMode mode, string queryTitle, bool expectedMatch)
    {
        using var ctrl = new System.Windows.Forms.Control();
        ctrl.CreateControl();

        var coordService = new CoordinateService();
        var targetService = new FakeWindowTargetService(coordService)
        {
            Candidates = new List<WindowTargetCandidate>
            {
                new(ctrl.Handle, 100, 200, "app.exe", "My App Window", "AppClass")
            }
        };

        var resolver = new TargetResolver(targetService, coordService);
        var descriptor = new TargetDescriptor
        {
            ProcessName = "app",
            WindowTitle = queryTitle,
            MatchMode = mode
        };

        var result = resolver.Resolve(descriptor);

        if (expectedMatch)
        {
            Assert.Equal(TargetResolutionStatus.Success, result.Status);
        }
        else
        {
            Assert.Equal(TargetResolutionStatus.NotFound, result.Status);
        }
    }

    [Fact]
    public void Resolve_EmptyDescriptor_ReturnsTargetInvalid()
    {
        var coordService = new CoordinateService();
        var targetService = new FakeWindowTargetService(coordService);
        var resolver = new TargetResolver(targetService, coordService);

        var descriptor = new TargetDescriptor(); // empty ProcessName and WindowTitle
        var result = resolver.Resolve(descriptor);

        Assert.False(result.IsSuccess);
        Assert.Equal(TargetResolutionStatus.TargetInvalid, result.Status);
    }

    [Fact]
    public void Resolve_ChildTarget_SingleMatch_ReturnsSuccess()
    {
        using var parent = new System.Windows.Forms.Panel();
        parent.CreateControl();
        using var child = new System.Windows.Forms.Button { Text = "UniqueButton" };
        parent.Controls.Add(child);
        child.CreateControl();

        var coordService = new CoordinateService();
        var targetService = new FakeWindowTargetService(coordService)
        {
            Candidates = new List<WindowTargetCandidate>
            {
                new(parent.Handle, 100, 200, "app.exe", "Parent Window", "PanelClass")
            }
        };

        var resolver = new TargetResolver(targetService, coordService);
        var descriptor = new TargetDescriptor
        {
            ProcessName = "app",
            WindowTitle = "Parent Window",
            ChildDescriptor = new ChildTargetDescriptor
            {
                ControlText = "UniqueButton"
            }
        };

        var result = resolver.Resolve(descriptor);

        Assert.True(result.IsSuccess);
        Assert.Equal(TargetResolutionStatus.Success, result.Status);
        Assert.NotNull(result.Target);
        Assert.Equal(child.Handle, result.Target.TargetHwnd);
        Assert.Equal(parent.Handle, result.Target.RootHwnd);
    }

    [Fact]
    public void Resolve_ChildTarget_MultipleMatches_ReturnsAmbiguous()
    {
        using var parent = new System.Windows.Forms.Panel();
        parent.CreateControl();
        using var child1 = new System.Windows.Forms.Button { Text = "DuplicateBtn" };
        using var child2 = new System.Windows.Forms.Button { Text = "DuplicateBtn" };
        parent.Controls.Add(child1);
        parent.Controls.Add(child2);
        child1.CreateControl();
        child2.CreateControl();

        var coordService = new CoordinateService();
        var targetService = new FakeWindowTargetService(coordService)
        {
            Candidates = new List<WindowTargetCandidate>
            {
                new(parent.Handle, 100, 200, "app.exe", "Parent Window", "PanelClass")
            }
        };

        var resolver = new TargetResolver(targetService, coordService);
        var descriptor = new TargetDescriptor
        {
            ProcessName = "app",
            WindowTitle = "Parent Window",
            ChildDescriptor = new ChildTargetDescriptor
            {
                ControlText = "DuplicateBtn"
            }
        };

        var result = resolver.Resolve(descriptor);

        Assert.False(result.IsSuccess);
        Assert.Equal(TargetResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.Target);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Contains(result.Candidates, c => c.Hwnd == child1.Handle);
        Assert.Contains(result.Candidates, c => c.Hwnd == child2.Handle);
    }

    [Fact]
    public void Resolve_ChildTarget_NotFound_ReturnsNotFound()
    {
        using var parent = new System.Windows.Forms.Panel();
        parent.CreateControl();
        using var child = new System.Windows.Forms.Button { Text = "ExistingButton" };
        parent.Controls.Add(child);
        child.CreateControl();

        var coordService = new CoordinateService();
        var targetService = new FakeWindowTargetService(coordService)
        {
            Candidates = new List<WindowTargetCandidate>
            {
                new(parent.Handle, 100, 200, "app.exe", "Parent Window", "PanelClass")
            }
        };

        var resolver = new TargetResolver(targetService, coordService);
        var descriptor = new TargetDescriptor
        {
            ProcessName = "app",
            WindowTitle = "Parent Window",
            ChildDescriptor = new ChildTargetDescriptor
            {
                ControlText = "NonExistentChild"
            }
        };

        var result = resolver.Resolve(descriptor);

        Assert.False(result.IsSuccess);
        Assert.Equal(TargetResolutionStatus.NotFound, result.Status);
        Assert.Null(result.Target);
    }
}
