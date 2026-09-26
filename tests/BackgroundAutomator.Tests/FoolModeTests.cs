using System.Diagnostics;
using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Profiles;
using BackgroundAutomator.Core.Security;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Core.TextDetection;
using Xunit;

namespace BackgroundAutomator.Tests;

public class FoolModeTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ProfileStorageService _storage;
    private readonly InMemoryLogger _logger;

    public FoolModeTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BA_FoolModeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _logger = new InMemoryLogger();
        _storage = new ProfileStorageService(_tempDirectory, _logger);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch { }
    }

    private class FakeForegroundService : IWindowForegroundService
    {
        public IntPtr CurrentForeground { get; set; } = (IntPtr)0x9999;
        public IntPtr LastRestoredHwnd { get; set; } = IntPtr.Zero;
        public int ActivateCallCount { get; private set; }
        public int RestoreCallCount { get; private set; }
        public bool ShouldFailActivation { get; set; }
        public bool IsMinimized { get; set; }
        public bool WindowClosed { get; set; }
        public int ProcessId { get; set; } = 1234;
        public string ProcessName { get; set; } = "WindowsTerminal";
        public string WindowClass { get; set; } = "CASCADIA_HOSTING_WINDOW_CLASS";
        public IntPtr RootHwndOverride { get; set; } = IntPtr.Zero;

        public IntPtr GetForegroundWindow() => CurrentForeground;

        public bool ActivateWindow(IntPtr hWnd)
        {
            ActivateCallCount++;
            if (ShouldFailActivation) return false;
            CurrentForeground = hWnd;
            return true;
        }

        public bool RestoreForegroundWindow(IntPtr hWnd)
        {
            RestoreCallCount++;
            LastRestoredHwnd = hWnd;
            CurrentForeground = hWnd;
            return true;
        }

        public bool IsWindowMinimized(IntPtr hWnd) => IsMinimized;
        public bool IsWindow(IntPtr hWnd) => !WindowClosed && hWnd != IntPtr.Zero;
        public bool IsWindowVisible(IntPtr hWnd) => true;
        public string GetProcessName(IntPtr hWnd) => ProcessName;
        public int GetProcessId(IntPtr hWnd) => ProcessId;
        public string GetWindowClass(IntPtr hWnd) => WindowClass;
        public IntPtr GetRootWindow(IntPtr hWnd) => RootHwndOverride != IntPtr.Zero ? RootHwndOverride : hWnd;
    }

    private class FakeForegroundKeyboard : IForegroundKeyboard
    {
        public int SendEnterCallCount { get; private set; }

        public bool SendEnter()
        {
            SendEnterCallCount++;
            return true;
        }
    }

    private class FakeElevationService : ProcessElevationService
    {
        public ElevationCompatibility DesiredCompatibility { get; set; } = ElevationCompatibility.Compatible;

        public override ElevationCheckResult CheckCompatibility(int targetProcessId)
        {
            return DesiredCompatibility == ElevationCompatibility.UipiMismatch
                ? ElevationCheckResult.CreateUipiMismatch()
                : ElevationCheckResult.CreateCompatible(false, false);
        }
    }

    private class FakeTextDetector : ITextDetectionService
    {
        private readonly Queue<TextDetectionResult> _results = new();
        public TextDetectionResult? FallbackResult { get; set; }

        public void EnqueueResult(string text, bool matched = true)
        {
            _results.Enqueue(TextDetectionResult.Success(text, text));
        }

        public void EnqueueNotFound()
        {
            _results.Enqueue(TextDetectionResult.NotFound());
        }

        public Task<TextDetectionResult> DetectAsync(IntPtr hWnd, TextDetectionRequest request, CancellationToken ct = default)
        {
            if (_results.Count > 0)
                return Task.FromResult(_results.Dequeue());

            if (FallbackResult != null)
                return Task.FromResult(FallbackResult);

            return Task.FromResult(TextDetectionResult.NotFound());
        }
    }

    private class FakeClicker : IBackgroundClicker
    {
        public ClickResult Click(TargetPoint target) => ClickResult.Success;
        public ClickResult DoubleClick(TargetPoint target) => ClickResult.Success;
    }

    private class FakeCapture : IWindowCaptureService
    {
        public WindowCapture? CaptureClientArea(IntPtr hWnd) => null;
        public Task<WindowCapture?> CaptureClientAreaAsync(IntPtr hWnd, CancellationToken ct = default) =>
            Task.FromResult<WindowCapture?>(null);
    }

    private class FakeKeyboard : IBackgroundKeyboard
    {
        public int KeyPressCount { get; private set; }
        public KeyPressResult PressKey(IntPtr hwnd, BackgroundKey key)
        {
            KeyPressCount++;
            return KeyPressResult.Success;
        }
        public KeyPressResult PressKey(IntPtr hwnd, uint virtualKey) => KeyPressResult.Success;
    }

    private const string ValidPromptViewport =
        "Requesting permission for:\r\n" +
        "dangerous-script.sh --force\r\n" +
        "\r\n" +
        "Run this command?\r\n" +
        "> 1. Yes, run command\r\n" +
        "  2. No\r\n";

    // 1. Existing profiles default to ExactRules.
    [Fact]
    public void Test1_ExistingProfiles_DefaultTo_ExactRules()
    {
        string legacyJson = @"{
          ""actionType"": ""SafeAutoConfirm"",
          ""ruleName"": ""Legacy Action"",
          ""expectedProcess"": ""WindowsTerminal.exe"",
          ""allowedCommand"": ""dotnet test""
        }";

        var config = System.Text.Json.JsonSerializer.Deserialize<MacroActionConfig>(legacyJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(config);
        Assert.Null(config.ApprovalPolicyMode);

        var action = Assert.IsType<SafeAutoConfirmAction>(config.ToMacroAction());
        Assert.Equal(ApprovalPolicyMode.ExactRules, action.PolicyMode);
    }

    // 2. FoolMode profile round-trip persists mode.
    [Fact]
    public void Test2_FoolModeProfile_RoundTrip_PersistsMode()
    {
        var rule = new CommandApprovalRule
        {
            Name = "Fool Mode Test",
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command"
        };

        var originalAction = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            focusBehavior: FocusBehavior.FastPulse,
            policyMode: ApprovalPolicyMode.FoolMode);

        var config = MacroActionConfig.FromMacroAction(originalAction);
        Assert.Equal("FoolMode", config.ApprovalPolicyMode);

        var restored = Assert.IsType<SafeAutoConfirmAction>(config.ToMacroAction());
        Assert.Equal(ApprovalPolicyMode.FoolMode, restored.PolicyMode);
        Assert.Contains("[FOOL MODE]", restored.DisplayString);
    }

    // 3. FoolMode skips ApprovalRuleSet evaluation.
    [Fact]
    public void Test3_FoolMode_Skips_ApprovalRuleSet_Evaluation()
    {
        var ruleSet = ApprovalRuleSet.FromSingleRule(new CommandApprovalRule
        {
            Name = "Strict Rule",
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command",
            AllowedCommand = "only-this-safe-command.exe",
            Enabled = true
        });

        var parser = new CommandPromptParser();
        var extraction = parser.Parse(ValidPromptViewport, "Run this command?", "Yes, run command");
        var snapshot = CommandPromptSnapshot.FromExtraction((IntPtr)0x1000, ValidPromptViewport, extraction);

        // ExactRules: should be BLOCKED because command doesn't match allowlist
        var exactDecision = CommandApprovalEvaluator.Evaluate(
            ruleSet, snapshot, "WindowsTerminal.exe", "CASCADIA", ApprovalPolicyMode.ExactRules);
        Assert.False(exactDecision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.CommandNotAllowed, exactDecision.BlockReason);

        // FoolMode: should be ALLOWED because allowlist check is bypassed
        var foolDecision = CommandApprovalEvaluator.Evaluate(
            ruleSet, snapshot, "WindowsTerminal.exe", "CASCADIA", ApprovalPolicyMode.FoolMode);
        Assert.True(foolDecision.IsAllowed);
        Assert.Equal("FOOL MODE", foolDecision.MatchedRuleName);
    }

    // 4. FoolMode still requires valid permission prompt.
    [Fact]
    public void Test4_FoolMode_StillRequires_ValidPermissionPrompt()
    {
        var ruleSet = ApprovalRuleSet.FromSingleRule(new CommandApprovalRule
        {
            Name = "Test",
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command"
        });

        // Visible text is just a shell directory listing without any prompt
        string nonPromptText = "Directory: D:\\project\r\nMode LastWriteTime Length Name\r\n-a--- 9/26/2026 1234 file.txt";
        var parser = new CommandPromptParser();
        var extraction = parser.Parse(nonPromptText, "Run this command?", "Yes, run command");
        var snapshot = CommandPromptSnapshot.FromExtraction((IntPtr)0x1000, nonPromptText, extraction);

        var decision = CommandApprovalEvaluator.Evaluate(
            ruleSet, snapshot, "WindowsTerminal.exe", "CASCADIA", ApprovalPolicyMode.FoolMode);

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.PromptNotVisible, decision.BlockReason);
    }

    // 5. FoolMode still requires "Yes, run command" selected.
    [Fact]
    public void Test5_FoolMode_StillRequires_YesOptionSelected()
    {
        var ruleSet = ApprovalRuleSet.FromSingleRule(new CommandApprovalRule
        {
            Name = "Test",
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command"
        });

        // Prompt exists, but option 2 "No" is selected
        string noSelectedViewport =
            "Requesting permission for:\r\n" +
            "dangerous-script.sh\r\n" +
            "\r\n" +
            "Run this command?\r\n" +
            "  1. Yes, run command\r\n" +
            "> 2. No\r\n";

        var parser = new CommandPromptParser();
        var extraction = parser.Parse(noSelectedViewport, "Run this command?", "Yes, run command");
        var snapshot = CommandPromptSnapshot.FromExtraction((IntPtr)0x1000, noSelectedViewport, extraction);

        var decision = CommandApprovalEvaluator.Evaluate(
            ruleSet, snapshot, "WindowsTerminal.exe", "CASCADIA", ApprovalPolicyMode.FoolMode);

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.OptionNotSelected, decision.BlockReason);
    }

    // 6. FoolMode does not approve unrelated text containing "Yes".
    [Fact]
    public void Test6_FoolMode_DoesNotApprove_UnrelatedTextContainingYes()
    {
        var ruleSet = ApprovalRuleSet.FromSingleRule(new CommandApprovalRule
        {
            Name = "Test",
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command"
        });

        // Unrelated document containing "Yes"
        string randomText = "Customer answered: Yes. Next question was skipped.\r\nDone.";
        var parser = new CommandPromptParser();
        var extraction = parser.Parse(randomText, "Run this command?", "Yes, run command");
        var snapshot = CommandPromptSnapshot.FromExtraction((IntPtr)0x1000, randomText, extraction);

        var decision = CommandApprovalEvaluator.Evaluate(
            ruleSet, snapshot, "WindowsTerminal.exe", "CASCADIA", ApprovalPolicyMode.FoolMode);

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.PromptNotVisible, decision.BlockReason);
    }

    // 7. FoolMode ObserveOnly never sends Enter.
    [Fact]
    public async Task Test7_FoolMode_ObserveOnly_NeverSendsEnter()
    {
        var fgService = new FakeForegroundService();
        var fgKeyboard = new FakeForegroundKeyboard();
        var detector = new FakeTextDetector();
        detector.EnqueueResult(ValidPromptViewport);

        var context = new MacroExecutionContext(
            new FakeClicker(),
            new FakeCapture(),
            _logger,
            (IntPtr)0x1000,
            new FakeKeyboard(),
            detector,
            fgKeyboard,
            fgService);

        var rule = new CommandApprovalRule
        {
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command"
        };

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.ObserveOnly,
            policyMode: ApprovalPolicyMode.FoolMode);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, fgKeyboard.SendEnterCallCount);
        Assert.Equal(0, fgService.ActivateCallCount);
        Assert.Contains("WOULD APPROVE", result.Message);
    }

    // 8. FoolMode Confirm sends maximum one Enter.
    [Fact]
    public async Task Test8_FoolMode_Confirm_SendsMaximumOneEnter()
    {
        var fgService = new FakeForegroundService();
        var fgKeyboard = new FakeForegroundKeyboard();
        var detector = new FakeTextDetector();

        // 1st detect: initial evaluation
        detector.EnqueueResult(ValidPromptViewport);
        // 2nd detect: revalidation after foreground activation
        detector.EnqueueResult(ValidPromptViewport);
        // 3rd detect: acknowledgement check (prompt dismissed)
        detector.EnqueueNotFound();

        var context = new MacroExecutionContext(
            new FakeClicker(),
            new FakeCapture(),
            _logger,
            (IntPtr)0x1000,
            new FakeKeyboard(),
            detector,
            fgKeyboard,
            fgService)
        {
            IsFoolModeAuthorized = true
        };

        var rule = new CommandApprovalRule
        {
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command"
        };

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            focusBehavior: FocusBehavior.FastPulse,
            policyMode: ApprovalPolicyMode.FoolMode);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, fgKeyboard.SendEnterCallCount); // MAX ONE ENTER
        Assert.Equal(1, fgService.ActivateCallCount);
        Assert.Equal(1, fgService.RestoreCallCount);
    }

    // 9. FoolMode retains target identity validation.
    [Fact]
    public void Test9_FoolMode_Retains_TargetIdentityValidation()
    {
        var ruleSet = ApprovalRuleSet.FromSingleRule(new CommandApprovalRule
        {
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command"
        });

        var parser = new CommandPromptParser();
        var extraction = parser.Parse(ValidPromptViewport, "Run this command?", "Yes, run command");
        var snapshot = CommandPromptSnapshot.FromExtraction((IntPtr)0x1000, ValidPromptViewport, extraction);

        // Process is notepad.exe instead of WindowsTerminal.exe
        var decision = CommandApprovalEvaluator.Evaluate(
            ruleSet, snapshot, "notepad.exe", "Notepad", ApprovalPolicyMode.FoolMode);

        Assert.False(decision.IsAllowed);
        Assert.Equal(ApprovalBlockReason.TargetMismatch, decision.BlockReason);
    }

    // 10. FoolMode retains UIPI validation.
    [Fact]
    public async Task Test10_FoolMode_Retains_UipiValidation()
    {
        var fgService = new FakeForegroundService();
        var fgKeyboard = new FakeForegroundKeyboard();
        var elevationService = new FakeElevationService
        {
            DesiredCompatibility = ElevationCompatibility.UipiMismatch
        };

        var context = new MacroExecutionContext(
            new FakeClicker(),
            new FakeCapture(),
            _logger,
            (IntPtr)0x1000,
            new FakeKeyboard(),
            new FakeTextDetector(),
            fgKeyboard,
            fgService,
            elevationService: elevationService)
        {
            IsFoolModeAuthorized = true
        };

        var rule = new CommandApprovalRule
        {
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command"
        };

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("UIPI mismatch", result.Message);
        Assert.Equal(0, fgKeyboard.SendEnterCallCount);
    }

    // 11. FoolMode retains foreground revalidation.
    [Fact]
    public async Task Test11_FoolMode_Retains_ForegroundRevalidation()
    {
        var fgService = new FakeForegroundService();
        var fgKeyboard = new FakeForegroundKeyboard();
        var detector = new FakeTextDetector();

        // 1st detect: prompt visible before pulse
        detector.EnqueueResult(ValidPromptViewport);
        // 2nd detect: prompt disappears during/after activation!
        detector.EnqueueNotFound();

        var context = new MacroExecutionContext(
            new FakeClicker(),
            new FakeCapture(),
            _logger,
            (IntPtr)0x1000,
            new FakeKeyboard(),
            detector,
            fgKeyboard,
            fgService)
        {
            IsFoolModeAuthorized = true
        };

        var rule = new CommandApprovalRule
        {
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command"
        };

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("RevalidationFailed", result.Message);
        Assert.Equal(0, fgKeyboard.SendEnterCallCount); // Never sent
    }

    // 12. FoolMode retains fingerprint acknowledgement.
    [Fact]
    public async Task Test12_FoolMode_Retains_FingerprintAcknowledgement()
    {
        var fgService = new FakeForegroundService();
        var fgKeyboard = new FakeForegroundKeyboard();
        var detector = new FakeTextDetector();

        // 1st detect: initial evaluation
        detector.EnqueueResult(ValidPromptViewport);
        // 2nd detect: revalidation
        detector.EnqueueResult(ValidPromptViewport);
        // Following detects: prompt STAYS VISIBLE indefinitely (never acknowledged)
        detector.FallbackResult = TextDetectionResult.Success(ValidPromptViewport, ValidPromptViewport);

        var context = new MacroExecutionContext(
            new FakeClicker(),
            new FakeCapture(),
            _logger,
            (IntPtr)0x1000,
            new FakeKeyboard(),
            detector,
            fgKeyboard,
            fgService)
        {
            IsFoolModeAuthorized = true
        };

        var rule = new CommandApprovalRule
        {
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command"
        };

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);

        var result = await action.ExecuteAsync(context, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MacroActionStatus.ApprovalBlocked, result.Status);
        Assert.Contains("ConfirmationNotAcknowledged", result.Message);
        Assert.Equal(1, fgKeyboard.SendEnterCallCount); // Sent once, but blocked when not acknowledged
    }

    // 20. ExactRules behavior remains unchanged.
    [Fact]
    public void Test20_ExactRules_Behavior_RemainsUnchanged()
    {
        var rule = new CommandApprovalRule
        {
            Name = "Strict Rule",
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command",
            AllowedCommand = "Get-Date",
            CommandMatchMode = CommandMatchMode.Exact,
            Enabled = true
        };

        var parser = new CommandPromptParser();

        // Case A: Matching command -> Allowed
        string matchingViewport =
            "Requesting permission for:\r\nGet-Date\r\n\r\nRun this command?\r\n> 1. Yes, run command\r\n  2. No\r\n";
        var extA = parser.Parse(matchingViewport, "Run this command?", "Yes, run command");
        var snapA = CommandPromptSnapshot.FromExtraction((IntPtr)0x1000, matchingViewport, extA);
        var decisionA = CommandApprovalEvaluator.Evaluate(rule, snapA, "WindowsTerminal.exe", "CASCADIA", ApprovalPolicyMode.ExactRules);
        Assert.True(decisionA.IsAllowed);

        // Case B: Non-matching command -> Blocked
        var extB = parser.Parse(ValidPromptViewport, "Run this command?", "Yes, run command");
        var snapB = CommandPromptSnapshot.FromExtraction((IntPtr)0x1000, ValidPromptViewport, extB);
        var decisionB = CommandApprovalEvaluator.Evaluate(rule, snapB, "WindowsTerminal.exe", "CASCADIA", ApprovalPolicyMode.ExactRules);
        Assert.False(decisionB.IsAllowed);
        Assert.Equal(ApprovalBlockReason.CommandNotAllowed, decisionB.BlockReason);
    }
}
