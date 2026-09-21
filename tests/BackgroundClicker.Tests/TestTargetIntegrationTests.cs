using System.Diagnostics;
using System.Drawing;
using BackgroundClicker.Core.Coordinates;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;
using Xunit;

namespace BackgroundClicker.Tests;

public class TestTargetIntegrationTests : IDisposable
{
    private readonly Process? _testTargetProcess;
    private readonly WindowTargetService _targetService;
    private readonly CoordinateService _coordinateService;
    private readonly InMemoryLogger _logger;

    public TestTargetIntegrationTests()
    {
        // Ensure test process has PerMonitorV2 DPI awareness to match App and TestTarget
        User32.SetProcessDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        _logger = new InMemoryLogger();
        _coordinateService = new CoordinateService(_logger);
        _targetService = new WindowTargetService(_coordinateService, _logger);

        // Find path to built BackgroundClicker.TestTarget.exe
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // Locate TestTarget binary in Debug folder
        string testTargetExe = Path.GetFullPath(Path.Combine(
            baseDir,
            @"..\..\..\..\BackgroundClicker.TestTarget\bin\Debug\net8.0-windows\BackgroundClicker.TestTarget.exe"));

        if (!File.Exists(testTargetExe))
        {
            // Fallback to current directory search
            testTargetExe = Path.GetFullPath(Path.Combine(
                baseDir,
                "BackgroundClicker.TestTarget.exe"));
        }

        if (File.Exists(testTargetExe))
        {
            var psi = new ProcessStartInfo
            {
                FileName = testTargetExe,
                UseShellExecute = true
            };
            _testTargetProcess = Process.Start(psi);

            // Wait for window handle to initialize
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(100);
                _testTargetProcess?.Refresh();
                if (_testTargetProcess != null && _testTargetProcess.MainWindowHandle != IntPtr.Zero)
                {
                    break;
                }
            }
        }
    }

    [Fact]
    public void TestTarget_AppearsInEnumeration_AndResolvesPropertiesAccurately()
    {
        Assert.NotNull(_testTargetProcess);
        Assert.NotEqual(IntPtr.Zero, _testTargetProcess.MainWindowHandle);

        // 1. Verify TestTarget appears in window enumeration
        var candidates = _targetService.EnumerateTopLevelWindows();
        var targetCandidate = candidates.FirstOrDefault(c => c.ProcessId == _testTargetProcess.Id);

        Assert.NotNull(targetCandidate);
        Assert.Equal(_testTargetProcess.MainWindowHandle, targetCandidate.Hwnd);
        Assert.Contains("TestTarget", targetCandidate.ProcessName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("BackgroundClicker Test Target", targetCandidate.WindowTitle);

        // 2. Verify resolving from candidate
        var target = _targetService.ResolveTargetFromCandidate(targetCandidate);
        Assert.NotNull(target);
        Assert.Equal(_testTargetProcess.MainWindowHandle, target.RootHwnd);
        Assert.Equal(_testTargetProcess.MainWindowHandle, target.TargetHwnd);
        Assert.Equal(_testTargetProcess.Id, target.ProcessId);
        Assert.True(target.IsWindowValid());

        // 3. Verify screen to client resolution over the window
        User32.GetWindowRect(targetCandidate.Hwnd, out RECT winRect);
        Point screenPointInside = new(winRect.Left + 50, winRect.Top + 80);

        var resolvedChild = _targetService.ResolveTargetFromScreenPoint(screenPointInside);
        Assert.NotNull(resolvedChild);
        Assert.Equal(_testTargetProcess.MainWindowHandle, resolvedChild.RootHwnd);
        Assert.True(resolvedChild.IsWindowValid());

        // Client coordinates must be relative to TargetHwnd
        Assert.Equal(resolvedChild.TargetHwnd, resolvedChild.ClientPoint.Hwnd);

        // 4. Verify round trip
        bool roundTripOk = _coordinateService.VerifyRoundTrip(
            resolvedChild.TargetHwnd,
            screenPointInside,
            out var clientPt,
            out var roundTripScreen);

        Assert.True(roundTripOk);
        Assert.Equal(screenPointInside, roundTripScreen);
    }

    [Fact]
    public void TestTarget_WhenClosed_HandledGracefullyWithoutCrash()
    {
        Assert.NotNull(_testTargetProcess);
        Assert.NotEqual(IntPtr.Zero, _testTargetProcess.MainWindowHandle);

        IntPtr hwnd = _testTargetProcess.MainWindowHandle;
        var target = new WindowTarget
        {
            RootHwnd = hwnd,
            TargetHwnd = hwnd,
            ClientPoint = new TargetPoint(hwnd, 20, 20)
        };

        Assert.True(target.IsWindowValid());

        // Kill process
        _testTargetProcess.Kill();
        _testTargetProcess.WaitForExit(3000);

        // Verify target detects window is now closed
        Assert.False(target.IsWindowValid());

        // Verify RefreshTarget returns null and does not throw
        var refreshed = _targetService.RefreshTarget(target);
        Assert.Null(refreshed);

        // Verify window is no longer in enumeration
        var candidates = _targetService.EnumerateTopLevelWindows();
        Assert.DoesNotContain(candidates, c => c.Hwnd == hwnd);
    }

    public void Dispose()
    {
        try
        {
            if (_testTargetProcess != null && !_testTargetProcess.HasExited)
            {
                _testTargetProcess.Kill();
                _testTargetProcess.Dispose();
            }
        }
        catch
        {
            // Suppress cleanup error
        }
    }
}
