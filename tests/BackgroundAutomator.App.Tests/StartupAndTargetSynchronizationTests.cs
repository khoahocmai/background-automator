using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using BackgroundAutomator.App.ViewModels;
using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Coordinates;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Profiles;
using BackgroundAutomator.Core.Security;
using BackgroundAutomator.Core.Targeting;
using Xunit;

namespace BackgroundAutomator.App.Tests;

public class StartupAndTargetSynchronizationTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ProfileStorageService _storage;
    private readonly InMemoryLogger _logger;
    private readonly CoordinateService _coordService;

    public StartupAndTargetSynchronizationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BA_StartupTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _logger = new InMemoryLogger();
        _storage = new ProfileStorageService(Path.Combine(_tempDirectory, "profiles"), _logger);
        _coordService = new CoordinateService(_logger);
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

    private class TestableWindowTargetService : WindowTargetService
    {
        public List<WindowTargetCandidate> Candidates { get; set; } = new();

        public TestableWindowTargetService(CoordinateService coordService) : base(coordService) { }

        public override IReadOnlyList<WindowTargetCandidate> EnumerateTopLevelWindows(IntPtr ignoreRootHwnd = default) => Candidates;
    }

    private class TestableTargetResolver : TargetResolver
    {
        public TargetResolutionResult? CustomResult { get; set; }

        public TestableTargetResolver(WindowTargetService targetService, CoordinateService coordinateService, IAppLogger? logger = null)
            : base(targetService, coordinateService, logger)
        {
        }

        public override TargetResolutionResult Resolve(TargetDescriptor descriptor, IntPtr ignoreHwnd = default)
        {
            if (CustomResult != null)
                return CustomResult;
            return base.Resolve(descriptor, ignoreHwnd);
        }
    }

    [Fact]
    public void Startup_WithSavedProfile_UnresolvableTarget_InitializesWithoutStackOverflow_AndSetsIdleState()
    {
        // Arrange: Create a saved profile resembling user's Agent.json with an unresolvable target
        var rule = new CommandApprovalRule
        {
            Name = "Approve date",
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command",
            AllowedCommand = "Get-Date"
        };
        var action = new SafeAutoConfirmAction(rule, executionMode: AutoConfirmExecutionMode.Confirm, focusBehavior: FocusBehavior.FastPulse);

        var profile = new ProfileModel
        {
            Version = ProfileModel.CurrentSchemaVersion,
            Name = "Agent",
            Mode = ProfileMode.Macro,
            Target = new TargetDescriptor
            {
                ProcessName = "WindowsTerminal.exe",
                WindowTitle = "C:\\WINDOWS\\system32\\cmd.exe",
                MatchMode = TitleMatchMode.Contains
            },
            MacroActions = new List<MacroActionConfig>
            {
                MacroActionConfig.FromMacroAction(action)
            },
            MacroSettings = new MacroRunnerSettingsConfig
            {
                RepeatMode = "UntilStopped",
                RepeatCount = 1,
                CycleDelayMilliseconds = 500
            }
        };

        _storage.SaveProfile(profile);
        _storage.SetLastUsedProfileName("Agent");

        var targetService = new TestableWindowTargetService(_coordService);
        var mainVm = new MainViewModel(_logger, _storage, targetService);

        // Act: Initialize MainViewModel (reproduces the startup sequence)
        mainVm.Initialize(IntPtr.Zero);

        // Assert: No stack overflow occurred; state initialized cleanly
        Assert.Null(mainVm.CurrentTarget);
        Assert.Equal("Target: None", mainVm.StatusTargetText);
        Assert.Equal("Runner: IDLE", mainVm.StatusRunnerText);
        Assert.Single(mainVm.MacroVM.Actions);
        Assert.False(mainVm.MacroVM.IsDirty);
        Assert.True(mainVm.MacroVM.IsEditingAction);
        Assert.Equal("Agent", mainVm.ProfilesVM.CurrentLoadedProfileName);
        Assert.Null(mainVm.TargetVM.CurrentTarget);
        Assert.False(mainVm.TargetVM.IsTargetActive);
    }

    [Fact]
    public void Startup_WithSavedProfile_ResolvableTarget_InitializesCleanly_AndSetsTarget()
    {
        // Arrange
        var targetService = new TestableWindowTargetService(_coordService);

        var profile = new ProfileModel
        {
            Version = ProfileModel.CurrentSchemaVersion,
            Name = "AutoProfile",
            Mode = ProfileMode.Macro,
            Target = new TargetDescriptor
            {
                ProcessName = "TargetApp.exe",
                WindowTitle = "Main Window",
                MatchMode = TitleMatchMode.Contains
            },
            MacroActions = new List<MacroActionConfig>
            {
                MacroActionConfig.FromMacroAction(new DelayAction(200))
            }
        };

        _storage.SaveProfile(profile);
        _storage.SetLastUsedProfileName("AutoProfile");

        var targetResolver = new TestableTargetResolver(targetService, _coordService, _logger)
        {
            CustomResult = TargetResolutionResult.CreateSuccess(new WindowTarget
            {
                RootHwnd = (IntPtr)0x8888,
                TargetHwnd = (IntPtr)0x8888,
                ProcessName = "TargetApp.exe",
                WindowTitle = "Main Window",
                WindowClass = "AppClass",
                ScreenPoint = new Point(100, 100),
                ClientPoint = new TargetPoint((IntPtr)0x8888, 10, 10)
            })
        };

        var mainVm = new MainViewModel(_logger, _storage, targetService, targetResolver);

        // Act
        mainVm.Initialize(IntPtr.Zero);

        // Assert
        Assert.NotNull(mainVm.CurrentTarget);
        Assert.Equal("TargetApp.exe", mainVm.CurrentTarget.ProcessName);
        Assert.NotNull(mainVm.TargetVM.CurrentTarget);
        Assert.True(mainVm.TargetVM.IsTargetActive);
        Assert.Equal("Runner: IDLE", mainVm.StatusRunnerText);
        Assert.Equal("AutoProfile", mainVm.ProfilesVM.CurrentLoadedProfileName);
    }

    [Fact]
    public void TargetViewModel_ClearTarget_UserAction_ClearsAuthoritativeTarget_WithoutInfiniteRecursion()
    {
        // Arrange: MainViewModel starts with an active target
        var targetService = new TestableWindowTargetService(_coordService);
        var mainVm = new MainViewModel(_logger, _storage, targetService);
        mainVm.Initialize(IntPtr.Zero);

        var target = new WindowTarget
        {
            RootHwnd = (IntPtr)0x1234,
            TargetHwnd = (IntPtr)0x1234,
            ProcessName = "App.exe",
            WindowTitle = "App Window",
            WindowClass = "AppClass"
        };

        // User selected/committed target in TargetVM
        mainVm.TargetVM.CommitTarget(target);

        Assert.NotNull(mainVm.CurrentTarget);
        Assert.NotNull(mainVm.TargetVM.CurrentTarget);
        Assert.True(mainVm.TargetVM.IsTargetActive);

        // Act: User clicks "Clear Target" in Target Inspector
        mainVm.TargetVM.ClearTargetCommand.Execute(null);

        // Assert: Target is cleanly cleared everywhere without recursion
        Assert.Null(mainVm.CurrentTarget);
        Assert.Null(mainVm.TargetVM.CurrentTarget);
        Assert.False(mainVm.TargetVM.IsTargetActive);
        Assert.Equal("Target: None", mainVm.StatusTargetText);
        Assert.Equal("Target cleared", mainVm.TargetVM.TargetStatus);
    }

    [Fact]
    public void TargetViewModel_ClearTarget_WhenAlreadyCleared_IsSafeNoOp()
    {
        var targetService = new TestableWindowTargetService(_coordService);
        var mainVm = new MainViewModel(_logger, _storage, targetService);
        mainVm.Initialize(IntPtr.Zero);

        Assert.Null(mainVm.TargetVM.CurrentTarget);
        Assert.False(mainVm.TargetVM.IsTargetActive);

        // Act: Execute clear when already clear
        mainVm.TargetVM.ClearTargetCommand.Execute(null);

        // Assert: No errors, still null
        Assert.Null(mainVm.TargetVM.CurrentTarget);
        Assert.False(mainVm.TargetVM.IsTargetActive);
    }

    [Fact]
    public void TargetViewModel_SetCurrentTarget_DoesNotTriggerOnTargetCommittedCallback()
    {
        int callbackCount = 0;
        var coordService = new CoordinateService();
        var targetService = new WindowTargetService(coordService);
        var elevationService = new ProcessElevationService();

        var targetVm = new TargetViewModel(
            targetService,
            elevationService,
            _logger,
            onTargetCommitted: _ => callbackCount++);

        var target = new WindowTarget
        {
            RootHwnd = (IntPtr)0x9999,
            TargetHwnd = (IntPtr)0x9999,
            ProcessName = "Sample.exe"
        };

        // Act: Directly calling SetCurrentTarget (downward observation from MainViewModel)
        targetVm.SetCurrentTarget(target);
        targetVm.SetCurrentTarget(null);

        // Assert: Observer method must NOT fire onTargetCommitted callback back to MainViewModel
        Assert.Equal(0, callbackCount);
    }
}
