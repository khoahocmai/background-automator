using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Win32;
using Xunit;
using Xunit.Abstractions;

namespace BackgroundAutomator.Tests;

public class ResourceSoakTests
{
    private readonly ITestOutputHelper _output;

    public ResourceSoakTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void WindowCapture_SoakTest_ZeroGdiLeaksUnderHeavyWorkload()
    {
        using var form = new Form
        {
            Width = 200,
            Height = 200,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(50, 50)
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
            var color = cap.GetPixel(5, 5);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        uint finalGdi = User32.GetGuiResources(hCurrentProcess, User32.GR_GDIOBJECTS);
        uint finalUser = User32.GetGuiResources(hCurrentProcess, User32.GR_USEROBJECTS);

        int gdiDelta = (int)finalGdi - (int)initialGdi;
        int userDelta = (int)finalUser - (int)initialUser;

        Assert.True(gdiDelta <= 1, $"GDI objects leaked during 500 captures! Initial: {initialGdi}, Final: {finalGdi}, Delta: {gdiDelta}");
        Assert.True(userDelta <= 1, $"USER objects leaked during 500 captures! Initial: {initialUser}, Final: {finalUser}, Delta: {userDelta}");

        form.Close();
    }

    [Fact]
    public void WindowCapture_ExtendedSoak_5000Captures_MeasuresResourceStability()
    {
        using var form = new Form
        {
            Width = 250,
            Height = 250,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(100, 100)
        };
        form.Show();
        Application.DoEvents();

        using var captureService = new GdiWindowCaptureService();
        IntPtr hProcess = Kernel32.GetCurrentProcess();
        var proc = Process.GetCurrentProcess();

        // Warmup
        for (int i = 0; i < 20; i++)
        {
            using var warmup = captureService.CaptureClientArea(form.Handle);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        proc.Refresh();

        uint initGdi = User32.GetGuiResources(hProcess, User32.GR_GDIOBJECTS);
        uint initUser = User32.GetGuiResources(hProcess, User32.GR_USEROBJECTS);
        int initHandles = proc.HandleCount;
        long initGcMem = GC.GetTotalMemory(true);
        long initWs = proc.WorkingSet64;
        long initPrivate = proc.PrivateMemorySize64;

        var sw = Stopwatch.StartNew();
        const int iterations = 5000;
        for (int i = 0; i < iterations; i++)
        {
            if ((i & 127) == 0) Application.DoEvents();
            using var cap = captureService.CaptureClientArea(form.Handle);
            Assert.NotNull(cap);
            Color c = cap.GetPixel(10, 10);
        }
        sw.Stop();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        proc.Refresh();

        uint finalGdi = User32.GetGuiResources(hProcess, User32.GR_GDIOBJECTS);
        uint finalUser = User32.GetGuiResources(hProcess, User32.GR_USEROBJECTS);
        int finalHandles = proc.HandleCount;
        long finalGcMem = GC.GetTotalMemory(true);
        long finalWs = proc.WorkingSet64;
        long finalPrivate = proc.PrivateMemorySize64;

        int gdiDelta = (int)finalGdi - (int)initGdi;
        int userDelta = (int)finalUser - (int)initUser;
        int handleDelta = finalHandles - initHandles;
        long gcMemDelta = finalGcMem - initGcMem;
        long wsDelta = finalWs - initWs;
        long privateDelta = finalPrivate - initPrivate;

        _output.WriteLine($"=== WindowCapture 5000 Iterations Soak Report ===");
        _output.WriteLine($"Elapsed: {sw.ElapsedMilliseconds} ms ({iterations * 1000.0 / sw.ElapsedMilliseconds:F1} captures/sec)");
        _output.WriteLine($"GDI Handles:     Init={initGdi}, Final={finalGdi}, Delta={gdiDelta}");
        _output.WriteLine($"USER Handles:    Init={initUser}, Final={finalUser}, Delta={userDelta}");
        _output.WriteLine($"Process Handles: Init={initHandles}, Final={finalHandles}, Delta={handleDelta}");
        _output.WriteLine($"GC Memory:       Init={initGcMem / 1024.0:F1} KB, Final={finalGcMem / 1024.0:F1} KB, Delta={gcMemDelta / 1024.0:F1} KB");
        _output.WriteLine($"Working Set:     Init={initWs / 1024.0 / 1024.0:F2} MB, Final={finalWs / 1024.0 / 1024.0:F2} MB, Delta={wsDelta / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"Private Bytes:   Init={initPrivate / 1024.0 / 1024.0:F2} MB, Final={finalPrivate / 1024.0 / 1024.0:F2} MB, Delta={privateDelta / 1024.0 / 1024.0:F2} MB");

        Assert.True(gdiDelta <= 2, $"GDI leak detected! Delta={gdiDelta}");
        Assert.True(userDelta <= 2, $"USER leak detected! Delta={userDelta}");

        form.Close();
    }

    [Fact]
    public void BackgroundClick_ExtendedSoak_5000Clicks_MeasuresResourceStability()
    {
        using var form = new Form
        {
            Width = 200,
            Height = 200,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(150, 150)
        };
        form.Show();
        Application.DoEvents();

        var engine = new BackgroundClickerEngine(messagePacingMilliseconds: 0);
        IntPtr hProcess = Kernel32.GetCurrentProcess();
        var proc = Process.GetCurrentProcess();

        // Warmup
        for (int i = 0; i < 20; i++)
        {
            engine.Click(new TargetPoint(form.Handle, 10, 10));
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        proc.Refresh();

        uint initGdi = User32.GetGuiResources(hProcess, User32.GR_GDIOBJECTS);
        uint initUser = User32.GetGuiResources(hProcess, User32.GR_USEROBJECTS);
        int initHandles = proc.HandleCount;
        long initGcMem = GC.GetTotalMemory(true);
        long initWs = proc.WorkingSet64;
        long initPrivate = proc.PrivateMemorySize64;

        var sw = Stopwatch.StartNew();
        const int iterations = 5000;
        for (int i = 0; i < iterations; i++)
        {
            if ((i & 63) == 0) Application.DoEvents();
            var res = engine.Click(new TargetPoint(form.Handle, 10 + (i % 50), 10 + (i % 50)));
            Assert.Equal(ClickResult.Success, res);
        }
        sw.Stop();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        proc.Refresh();

        uint finalGdi = User32.GetGuiResources(hProcess, User32.GR_GDIOBJECTS);
        uint finalUser = User32.GetGuiResources(hProcess, User32.GR_USEROBJECTS);
        int finalHandles = proc.HandleCount;
        long finalGcMem = GC.GetTotalMemory(true);
        long finalWs = proc.WorkingSet64;
        long finalPrivate = proc.PrivateMemorySize64;

        int gdiDelta = (int)finalGdi - (int)initGdi;
        int userDelta = (int)finalUser - (int)initUser;
        int handleDelta = finalHandles - initHandles;
        long gcMemDelta = finalGcMem - initGcMem;
        long wsDelta = finalWs - initWs;
        long privateDelta = finalPrivate - initPrivate;

        _output.WriteLine($"=== BackgroundClick 5000 Clicks Soak Report ===");
        _output.WriteLine($"Elapsed: {sw.ElapsedMilliseconds} ms ({iterations * 1000.0 / sw.ElapsedMilliseconds:F1} clicks/sec)");
        _output.WriteLine($"GDI Handles:     Init={initGdi}, Final={finalGdi}, Delta={gdiDelta}");
        _output.WriteLine($"USER Handles:    Init={initUser}, Final={finalUser}, Delta={userDelta}");
        _output.WriteLine($"Process Handles: Init={initHandles}, Final={finalHandles}, Delta={handleDelta}");
        _output.WriteLine($"GC Memory:       Init={initGcMem / 1024.0:F1} KB, Final={finalGcMem / 1024.0:F1} KB, Delta={gcMemDelta / 1024.0:F1} KB");
        _output.WriteLine($"Working Set:     Init={initWs / 1024.0 / 1024.0:F2} MB, Final={finalWs / 1024.0 / 1024.0:F2} MB, Delta={wsDelta / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"Private Bytes:   Init={initPrivate / 1024.0 / 1024.0:F2} MB, Final={finalPrivate / 1024.0 / 1024.0:F2} MB, Delta={privateDelta / 1024.0 / 1024.0:F2} MB");

        Assert.True(gdiDelta <= 2, $"GDI leak detected! Delta={gdiDelta}");
        Assert.True(userDelta <= 2, $"USER leak detected! Delta={userDelta}");

        form.Close();
    }
}
