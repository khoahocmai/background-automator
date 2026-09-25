using System.Text.Json;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Profiles;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Core.TextDetection;
using Xunit;

namespace BackgroundAutomator.Tests;

public class WaitForTextProfileTests : IDisposable
{
    private readonly string _testProfileDir;

    public WaitForTextProfileTests()
    {
        _testProfileDir = Path.Combine(Path.GetTempPath(), "BackgroundAutomator_TestProfiles_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testProfileDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testProfileDir))
            {
                Directory.Delete(_testProfileDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public void MacroActionConfig_Serializes_And_Deserializes_WaitForTextAction()
    {
        var original = new WaitForTextAction(
            "Run this command?",
            TextMatchMode.Exact,
            timeout: TimeSpan.FromSeconds(45),
            pollInterval: TimeSpan.FromMilliseconds(250));

        var config = MacroActionConfig.FromMacroAction(original);

        Assert.Equal("WaitForText", config.ActionType);
        Assert.Equal("Run this command?", config.ExpectedText);
        Assert.Equal("Exact", config.TextMatchMode);
        Assert.Equal(45000, config.TimeoutMs);
        Assert.Equal(250, config.PollIntervalMs);

        var restored = (WaitForTextAction)config.ToMacroAction();

        Assert.Equal(original.ExpectedText, restored.ExpectedText);
        Assert.Equal(original.MatchMode, restored.MatchMode);
        Assert.Equal(original.Timeout, restored.Timeout);
        Assert.Equal(original.PollInterval, restored.PollInterval);
    }

    [Fact]
    public void ProfileStorageService_Saves_And_Loads_Profile_With_WaitForTextAction()
    {
        var storage = new ProfileStorageService(_testProfileDir, new InMemoryLogger());

        var model = new ProfileModel
        {
            Name = "PromptAutoDetectionProfile",
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
                MacroActionConfig.FromMacroAction(new WaitForTextAction("Run this command?", TextMatchMode.Contains, TimeSpan.FromSeconds(60))),
                MacroActionConfig.FromMacroAction(new PressKeyAction(BackgroundAutomator.Core.Keyboard.BackgroundKey.Enter))
            }
        };

        storage.SaveProfile(model);

        var loaded = storage.LoadProfileByName("PromptAutoDetectionProfile");
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.MacroActions.Count);

        var action0 = loaded.MacroActions[0].ToMacroAction();
        Assert.IsType<WaitForTextAction>(action0);

        var waitAction = (WaitForTextAction)action0;
        Assert.Equal("Run this command?", waitAction.ExpectedText);
        Assert.Equal(TextMatchMode.Contains, waitAction.MatchMode);
        Assert.Equal(TimeSpan.FromSeconds(60), waitAction.Timeout);

        var action1 = loaded.MacroActions[1].ToMacroAction();
        Assert.IsType<PressKeyAction>(action1);
    }

    [Fact]
    public void Older_Profile_Without_WaitForText_Loads_Without_Error()
    {
        // JSON simulating an older profile schema with only Click and Delay
        string oldJson = """
        {
            "version": "1.0",
            "name": "LegacyProfile",
            "createdAt": "2026-09-24T00:00:00Z",
            "updatedAt": "2026-09-24T00:00:00Z",
            "mode": 1,
            "target": {
                "processName": "notepad",
                "windowTitle": "Untitled - Notepad",
                "windowClass": "Notepad",
                "matchMode": 1
            },
            "macroActions": [
                {
                    "actionType": "Click",
                    "x": 100,
                    "y": 200
                },
                {
                    "actionType": "Delay",
                    "delayMs": 500
                }
            ]
        }
        """;

        string filePath = Path.Combine(_testProfileDir, "LegacyProfile.json");
        File.WriteAllText(filePath, oldJson);

        var storage = new ProfileStorageService(_testProfileDir, new InMemoryLogger());
        var profile = storage.LoadProfile(filePath);

        Assert.NotNull(profile);
        Assert.Equal(2, profile.MacroActions.Count);
        Assert.IsType<ClickAction>(profile.MacroActions[0].ToMacroAction());
        Assert.IsType<DelayAction>(profile.MacroActions[1].ToMacroAction());
    }
}
