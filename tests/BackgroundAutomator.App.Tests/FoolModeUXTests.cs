using System;
using System.Collections.Generic;
using System.IO;
using BackgroundAutomator.App.Models;
using BackgroundAutomator.App.Services;
using BackgroundAutomator.App.Tests.Fakes;
using BackgroundAutomator.App.ViewModels;
using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Coordinates;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Profiles;
using BackgroundAutomator.Core.Security;
using BackgroundAutomator.Core.Targeting;
using Xunit;

namespace BackgroundAutomator.App.Tests;

public class FoolModeUXTests
{
    private sealed class TestableWindowTargetService : WindowTargetService
    {
        public List<WindowTargetCandidate> Candidates { get; set; } = new();

        public TestableWindowTargetService(CoordinateService coordService) : base(coordService) { }

        public override IReadOnlyList<WindowTargetCandidate> EnumerateTopLevelWindows(IntPtr ignoreRootHwnd = default) => Candidates;
    }

    private sealed class TestContext : IDisposable
    {
        public MainViewModel MainVm { get; }
        public FakeDialogService Dialogs { get; }
        public FakeTargetValidator TargetValidator { get; }
        public InMemoryLogger Logger { get; }
        public ProfileStorageService Storage { get; }
        public TestableWindowTargetService TargetService { get; }
        private readonly string _tempDirectory;

        public TestContext(string? tempDir = null)
        {
            _tempDirectory = tempDir ?? Path.Combine(Path.GetTempPath(), "BA_FoolModeUXTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
            Logger = new InMemoryLogger();
            Storage = new ProfileStorageService(Path.Combine(_tempDirectory, "profiles"), Logger);
            var coordService = new CoordinateService(Logger);
            TargetService = new TestableWindowTargetService(coordService);
            Dialogs = new FakeDialogService();
            TargetValidator = new FakeTargetValidator { IsValidResult = true };

            MainVm = new MainViewModel(
                Logger,
                Storage,
                TargetService,
                targetResolver: null,
                dialogService: Dialogs,
                targetValidator: TargetValidator);
        }

        public WindowTarget CreateDummyTarget(IntPtr hwnd = default)
        {
            if (hwnd == default) hwnd = (IntPtr)0x5555;
            return new WindowTarget
            {
                RootHwnd = hwnd,
                TargetHwnd = hwnd,
                ProcessName = "WindowsTerminal.exe",
                ProcessId = 1234,
                ThreadId = 5678,
                WindowTitle = "Terminal",
                WindowClass = "CASCADIA"
            };
        }

        public void Dispose()
        {
            try
            {
                MainVm.Cleanup();
            }
            catch { }

            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, true);
                }
            }
            catch { }
        }
    }

    // 13. Run Macro requires explicit warning confirmation.
    [Fact]
    public void Test13_RunMacro_Requires_ExplicitWarningConfirmation()
    {
        using var ctx = new TestContext();
        ctx.MainVm.TargetVM.CommitTarget(ctx.CreateDummyTarget());

        var foolAction = new SafeAutoConfirmAction(
            new CommandApprovalRule { Name = "Fool Action" },
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);
        ctx.MainVm.MacroVM.Actions.Add(new MacroActionItem(1, foolAction));

        ctx.Dialogs.ConfirmResult = true;

        ctx.MainVm.MacroVM.RunMacro();

        Assert.Equal(1, ctx.Dialogs.ConfirmCallCount);
        Assert.True(ctx.MainVm.MacroVM.IsFoolModeActive);
    }

    // 14. Cancelling warning keeps Runner IDLE.
    [Fact]
    public void Test14_CancellingWarning_KeepsRunnerIdle()
    {
        using var ctx = new TestContext();
        ctx.MainVm.TargetVM.CommitTarget(ctx.CreateDummyTarget());

        var foolAction = new SafeAutoConfirmAction(
            new CommandApprovalRule { Name = "Fool Action" },
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);
        ctx.MainVm.MacroVM.Actions.Add(new MacroActionItem(1, foolAction));

        ctx.Dialogs.ConfirmResult = false; // User clicks Cancel

        ctx.MainVm.MacroVM.RunMacro();

        Assert.Equal(1, ctx.Dialogs.ConfirmCallCount);
        Assert.False(ctx.MainVm.MacroVM.IsFoolModeActive);
        Assert.False(ctx.MainVm.MacroVM.IsRunning);
        Assert.Equal("Runner: IDLE", ctx.MainVm.StatusRunnerText);
    }

    // 15. F6 requires the exact same confirmation.
    [Fact]
    public void Test15_F6_Requires_ExactSameConfirmation()
    {
        using var ctx = new TestContext();
        ctx.MainVm.TargetVM.CommitTarget(ctx.CreateDummyTarget());
        ctx.MainVm.CurrentActivePageType = typeof(Views.MacroPage);

        var foolAction = new SafeAutoConfirmAction(
            new CommandApprovalRule { Name = "Fool Action" },
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);
        ctx.MainVm.MacroVM.Actions.Add(new MacroActionItem(1, foolAction));

        ctx.Dialogs.ConfirmResult = false; // User cancels F6 prompt

        ctx.MainVm.HandleF6();

        Assert.Equal(1, ctx.Dialogs.ConfirmCallCount);
        Assert.False(ctx.MainVm.MacroVM.IsFoolModeActive);
        Assert.Equal("Runner: IDLE", ctx.MainVm.StatusRunnerText);
    }

    // 16. F7 expires session authorization.
    [Fact]
    public void Test16_F7_Expires_SessionAuthorization()
    {
        using var ctx = new TestContext();
        ctx.MainVm.TargetVM.CommitTarget(ctx.CreateDummyTarget());

        var foolAction = new SafeAutoConfirmAction(
            new CommandApprovalRule { Name = "Fool Action" },
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);
        ctx.MainVm.MacroVM.Actions.Add(new MacroActionItem(1, foolAction));

        ctx.Dialogs.ConfirmResult = true;
        ctx.MainVm.MacroVM.RunMacro();

        Assert.True(ctx.MainVm.MacroVM.IsFoolModeActive);

        // Press F7
        ctx.MainVm.HandleF7();

        Assert.False(ctx.MainVm.MacroVM.IsFoolModeActive);
        Assert.Equal("Runner: IDLE", ctx.MainVm.StatusRunnerText);
    }

    // 17. Starting again after F7 asks again.
    [Fact]
    public void Test17_StartingAgain_AfterF7_PromptsAgain()
    {
        using var ctx = new TestContext();
        ctx.MainVm.TargetVM.CommitTarget(ctx.CreateDummyTarget());

        var foolAction = new SafeAutoConfirmAction(
            new CommandApprovalRule { Name = "Fool Action" },
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);
        ctx.MainVm.MacroVM.Actions.Add(new MacroActionItem(1, foolAction));

        ctx.Dialogs.ConfirmResult = true;

        // First run
        ctx.MainVm.MacroVM.RunMacro();
        Assert.Equal(1, ctx.Dialogs.ConfirmCallCount);

        // Emergency stop via F7
        ctx.MainVm.HandleF7();
        Assert.False(ctx.MainVm.MacroVM.IsFoolModeActive);

        // Start again: MUST ask again
        ctx.MainVm.MacroVM.RunMacro();
        Assert.Equal(2, ctx.Dialogs.ConfirmCallCount);
    }

    // 18. Multiple FoolMode actions produce only one start confirmation.
    [Fact]
    public void Test18_MultipleFoolModeActions_ProduceOnlyOneStartConfirmation()
    {
        using var ctx = new TestContext();
        ctx.MainVm.TargetVM.CommitTarget(ctx.CreateDummyTarget());

        var action1 = new SafeAutoConfirmAction(
            new CommandApprovalRule { Name = "Fool 1" },
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);
        var action2 = new DelayAction(100);
        var action3 = new SafeAutoConfirmAction(
            new CommandApprovalRule { Name = "Fool 2" },
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);

        ctx.MainVm.MacroVM.Actions.Add(new MacroActionItem(1, action1));
        ctx.MainVm.MacroVM.Actions.Add(new MacroActionItem(2, action2));
        ctx.MainVm.MacroVM.Actions.Add(new MacroActionItem(3, action3));

        ctx.Dialogs.ConfirmResult = true;

        ctx.MainVm.MacroVM.RunMacro();

        Assert.Equal(1, ctx.Dialogs.ConfirmCallCount); // EXACTLY ONCE
    }

    // 19. Startup profile containing FoolMode does NOT auto-run.
    [Fact]
    public void Test19_StartupProfile_ContainingFoolMode_DoesNotAutoRun()
    {
        using var ctx = new TestContext();

        var foolAction = new SafeAutoConfirmAction(
            new CommandApprovalRule { Name = "Fool Startup" },
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);

        var profile = new ProfileModel
        {
            Version = ProfileModel.CurrentSchemaVersion,
            Name = "FoolStartupProfile",
            Mode = ProfileMode.Macro,
            Target = new TargetDescriptor
            {
                ProcessName = "WindowsTerminal.exe",
                WindowTitle = "Terminal",
                MatchMode = TitleMatchMode.Contains
            },
            MacroActions = new List<MacroActionConfig>
            {
                MacroActionConfig.FromMacroAction(foolAction)
            },
            MacroSettings = new MacroRunnerSettingsConfig
            {
                RepeatMode = "UntilStopped",
                RepeatCount = 1,
                CycleDelayMilliseconds = 500
            }
        };

        ctx.Storage.SaveProfile(profile);
        ctx.Storage.SetLastUsedProfileName("FoolStartupProfile");

        // Act: App startup initialization
        ctx.MainVm.Initialize(IntPtr.Zero);

        // Assert: Restored into editor, but runner is strictly IDLE, no prompt was shown, no auto-run
        Assert.Equal(0, ctx.Dialogs.ConfirmCallCount);
        Assert.Equal(0, ctx.Dialogs.InfoCallCount);
        Assert.Equal("Runner: IDLE", ctx.MainVm.StatusRunnerText);
        Assert.False(ctx.MainVm.MacroVM.IsRunning);
        Assert.False(ctx.MainVm.MacroVM.IsFoolModeActive);
        Assert.Single(ctx.MainVm.MacroVM.Actions);
        var loadedAction = Assert.IsType<SafeAutoConfirmAction>(ctx.MainVm.MacroVM.Actions[0].Action);
        Assert.Equal(ApprovalPolicyMode.FoolMode, loadedAction.PolicyMode);
    }

    // 21. Invalid Target blocks execution, shows info dialog, does not request confirmation.
    [Fact]
    public void Test21_InvalidTarget_BlocksExecution_ShowsInfo_WithoutConfirmation()
    {
        using var ctx = new TestContext();
        ctx.TargetValidator.IsValidResult = false; // Target validation fails
        ctx.MainVm.TargetVM.CommitTarget(ctx.CreateDummyTarget());

        var foolAction = new SafeAutoConfirmAction(
            new CommandApprovalRule { Name = "Fool Action" },
            executionMode: AutoConfirmExecutionMode.Confirm,
            policyMode: ApprovalPolicyMode.FoolMode);
        ctx.MainVm.MacroVM.Actions.Add(new MacroActionItem(1, foolAction));

        ctx.MainVm.MacroVM.RunMacro();

        Assert.Equal(1, ctx.Dialogs.InfoCallCount);
        Assert.Equal(0, ctx.Dialogs.ConfirmCallCount);
        Assert.False(ctx.MainVm.MacroVM.IsFoolModeActive);
        Assert.False(ctx.MainVm.MacroVM.IsRunning);
        Assert.Equal("No Target Selected", ctx.Dialogs.LastTitle);
    }

    // 22. Empty actions list blocks execution, shows info dialog, does not request confirmation.
    [Fact]
    public void Test22_EmptyActions_BlocksExecution_ShowsInfo_WithoutConfirmation()
    {
        using var ctx = new TestContext();
        ctx.MainVm.TargetVM.CommitTarget(ctx.CreateDummyTarget());

        ctx.MainVm.MacroVM.RunMacro();

        Assert.Equal(1, ctx.Dialogs.InfoCallCount);
        Assert.Equal(0, ctx.Dialogs.ConfirmCallCount);
        Assert.False(ctx.MainVm.MacroVM.IsFoolModeActive);
        Assert.Equal("No Actions Configured", ctx.Dialogs.LastTitle);
    }

    // 23. Win32TargetValidator verifies target existence and rejects null or invalid HWND.
    [Fact]
    public void Test23_Win32TargetValidator_RejectsNullAndInvalidHwnd()
    {
        var validator = new Win32TargetValidator();

        Assert.False(validator.IsValid(null));

        var invalidHwndTarget = new WindowTarget
        {
            RootHwnd = unchecked((IntPtr)0x99999999),
            TargetHwnd = unchecked((IntPtr)0x99999999),
            ProcessName = "NonExistent"
        };
        Assert.False(validator.IsValid(invalidHwndTarget));
    }
}
