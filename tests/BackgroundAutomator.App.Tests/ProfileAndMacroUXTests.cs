using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BackgroundAutomator.App.Models;
using BackgroundAutomator.App.ViewModels;
using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Coordinates;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Profiles;
using BackgroundAutomator.Core.Targeting;
using Xunit;

namespace BackgroundAutomator.App.Tests;

public class ProfileAndMacroUXTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ProfileStorageService _storage;
    private readonly InMemoryLogger _logger;

    public ProfileAndMacroUXTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BA_UXTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _storage = new ProfileStorageService(_tempDirectory);
        _logger = new InMemoryLogger();
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

    private class NullClicker : IBackgroundClicker
    {
        public ClickResult Click(TargetPoint target) => ClickResult.Success;
        public ClickResult DoubleClick(TargetPoint target) => ClickResult.Success;
    }

    private class NullCaptureService : IWindowCaptureService
    {
        public WindowCapture? CaptureClientArea(IntPtr hWnd) => null;
        public Task<WindowCapture?> CaptureClientAreaAsync(IntPtr hWnd, CancellationToken ct = default) =>
            Task.FromResult<WindowCapture?>(null);
    }

    private class NullKeyboard : IBackgroundKeyboard
    {
        public KeyPressResult PressKey(IntPtr hwnd, BackgroundKey key) => KeyPressResult.Success;
        public KeyPressResult PressKey(IntPtr hwnd, uint virtualKey) => KeyPressResult.Success;
    }

    private MacroViewModel CreateMacroViewModel()
    {
        var coordService = new CoordinateService();
        var captureService = new GdiWindowCaptureService(_logger);
        var clicker = new BackgroundClickerEngine(_logger);
        var runner = new MacroRunner(_logger);
        return new MacroViewModel(runner, clicker, captureService, new NullKeyboard(), _logger, (state, msg) => { });
    }

    [Fact]
    public void MacroActionItem_UpdateAction_Updates_Properties_In_Place()
    {
        var rule1 = new CommandApprovalRule { Name = "Rule 1", AllowedCommand = "Get-Date" };
        var action1 = new SafeAutoConfirmAction(rule1, executionMode: AutoConfirmExecutionMode.Confirm, focusBehavior: FocusBehavior.FastPulse);
        var item = new MacroActionItem(1, action1);

        Assert.Equal(1, item.Index);
        Assert.Equal("Safe Auto Confirm", item.Name);
        Assert.Contains("Get-Date", item.Details);

        var rule2 = new CommandApprovalRule { Name = "Rule 2", AllowedCommand = "dotnet test BackgroundAutomator.sln" };
        var action2 = new SafeAutoConfirmAction(rule2, executionMode: AutoConfirmExecutionMode.Confirm, focusBehavior: FocusBehavior.KeepTargetForeground);
        item.UpdateAction(action2);

        Assert.Equal(1, item.Index); // Index unchanged
        Assert.Same(action2, item.Action);
        Assert.Contains("dotnet test BackgroundAutomator.sln", item.Details);
        Assert.Contains("[KeepFG]", item.Details);
    }

    [Fact]
    public void MacroViewModel_EditAction_Populates_Builder_And_Sets_Edit_State()
    {
        var vm = CreateMacroViewModel();
        var rule = new CommandApprovalRule
        {
            Name = "Approve date",
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command",
            AllowedCommand = "Get-Date"
        };
        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            focusBehavior: FocusBehavior.KeepTargetForeground,
            timeout: TimeSpan.FromSeconds(30),
            pollInterval: TimeSpan.FromMilliseconds(450),
            waitMode: AutoConfirmWaitMode.Indefinite);

        var item = new MacroActionItem(1, action);
        vm.Actions.Add(item);

        vm.EditAction(item);

        Assert.True(vm.IsEditingAction);
        Assert.False(vm.IsNotEditingAction);
        Assert.Same(item, vm.EditingActionItem);
        Assert.Equal(5, vm.SelectedActionCategoryIndex); // 5 is Safe Auto Confirm tab
        Assert.Equal("Approve date", vm.AutoConfirmRuleName);
        Assert.Equal("WindowsTerminal.exe", vm.AutoConfirmProcess);
        Assert.Equal("Run this command?", vm.AutoConfirmPrompt);
        Assert.Equal("Yes, run command", vm.AutoConfirmSelectedOption);
        Assert.Equal("Get-Date", vm.AutoConfirmAllowedCommand);
        Assert.Equal(AutoConfirmExecutionMode.Confirm, vm.AutoConfirmExecutionMode);
        Assert.Equal(AutoConfirmWaitMode.Indefinite, vm.AutoConfirmWaitMode);
        Assert.Equal(FocusBehavior.KeepTargetForeground, vm.AutoConfirmFocusBehavior);
        Assert.Equal(450, vm.AutoConfirmPollIntervalMs);
        Assert.Contains("Edit Action #1: Safe Auto Confirm", vm.BuilderHeader);
    }

    [Fact]
    public void MacroViewModel_SaveActionEdit_Updates_Action_In_Place_Without_Duplication()
    {
        var vm = CreateMacroViewModel();
        var rule = new CommandApprovalRule
        {
            Name = "Original",
            ExpectedProcess = "WindowsTerminal.exe",
            AllowedCommand = "Get-Date"
        };
        var action = new SafeAutoConfirmAction(rule, executionMode: AutoConfirmExecutionMode.Confirm, focusBehavior: FocusBehavior.FastPulse);
        var item = new MacroActionItem(1, action);
        vm.Actions.Add(item);

        vm.EditAction(item);
        Assert.True(vm.IsEditingAction);

        // Modify in builder
        vm.AutoConfirmAllowedCommand = "dotnet build";
        vm.AutoConfirmFocusBehavior = FocusBehavior.KeepTargetForeground;
        vm.AutoConfirmPollIntervalMs = 750;

        vm.SaveActionEdit();

        // Invariant: action sequence count remains 1 (no duplicate)
        Assert.Single(vm.Actions);
        Assert.False(vm.IsEditingAction);
        Assert.Null(vm.EditingActionItem);
        Assert.True(vm.IsDirty);

        var updated = Assert.IsType<SafeAutoConfirmAction>(vm.Actions[0].Action);
        Assert.Equal("dotnet build", updated.Rule.AllowedCommand);
        Assert.Equal(FocusBehavior.KeepTargetForeground, updated.FocusBehavior);
        Assert.Equal(750, updated.PollInterval.TotalMilliseconds);
    }

    [Fact]
    public void MacroViewModel_CancelActionEdit_Discards_Changes()
    {
        var vm = CreateMacroViewModel();
        var rule = new CommandApprovalRule
        {
            Name = "Original",
            ExpectedProcess = "WindowsTerminal.exe",
            AllowedCommand = "Get-Date"
        };
        var action = new SafeAutoConfirmAction(rule, executionMode: AutoConfirmExecutionMode.Confirm, focusBehavior: FocusBehavior.FastPulse);
        var item = new MacroActionItem(1, action);
        vm.Actions.Add(item);

        vm.EditAction(item);

        // Make modifications in builder
        vm.AutoConfirmAllowedCommand = "should not be saved";

        vm.CancelActionEdit();

        Assert.False(vm.IsEditingAction);
        Assert.Null(vm.EditingActionItem);
        Assert.Single(vm.Actions);

        var current = Assert.IsType<SafeAutoConfirmAction>(vm.Actions[0].Action);
        Assert.Equal("Get-Date", current.Rule.AllowedCommand); // Original preserved!
    }

    [Fact]
    public void MacroViewModel_LoadFromProfile_Populates_First_Action_And_Displays_Profile()
    {
        var vm = CreateMacroViewModel();
        var rule = new CommandApprovalRule
        {
            Name = "Profile Rule",
            ExpectedProcess = "WindowsTerminal.exe",
            AllowedCommand = "git status"
        };
        var action = new SafeAutoConfirmAction(rule, executionMode: AutoConfirmExecutionMode.Confirm, focusBehavior: FocusBehavior.FastPulse);

        vm.LoadFromProfile(new List<IMacroAction> { action }, null, "AgentProfile");

        Assert.Equal("AgentProfile", vm.CurrentProfileName);
        Assert.Equal("AgentProfile", vm.CurrentProfileDisplay);
        Assert.False(vm.IsDirty);
        Assert.Single(vm.Actions);

        // Builder automatically enters edit mode on action 0
        Assert.True(vm.IsEditingAction);
        Assert.Equal(5, vm.SelectedActionCategoryIndex);
        Assert.Equal("git status", vm.AutoConfirmAllowedCommand);

        // Making changes and saving sets dirty indicator
        vm.AutoConfirmAllowedCommand = "git diff";
        vm.SaveActionEdit();
        Assert.Equal("AgentProfile *", vm.CurrentProfileDisplay);
    }

    private class FakeWindowTargetService : WindowTargetService
    {
        public List<WindowTargetCandidate> Candidates { get; set; } = new();

        public FakeWindowTargetService(CoordinateService coordService) : base(coordService) { }

        public override IReadOnlyList<WindowTargetCandidate> EnumerateTopLevelWindows(IntPtr ignoreRootHwnd = default) => Candidates;
    }

    [Fact]
    public void ProfilesViewModel_IsTargetCompatible_Checks_Identity()
    {
        var target = new WindowTarget
        {
            RootHwnd = (IntPtr)0x1234,
            TargetHwnd = (IntPtr)0x1234,
            ProcessName = "WindowsTerminal",
            WindowTitle = "Administrator: Windows PowerShell",
            WindowClass = "CASCADIA_HOSTING_WINDOW_CLASS"
        };

        var exactDescriptor = new TargetDescriptor
        {
            ProcessName = "WindowsTerminal.exe",
            WindowTitle = "Administrator: Windows PowerShell",
            MatchMode = TitleMatchMode.Exact
        };
        Assert.True(ProfilesViewModel.IsTargetCompatible(target, exactDescriptor));

        var containsDescriptor = new TargetDescriptor
        {
            ProcessName = "WindowsTerminal",
            WindowTitle = "PowerShell",
            MatchMode = TitleMatchMode.Contains
        };
        Assert.True(ProfilesViewModel.IsTargetCompatible(target, containsDescriptor));

        var differentProcess = new TargetDescriptor
        {
            ProcessName = "notepad.exe",
            WindowTitle = "PowerShell",
            MatchMode = TitleMatchMode.Contains
        };
        Assert.False(ProfilesViewModel.IsTargetCompatible(target, differentProcess));
    }

    [Fact]
    public void ProfilesViewModel_ReResolve_Clears_Target_When_NotFound()
    {
        var coordService = new CoordinateService();
        var fakeTargetService = new FakeWindowTargetService(coordService);
        var targetResolver = new TargetResolver(fakeTargetService, coordService);

        WindowTarget? committedTarget = new WindowTarget
        {
            RootHwnd = (IntPtr)0x5555,
            TargetHwnd = (IntPtr)0x5555,
            ProcessName = "StaleProcess"
        };

        var vm = new ProfilesViewModel(
            _storage,
            targetResolver,
            _logger,
            onTargetCommitted: target => committedTarget = target,
            getCurrentTarget: () => committedTarget,
            loadSimpleProfile: (_, _) => { },
            loadMacroProfile: (_, _, _) => { },
            getSimplePoints: () => new List<ClickPoint>(),
            getSimpleSettings: () => new ClickRunnerSettingsConfig(),
            getMacroActions: () => new List<IMacroAction>());

        vm.ActiveProfileTarget = new TargetDescriptor
        {
            ProcessName = "NonExistentApp.exe",
            WindowTitle = "None",
            MatchMode = TitleMatchMode.Exact
        };

        vm.ReResolveTargetSilently();

        // Invariant: Target must be cleared on NotFound to prevent contradictory UI status
        Assert.Null(committedTarget);
        Assert.Contains("Not found", vm.RuntimeTargetText);
    }

    [Fact]
    public void ProfilesViewModel_ReResolve_Clears_Target_When_Ambiguous()
    {
        var coordService = new CoordinateService();
        var fakeTargetService = new FakeWindowTargetService(coordService)
        {
            Candidates = new List<WindowTargetCandidate>
            {
                new((IntPtr)0x1111, 10, 20, "wt.exe", "Tab 1", "CASCADIA"),
                new((IntPtr)0x2222, 10, 21, "wt.exe", "Tab 2", "CASCADIA")
            }
        };
        var targetResolver = new TargetResolver(fakeTargetService, coordService);

        WindowTarget? committedTarget = new WindowTarget
        {
            RootHwnd = (IntPtr)0x5555,
            TargetHwnd = (IntPtr)0x5555,
            ProcessName = "StaleProcess"
        };

        var vm = new ProfilesViewModel(
            _storage,
            targetResolver,
            _logger,
            onTargetCommitted: target => committedTarget = target,
            getCurrentTarget: () => committedTarget,
            loadSimpleProfile: (_, _) => { },
            loadMacroProfile: (_, _, _) => { },
            getSimplePoints: () => new List<ClickPoint>(),
            getSimpleSettings: () => new ClickRunnerSettingsConfig(),
            getMacroActions: () => new List<IMacroAction>());

        vm.ActiveProfileTarget = new TargetDescriptor
        {
            ProcessName = "wt.exe",
            WindowTitle = null,
            MatchMode = TitleMatchMode.Any
        };

        vm.ReResolveTargetSilently();

        // Invariant: Ambiguous match clears live target and displays ambiguous count
        Assert.Null(committedTarget);
        Assert.Contains("Ambiguous", vm.RuntimeTargetText);
        Assert.Contains("2", vm.RuntimeTargetText);
    }
}
