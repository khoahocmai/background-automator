using System.Diagnostics;
using System.Drawing;
using BackgroundClicker.Core.Clicking;
using BackgroundClicker.Core.Coordinates;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;
using Xunit;

namespace BackgroundClicker.Tests;

public class BackgroundClickIntegrationTests : IDisposable
{
    private readonly Process? _testTargetProcess;
    private readonly string _logFilePath;
    private readonly WindowTargetService _targetService;
    private readonly CoordinateService _coordinateService;
    private readonly InMemoryLogger _logger;
    private readonly BackgroundClickerEngine _clicker;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public BackgroundClickIntegrationTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        User32.SetProcessDpiAwarenessContext(NativeConstants.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        _logger = new InMemoryLogger();
        _coordinateService = new CoordinateService(_logger);
        _targetService = new WindowTargetService(_coordinateService, _logger);
        _clicker = new BackgroundClickerEngine(_logger);

        _logFilePath = Path.Combine(Path.GetTempPath(), $"testtarget_integration_{Guid.NewGuid():N}.tsv");

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string testTargetExe = Path.GetFullPath(Path.Combine(
            baseDir,
            @"..\..\..\..\BackgroundClicker.TestTarget\bin\Debug\net8.0-windows\BackgroundClicker.TestTarget.exe"));

        if (!File.Exists(testTargetExe))
        {
            testTargetExe = Path.GetFullPath(Path.Combine(baseDir, "BackgroundClicker.TestTarget.exe"));
        }

        if (File.Exists(testTargetExe))
        {
            var psi = new ProcessStartInfo
            {
                FileName = testTargetExe,
                Arguments = $"--log-file \"{_logFilePath}\" --log-mouse-move",
                UseShellExecute = true
            };
            _testTargetProcess = Process.Start(psi);

            for (int i = 0; i < 50; i++)
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
    public async Task BackgroundClick_SingleAndDoubleClick_VerifiedAgainstTestTargetObservedMessages()
    {
        Assert.NotNull(_testTargetProcess);
        Assert.NotEqual(IntPtr.Zero, _testTargetProcess.MainWindowHandle);

        IntPtr mainHwnd = _testTargetProcess.MainWindowHandle;

        // 1. Resolve Button HWND inside TestTarget
        IntPtr buttonHwnd = await ResolveButtonHwndAsync(mainHwnd);
        Assert.NotEqual(IntPtr.Zero, buttonHwnd);
        Assert.True(User32.IsWindow(buttonHwnd));

        // Initial log length
        int initialLineCount = GetLogLines().Length;

        // 2. Perform Single Click at specific client coordinate (42, 24)
        const int singleX = 42;
        const int singleY = 24;
        var singleTarget = new TargetPoint(buttonHwnd, singleX, singleY);

        ClickResult singleResult = _clicker.Click(singleTarget);
        Assert.Equal(ClickResult.Success, singleResult);

        // 3. Poll log file to verify TestTarget observed WM_MOUSEMOVE, WM_LBUTTONDOWN, WM_LBUTTONUP
        string[] singleLines = await WaitForLogLinesCountAsync(initialLineCount + 3, TimeSpan.FromSeconds(5));
        _output.WriteLine($"Initial line count: {initialLineCount}, SingleLines count: {singleLines.Length}");
        for (int i = 0; i < singleLines.Length; i++)
        {
            _output.WriteLine($"Line[{i}]: {singleLines[i]}");
        }
        var singleMessages = singleLines.Skip(initialLineCount).Take(3).Select(ParseLogLine).ToList();

        Assert.Equal(3, singleMessages.Count);
        Assert.Contains("WM_MOUSEMOVE", singleMessages[0].MessageName);
        Assert.Contains("WM_LBUTTONDOWN", singleMessages[1].MessageName);
        Assert.Contains("WM_LBUTTONUP", singleMessages[2].MessageName);

        // Verify decoded coordinates match exactly requested TargetPoint
        Assert.Equal($"({singleX}, {singleY})", singleMessages[0].DecodedCoord);
        Assert.Equal($"({singleX}, {singleY})", singleMessages[1].DecodedCoord);
        Assert.Equal($"({singleX}, {singleY})", singleMessages[2].DecodedCoord);

        // Verify button down wParam contains MK_LBUTTON
        Assert.Equal("0x0001", singleMessages[1].WParam);
        // Verify button up wParam contains 0
        Assert.Equal("0x0000", singleMessages[2].WParam);

        // Allow message pump to settle before dispatching double-click
        await Task.Delay(100);

        // 4. Perform Double Click at specific client coordinate (58, 19)
        int preDblCount = GetLogLines().Length;
        const int dblX = 58;
        const int dblY = 19;
        var dblTarget = new TargetPoint(buttonHwnd, dblX, dblY);

        ClickResult dblResult = _clicker.DoubleClick(dblTarget);
        Assert.Equal(ClickResult.Success, dblResult);

        // Expected 5 messages: WM_MOUSEMOVE, WM_LBUTTONDOWN, WM_LBUTTONUP, WM_LBUTTONDBLCLK, WM_LBUTTONUP
        string[] dblLines = await WaitForLogLinesCountAsync(preDblCount + 5, TimeSpan.FromSeconds(5));
        _output.WriteLine($"PreDbl line count: {preDblCount}, DblLines count: {dblLines.Length}");
        for (int i = preDblCount; i < dblLines.Length; i++)
        {
            _output.WriteLine($"DblLine[{i}]: {dblLines[i]}");
        }
        var dblMessages = dblLines.Skip(preDblCount).Take(5).Select(ParseLogLine).ToList();

        Assert.Equal(5, dblMessages.Count);
        Assert.Contains("WM_MOUSEMOVE", dblMessages[0].MessageName);
        Assert.Contains("WM_LBUTTONDOWN", dblMessages[1].MessageName);
        Assert.Contains("WM_LBUTTONUP", dblMessages[2].MessageName);
        Assert.Contains("WM_LBUTTONDBLCLK", dblMessages[3].MessageName);
        Assert.Contains("WM_LBUTTONUP", dblMessages[4].MessageName);

        // Decoded coordinates must match dblTarget
        Assert.Equal($"({dblX}, {dblY})", dblMessages[0].DecodedCoord);
        Assert.Equal($"({dblX}, {dblY})", dblMessages[1].DecodedCoord);
        Assert.Equal($"({dblX}, {dblY})", dblMessages[2].DecodedCoord);
        Assert.Equal($"({dblX}, {dblY})", dblMessages[3].DecodedCoord);
        Assert.Equal($"({dblX}, {dblY})", dblMessages[4].DecodedCoord);

        // Down and DblClk must have MK_LBUTTON (0x0001)
        Assert.Equal("0x0001", dblMessages[1].WParam);
        Assert.Equal("0x0001", dblMessages[3].WParam);
        // Up must have 0
        Assert.Equal("0x0000", dblMessages[2].WParam);
        Assert.Equal("0x0000", dblMessages[4].WParam);

        // 5. Terminate TestTarget process
        _testTargetProcess.Kill();
        _testTargetProcess.WaitForExit(3000);

        // 6. Verify subsequent click returns InvalidTarget safely
        ClickResult postDeathResult = _clicker.Click(new TargetPoint(buttonHwnd, 10, 10));
        Assert.Equal(ClickResult.InvalidTarget, postDeathResult);
    }

    [Fact]
    public async Task BackgroundClick_PacingBenchmark_0msVs1msVs10ms()
    {
        Assert.NotNull(_testTargetProcess);
        Assert.NotEqual(IntPtr.Zero, _testTargetProcess.MainWindowHandle);

        IntPtr mainHwnd = _testTargetProcess.MainWindowHandle;
        IntPtr buttonHwnd = await ResolveButtonHwndAsync(mainHwnd);
        Assert.NotEqual(IntPtr.Zero, buttonHwnd);

        // Benchmark 1: 0ms pacing under load with 100 clicks (= 300 messages)
        {
            var clicker0ms = new BackgroundClickerEngine(_logger, messagePacingMilliseconds: 0);
            int startCount = GetLogLines().Length;
            var sw = Stopwatch.StartNew();
            const int clickCount = 100;
            for (int i = 0; i < clickCount; i++)
            {
                var target = new TargetPoint(buttonHwnd, 10 + (i % 40), 10 + (i % 20));
                ClickResult res = clicker0ms.Click(target);
                Assert.Equal(ClickResult.Success, res);
            }
            sw.Stop();

            int expected = clickCount * 3;
            string[] lines = await WaitForLogLinesCountAsync(startCount + expected, TimeSpan.FromSeconds(10));
            int received = lines.Length - startCount;
            _output.WriteLine($"[Benchmark 0ms] 100 clicks: Elapsed={sw.ElapsedMilliseconds}ms, Received={received}/{expected} messages");
            Assert.True(received >= expected, $"Pacing 0ms expected at least {expected} messages, received {received}");
        }

        // Benchmark 2: 1ms pacing with 20 clicks (= 60 messages)
        {
            var clicker1ms = new BackgroundClickerEngine(_logger, messagePacingMilliseconds: 1);
            int startCount = GetLogLines().Length;
            var sw = Stopwatch.StartNew();
            const int clickCount = 20;
            for (int i = 0; i < clickCount; i++)
            {
                var target = new TargetPoint(buttonHwnd, 15, 15);
                ClickResult res = clicker1ms.Click(target);
                Assert.Equal(ClickResult.Success, res);
            }
            sw.Stop();

            int expected = clickCount * 3;
            string[] lines = await WaitForLogLinesCountAsync(startCount + expected, TimeSpan.FromSeconds(5));
            int received = lines.Length - startCount;
            _output.WriteLine($"[Benchmark 1ms] 20 clicks: Elapsed={sw.ElapsedMilliseconds}ms, Received={received}/{expected} messages");
            Assert.True(received >= expected, $"Pacing 1ms expected at least {expected} messages, received {received}");
        }

        // Benchmark 3: 10ms pacing with 10 clicks (= 30 messages)
        {
            var clicker10ms = new BackgroundClickerEngine(_logger, messagePacingMilliseconds: 10);
            int startCount = GetLogLines().Length;
            var sw = Stopwatch.StartNew();
            const int clickCount = 10;
            for (int i = 0; i < clickCount; i++)
            {
                var target = new TargetPoint(buttonHwnd, 20, 20);
                ClickResult res = clicker10ms.Click(target);
                Assert.Equal(ClickResult.Success, res);
            }
            sw.Stop();

            int expected = clickCount * 3;
            string[] lines = await WaitForLogLinesCountAsync(startCount + expected, TimeSpan.FromSeconds(5));
            int received = lines.Length - startCount;
            _output.WriteLine($"[Benchmark 10ms] 10 clicks: Elapsed={sw.ElapsedMilliseconds}ms, Received={received}/{expected} messages");
            Assert.True(received >= expected, $"Pacing 10ms expected at least {expected} messages, received {received}");
        }
    }

    private static async Task<IntPtr> ResolveButtonHwndAsync(IntPtr mainHwnd)
    {
        IntPtr buttonHwnd = IntPtr.Zero;
        for (int retry = 0; retry < 20; retry++)
        {
            User32.EnumChildWindows(mainHwnd, (hwnd, lParam) =>
            {
                string text = User32.GetWindowTextSafe(hwnd);
                if (text.Contains("Target Button", StringComparison.OrdinalIgnoreCase))
                {
                    buttonHwnd = hwnd;
                    return false; // Stop enumeration
                }
                return true;
            }, IntPtr.Zero);

            if (buttonHwnd != IntPtr.Zero)
                break;

            await Task.Delay(100);
        }
        return buttonHwnd;
    }

    private string[] GetLogLines()
    {
        if (!File.Exists(_logFilePath))
            return Array.Empty<string>();

        try
        {
            using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            var list = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    list.Add(line);
                }
            }
            return list.ToArray();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    private async Task<string[]> WaitForLogLinesCountAsync(int expectedMinCount, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            string[] lines = GetLogLines();
            if (lines.Length >= expectedMinCount)
            {
                return lines;
            }
            await Task.Delay(50);
        }
        return GetLogLines();
    }

    private static (string MessageName, string WParam, string LParam, string DecodedCoord) ParseLogLine(string line)
    {
        var parts = line.Split('\t');
        if (parts.Length >= 7)
        {
            return (parts[1], parts[4], parts[5], parts[6]);
        }
        return (string.Empty, string.Empty, string.Empty, string.Empty);
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
        }

        try
        {
            if (File.Exists(_logFilePath))
            {
                File.Delete(_logFilePath);
            }
        }
        catch
        {
        }
    }
}
