using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Profiles;
using BackgroundAutomator.Core.Targeting;
using Xunit;

namespace BackgroundAutomator.Tests;

public class SafeAutoConfirmProfileTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly ProfileStorageService _storage;

    public SafeAutoConfirmProfileTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BackgroundAutomator_AutoConfirmProfileTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _storage = new ProfileStorageService(_tempDirectory);
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
        catch
        {
            // Best-effort cleanup
        }
    }

    [Fact]
    public void MacroActionConfig_RoundTrips_SafeAutoConfirmAction()
    {
        var rule = new CommandApprovalRule
        {
            Name = "Approve tests",
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedWindowClass = "CASCADIA",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command",
            AllowedCommand = "dotnet test BackgroundAutomator.sln",
            CommandMatchMode = CommandMatchMode.Exact,
            Enabled = true
        };

        var originalAction = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.Confirm,
            deliveryMode: KeyDeliveryMode.ForegroundPulse,
            timeout: TimeSpan.FromSeconds(45),
            pollInterval: TimeSpan.FromMilliseconds(250));

        var config = MacroActionConfig.FromMacroAction(originalAction);

        Assert.Equal("SafeAutoConfirm", config.ActionType);
        Assert.Equal("Approve tests", config.RuleName);
        Assert.Equal("WindowsTerminal.exe", config.ExpectedProcess);
        Assert.Equal("CASCADIA", config.ExpectedWindowClass);
        Assert.Equal("Run this command?", config.ExpectedPrompt);
        Assert.Equal("Yes, run command", config.ExpectedSelectedOption);
        Assert.Equal("dotnet test BackgroundAutomator.sln", config.AllowedCommand);
        Assert.Equal("Exact", config.CommandMatchMode);
        Assert.Equal("Confirm", config.ExecutionMode);
        Assert.Equal("ForegroundPulse", config.DeliveryMode);
        Assert.Equal(45000, config.TimeoutMs);
        Assert.Equal(250, config.PollIntervalMs);

        var restoredAction = Assert.IsType<SafeAutoConfirmAction>(config.ToMacroAction());

        Assert.Equal(originalAction.Rule.Name, restoredAction.Rule.Name);
        Assert.Equal(originalAction.Rule.ExpectedProcess, restoredAction.Rule.ExpectedProcess);
        Assert.Equal(originalAction.Rule.ExpectedWindowClass, restoredAction.Rule.ExpectedWindowClass);
        Assert.Equal(originalAction.Rule.ExpectedPrompt, restoredAction.Rule.ExpectedPrompt);
        Assert.Equal(originalAction.Rule.ExpectedSelectedOption, restoredAction.Rule.ExpectedSelectedOption);
        Assert.Equal(originalAction.Rule.AllowedCommand, restoredAction.Rule.AllowedCommand);
        Assert.Equal(originalAction.Rule.CommandMatchMode, restoredAction.Rule.CommandMatchMode);
        Assert.Equal(originalAction.ExecutionMode, restoredAction.ExecutionMode);
        Assert.Equal(originalAction.DeliveryMode, restoredAction.DeliveryMode);
        Assert.Equal(originalAction.Timeout, restoredAction.Timeout);
        Assert.Equal(originalAction.PollInterval, restoredAction.PollInterval);
    }

    [Fact]
    public void Profile_With_SafeAutoConfirm_Saves_And_Loads_Atomically()
    {
        var rule = new CommandApprovalRule
        {
            Name = "Approve Antigravity Build",
            ExpectedProcess = "WindowsTerminal.exe",
            ExpectedPrompt = "Run this command?",
            ExpectedSelectedOption = "Yes, run command",
            AllowedCommand = "dotnet build BackgroundAutomator.sln",
            CommandMatchMode = CommandMatchMode.Exact
        };

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode.ObserveOnly,
            timeout: TimeSpan.FromSeconds(30));

        var profile = new ProfileModel
        {
            Version = ProfileModel.CurrentSchemaVersion,
            Name = "TerminalAutoApproveProfile",
            Mode = ProfileMode.Macro,
            Target = new TargetDescriptor
            {
                ProcessName = "WindowsTerminal",
                WindowTitle = "PowerShell",
                WindowClass = "CASCADIA_HOSTING_WINDOW_CLASS",
                MatchMode = TitleMatchMode.Contains
            },
            MacroActions = new List<MacroActionConfig>
            {
                MacroActionConfig.FromMacroAction(action)
            }
        };

        _storage.SaveProfile(profile);

        string savedProfilePath = _storage.GetProfileFilePath("TerminalAutoApproveProfile");
        Assert.True(File.Exists(savedProfilePath));

        var loadedProfile = _storage.LoadProfile(savedProfilePath);
        Assert.NotNull(loadedProfile);

        var loadedActionConfig = Assert.Single(loadedProfile.MacroActions);
        var loadedAction = Assert.IsType<SafeAutoConfirmAction>(loadedActionConfig.ToMacroAction());

        Assert.Equal("Approve Antigravity Build", loadedAction.Rule.Name);
        Assert.Equal("dotnet build BackgroundAutomator.sln", loadedAction.Rule.AllowedCommand);
        Assert.Equal(AutoConfirmExecutionMode.ObserveOnly, loadedAction.ExecutionMode);
    }

    [Fact]
    public void Legacy_Profiles_Remain_Compatible()
    {
        // JSON representing existing pre-Phase-4 profile with diverse actions
        string legacyJson = @"{
  ""version"": ""1.0"",
  ""name"": ""LegacyProfile"",
  ""mode"": ""Macro"",
  ""macroActions"": [
    { ""actionType"": ""Click"", ""x"": 10, ""y"": 20 },
    { ""actionType"": ""DoubleClick"", ""x"": 30, ""y"": 40 },
    { ""actionType"": ""Delay"", ""delayMs"": 500 },
    { ""actionType"": ""WaitColor"", ""x"": 50, ""y"": 60, ""colorHex"": ""#00FF00"", ""tolerance"": 5, ""timeoutMs"": 5000 },
    { ""actionType"": ""PressKey"", ""key"": ""Enter"" },
    { ""actionType"": ""WaitForText"", ""expectedText"": ""Ready"", ""textMatchMode"": ""Contains"", ""timeoutMs"": 10000 }
  ]
}";

        string profilePath = Path.Combine(_tempDirectory, "LegacyProfile.json");
        File.WriteAllText(profilePath, legacyJson);

        var loadedProfile = _storage.LoadProfile(profilePath);
        Assert.NotNull(loadedProfile);

        var actions = loadedProfile.MacroActions.Select(c => c.ToMacroAction()).ToList();
        Assert.Equal(6, actions.Count);
        Assert.IsType<ClickAction>(actions[0]);
        Assert.IsType<DoubleClickAction>(actions[1]);
        Assert.IsType<DelayAction>(actions[2]);
        Assert.IsType<WaitColorAction>(actions[3]);
        Assert.IsType<PressKeyAction>(actions[4]);
        Assert.IsType<WaitForTextAction>(actions[5]);
    }
}
