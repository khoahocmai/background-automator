using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Win32;
using Xunit;

namespace BackgroundAutomator.Tests;

public class KeyboardIntegrationTests
{
    private record LogEntry(
        DateTime Timestamp,
        string MessageName,
        IntPtr Hwnd,
        string TargetName,
        string WParam,
        string LParam,
        string DecodedCoord,
        uint ThreadId);

    private static LogEntry ParseLogLine(string line)
    {
        string[] parts = line.Split('\t');
        return new LogEntry(
            DateTime.Parse(parts[0]),
            parts[1],
            new IntPtr(Convert.ToInt64(parts[2], 16)),
            parts[3],
            parts[4],
            parts[5],
            parts[6],
            uint.Parse(parts[7]));
    }

    [Fact]
    public async Task BackgroundKeyboard_Delivers_EnterKeyDownAndKeyUp_To_TestTarget()
    {
        using var fixture = new TestTargetFixture();
        Assert.True(User32.IsWindow(fixture.MainWindowHandle));
        Assert.True(fixture.KeyLoggerHwnd != IntPtr.Zero, "KeyLoggerHwnd was not resolved");

        var keyboard = new BackgroundKeyboardEngine();

        // Count initial log lines
        int initialCount = GetLogLines(fixture.LogFilePath).Length;

        // Dispatch PressKey Enter to the KeyLogger control
        var result = keyboard.PressKey(fixture.KeyLoggerHwnd, BackgroundKey.Enter);
        Assert.Equal(KeyPressResult.Success, result);

        // Wait for 2 messages: WM_KEYDOWN and WM_KEYUP
        string[] lines = await WaitForLogLinesCountAsync(fixture.LogFilePath, initialCount + 2, TimeSpan.FromSeconds(5));
        var newEntries = lines.Skip(initialCount).Take(2).Select(ParseLogLine).ToList();

        Assert.Equal(2, newEntries.Count);

        // 1. WM_KEYDOWN
        Assert.Contains("WM_KEYDOWN", newEntries[0].MessageName);
        Assert.Equal("0x000D", newEntries[0].WParam); // VK_RETURN
        Assert.Contains("Key: Enter", newEntries[0].DecodedCoord);

        // 2. WM_KEYUP
        Assert.Contains("WM_KEYUP", newEntries[1].MessageName);
        Assert.Equal("0x000D", newEntries[1].WParam);
        Assert.Contains("Key: Enter", newEntries[1].DecodedCoord);
    }

    [Fact]
    public async Task BackgroundKeyboard_Delivers_ExtendedArrowKey_With_ExtendedBit()
    {
        using var fixture = new TestTargetFixture();
        Assert.True(User32.IsWindow(fixture.MainWindowHandle));

        var keyboard = new BackgroundKeyboardEngine();

        int initialCount = GetLogLines(fixture.LogFilePath).Length;

        // Dispatch ArrowDown
        var result = keyboard.PressKey(fixture.KeyLoggerHwnd, BackgroundKey.ArrowDown);
        Assert.Equal(KeyPressResult.Success, result);

        string[] lines = await WaitForLogLinesCountAsync(fixture.LogFilePath, initialCount + 2, TimeSpan.FromSeconds(5));
        var newEntries = lines.Skip(initialCount).Take(2).Select(ParseLogLine).ToList();

        Assert.Equal(2, newEntries.Count);

        // WM_KEYDOWN for Down Arrow (VK_DOWN = 0x0028)
        Assert.Contains("WM_KEYDOWN", newEntries[0].MessageName);
        Assert.Equal("0x0028", newEntries[0].WParam);
        Assert.Contains("Key: Down", newEntries[0].DecodedCoord);

        // Verify extended key bit (bit 24) is set in lParam
        long downLParam = Convert.ToInt64(newEntries[0].LParam, 16);
        Assert.True(KeyboardMessageHelper.IsExtendedKeyLParam((IntPtr)downLParam));

        // WM_KEYUP for Down Arrow
        Assert.Contains("WM_KEYUP", newEntries[1].MessageName);
        Assert.Equal("0x0028", newEntries[1].WParam);

        long upLParam = Convert.ToInt64(newEntries[1].LParam, 16);
        Assert.True(KeyboardMessageHelper.IsExtendedKeyLParam((IntPtr)upLParam));
    }

    [Fact]
    public async Task MacroRunner_Executes_Click_Delay_PressKey_InOrder_Against_TestTarget()
    {
        using var fixture = new TestTargetFixture();
        Assert.True(User32.IsWindow(fixture.MainWindowHandle));

        var clicker = new BackgroundClickerEngine();
        var keyboard = new BackgroundKeyboardEngine();
        var captureService = new GdiWindowCaptureService();
        var logger = new InMemoryLogger();

        var context = new MacroExecutionContext(
            clicker,
            captureService,
            logger,
            targetHwnd: fixture.MainWindowHandle,
            keyboard: keyboard);

        var runner = new MacroRunner(logger);

        int initialCount = GetLogLines(fixture.LogFilePath).Length;

        var actions = new List<IMacroAction>
        {
            new ClickAction(10, 10, overrideHwnd: fixture.ButtonHwnd),
            new DelayAction(50),
            new PressKeyAction(BackgroundKey.Enter, overrideHwnd: fixture.KeyLoggerHwnd)
        };

        var executionResult = await runner.RunAsync(actions, context);

        Assert.True(executionResult.IsSuccess);
        Assert.Equal(3, executionResult.CompletedActionsCount);

        // Click posts 3 messages (MOVE, DOWN, UP) + PressKey posts 2 messages (KEYDOWN, KEYUP) = 5 messages
        string[] lines = await WaitForLogLinesCountAsync(fixture.LogFilePath, initialCount + 5, TimeSpan.FromSeconds(5));
        var entries = lines.Skip(initialCount).Take(5).Select(ParseLogLine).ToList();

        Assert.Equal(5, entries.Count);
        Assert.Contains("WM_MOUSEMOVE", entries[0].MessageName);
        Assert.Contains("WM_LBUTTONDOWN", entries[1].MessageName);
        Assert.Contains("WM_LBUTTONUP", entries[2].MessageName);
        Assert.Contains("WM_KEYDOWN", entries[3].MessageName);
        Assert.Contains("WM_KEYUP", entries[4].MessageName);
    }

    private static string[] GetLogLines(string logFilePath)
    {
        if (!File.Exists(logFilePath))
            return Array.Empty<string>();

        try
        {
            using var fs = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            var lines = new List<string>();
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add(line);
            }
            return lines.ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static async Task<string[]> WaitForLogLinesCountAsync(string logFilePath, int minCount, TimeSpan timeout)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            string[] lines = GetLogLines(logFilePath);
            if (lines.Length >= minCount)
                return lines;

            await Task.Delay(50);
        }

        return GetLogLines(logFilePath);
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public KeyboardIntegrationTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    [Trait("Category", "ManualDiagnostic")]
    public void RealWorld_Terminal_Keyboard_Diagnostics()
    {
        if (Environment.GetEnvironmentVariable("BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS") != "1")
        {
            _output.WriteLine("[OPT-IN] Skipped. Set BACKGROUNDAUTOMATOR_INTERACTIVE_TESTS=1 to run interactive terminal diagnostics.");
            return;
        }

        var thread = new Thread(() =>
        {
            IntPtr hDesk = User32.OpenDesktop("default", 0, false, 0x01FF);
            if (hDesk != IntPtr.Zero)
            {
                User32.SetThreadDesktop(hDesk);
            }

            IAppLogger logger = new InMemoryLogger();
            logger.MessageLogged += entry => _output.WriteLine(entry.ToString());
            var engine = new BackgroundKeyboardEngine(logger);

            IntPtr fg = User32.GetForegroundWindow();
            logger.Info($"Current Foreground HWND: 0x{fg.ToInt64():X8}");

            var terminalProcs = System.Diagnostics.Process.GetProcesses()
                .Where(p => p.ProcessName.Contains("Terminal", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var proc in terminalProcs)
            {
                logger.Info($"Terminal Process: {proc.ProcessName} (PID {proc.Id})");
            }

            // Enumerate windows looking for CASCADIA or terminal windows
            User32.EnumWindows((hWnd, lParam) =>
            {
                string cls = User32.GetClassNameSafe(hWnd);
                string title = User32.GetWindowTextSafe(hWnd);

                if (cls.Contains("CASCADIA", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("PowerShell", StringComparison.OrdinalIgnoreCase) ||
                    cls.Contains("Console", StringComparison.OrdinalIgnoreCase))
                {
                    User32.GetWindowThreadProcessId(hWnd, out uint pid);
                    bool isForeground = (User32.GetForegroundWindow() == hWnd);
                    logger.Info($"Found Terminal Window: HWND=0x{hWnd.ToInt64():X8}, PID={pid}, Class='{cls}', Title='{title}', Foreground={isForeground}");

                    // Test PressKey on this window
                    var res = engine.PressKey(hWnd, BackgroundKey.Enter);
                    logger.Info($"  PressKey(Enter) on Top HWND 0x{hWnd.ToInt64():X8}: Result={res}");

                    // Also enumerate and test child windows
                    User32.EnumChildWindows(hWnd, (hChild, lChild) =>
                    {
                        string childCls = User32.GetClassNameSafe(hChild);
                        string childTitle = User32.GetWindowTextSafe(hChild);
                        logger.Info($"    Child HWND=0x{hChild.ToInt64():X8}, Class='{childCls}', Title='{childTitle}'");

                        var childRes = engine.PressKey(hChild, BackgroundKey.Enter);
                        logger.Info($"    PressKey(Enter) on Child HWND 0x{hChild.ToInt64():X8}: Result={childRes}");
                        return true;
                    }, IntPtr.Zero);
                }
                return true;
            }, IntPtr.Zero);

            if (hDesk != IntPtr.Zero)
            {
                User32.CloseDesktop(hDesk);
            }
        });

        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        thread.Join();
    }
}
