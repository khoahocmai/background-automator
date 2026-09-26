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

    [Fact]
    public void MacroActionConfig_RoundTrips_MultiRule_RuleSet_And_WaitMode_Indefinite()
    {
        var rule1 = new CommandApprovalRule { Name = "Build", AllowedCommand = "dotnet build" };
        var rule2 = new CommandApprovalRule { Name = "Test", AllowedCommand = "dotnet test" };
        var rule3 = new CommandApprovalRule { Name = "Date", AllowedCommand = "Get-Date", Enabled = false };

        var ruleSet = new ApprovalRuleSet("DevSet", new[] { rule1, rule2, rule3 });
        var action = new SafeAutoConfirmAction(
            ruleSet,
            executionMode: AutoConfirmExecutionMode.Confirm,
            waitMode: AutoConfirmWaitMode.Indefinite);

        var config = MacroActionConfig.FromMacroAction(action);

        Assert.Equal("SafeAutoConfirm", config.ActionType);
        Assert.Equal("Indefinite", config.WaitMode);
        Assert.NotNull(config.RuleSet);
        Assert.Equal(3, config.RuleSet.Rules.Count);
        Assert.Equal("dotnet build", config.AllowedCommand); // legacy fallback populated

        var restored = Assert.IsType<SafeAutoConfirmAction>(config.ToMacroAction());

        Assert.Equal(AutoConfirmWaitMode.Indefinite, restored.WaitMode);
        Assert.Equal("DevSet", restored.RuleSet.Name);
        Assert.Equal(3, restored.RuleSet.Rules.Count);
        Assert.Equal("dotnet build", restored.RuleSet.Rules[0].AllowedCommand);
        Assert.Equal("dotnet test", restored.RuleSet.Rules[1].AllowedCommand);
        Assert.Equal("Get-Date", restored.RuleSet.Rules[2].AllowedCommand);
        Assert.False(restored.RuleSet.Rules[2].Enabled);
    }

    [Fact]
    public void Legacy_SafeAutoConfirm_Without_RuleSet_Migrates_To_RuleSet()
    {
        string legacyJson = @"{
  ""actionType"": ""SafeAutoConfirm"",
  ""ruleName"": ""Legacy Rule"",
  ""expectedProcess"": ""WindowsTerminal.exe"",
  ""expectedPrompt"": ""Run this command?"",
  ""expectedSelectedOption"": ""Yes, run command"",
  ""allowedCommand"": ""Get-Date"",
  ""commandMatchMode"": ""Exact"",
  ""executionMode"": ""ObserveOnly"",
  ""deliveryMode"": ""ForegroundPulse"",
  ""timeoutMs"": 60000,
  ""pollIntervalMs"": 500
}";

        var config = System.Text.Json.JsonSerializer.Deserialize<MacroActionConfig>(legacyJson, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(config);
        Assert.Null(config.RuleSet);
        Assert.Null(config.WaitMode);

        var action = Assert.IsType<SafeAutoConfirmAction>(config.ToMacroAction());

        Assert.Equal(AutoConfirmWaitMode.FixedTimeout, action.WaitMode);
        Assert.NotNull(action.RuleSet);
        var singleRule = Assert.Single(action.RuleSet.Rules);
        Assert.Equal("Get-Date", singleRule.AllowedCommand);
        Assert.Equal("Legacy Rule", singleRule.Name);
        Assert.True(singleRule.Enabled);
    }

    [Fact]
    public void Profile_With_MacroSettings_And_Startup_Saves_And_Loads()
    {
        var profile = new ProfileModel
        {
            Name = "ContinuousWatcherProfile",
            Mode = ProfileMode.Macro,
            IsStartupProfile = true,
            MacroSettings = new MacroRunnerSettingsConfig
            {
                RepeatMode = "UntilStopped",
                RepeatCount = 1,
                CycleDelayMilliseconds = 250
            }
        };

        _storage.SaveProfile(profile);

        var loaded = _storage.LoadProfileByName("ContinuousWatcherProfile");
        Assert.NotNull(loaded);
        Assert.True(loaded.IsStartupProfile);
        Assert.NotNull(loaded.MacroSettings);
        Assert.Equal("UntilStopped", loaded.MacroSettings.RepeatMode);
        Assert.Equal(250, loaded.MacroSettings.CycleDelayMilliseconds);
    }

    [Fact]
    public void AppPreferences_Saves_And_Loads_Startup_And_LastUsed()
    {
        _storage.SetStartupProfileName("MyStartupProfile");
        _storage.SetLastUsedProfileName("MyLastProfile");

        Assert.Equal("MyStartupProfile", _storage.GetStartupProfileName());
        Assert.Equal("MyLastProfile", _storage.GetLastUsedProfileName());

        var prefs = _storage.LoadAppPreferences();
        Assert.Equal("MyStartupProfile", prefs.StartupProfileName);
        Assert.Equal("MyLastProfile", prefs.LastUsedProfileName);
    }
}
