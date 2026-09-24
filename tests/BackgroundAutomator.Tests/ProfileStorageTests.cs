using System.Drawing;
using System.Text.Json;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Profiles;
using BackgroundAutomator.Core.Runner;
using BackgroundAutomator.Core.Targeting;
using Xunit;

namespace BackgroundAutomator.Tests;

public class ProfileStorageTests : IDisposable
{
    private readonly string _testDir;
    private readonly ProfileStorageService _storage;

    public ProfileStorageTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "BackgroundAutomator_Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _storage = new ProfileStorageService(_testDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void SaveProfile_CreatesValidJsonFileAtomically()
    {
        var profile = new ProfileModel
        {
            Name = "AtomicTestProfile",
            Mode = ProfileMode.Simple,
            Target = new TargetDescriptor
            {
                ProcessName = "notepad",
                WindowTitle = "Untitled",
                MatchMode = TitleMatchMode.StartsWith,
                SavedClientX = 150,
                SavedClientY = 250
            },
            SimpleSettings = new ClickRunnerSettingsConfig
            {
                IntervalMs = 250,
                RepeatMode = RepeatMode.Count,
                RepeatCount = 5
            },
            ClickPoints = new List<ClickPointConfig>
            {
                new(100, 200, ClickType.Single, "Point 1"),
                new(300, 400, ClickType.Double, "Point 2")
            }
        };

        string targetPath = _storage.GetProfileFilePath(profile.Name);
        _storage.SaveProfile(profile);

        Assert.True(File.Exists(targetPath));

        // Verify no leftover .tmp files
        var tmpFiles = Directory.GetFiles(_testDir, "*.tmp");
        Assert.Empty(tmpFiles);

        // Verify contents
        var loaded = _storage.LoadProfile(targetPath);
        Assert.Equal("AtomicTestProfile", loaded.Name);
        Assert.Equal(ProfileMode.Simple, loaded.Mode);
        Assert.NotNull(loaded.Target);
        Assert.Equal("notepad", loaded.Target.ProcessName);
        Assert.Equal("Untitled", loaded.Target.WindowTitle);
        Assert.Equal(150, loaded.Target.SavedClientX);
        Assert.Equal(250, loaded.Target.SavedClientY);
        Assert.NotNull(loaded.SimpleSettings);
        Assert.Equal(250, loaded.SimpleSettings.IntervalMs);
        Assert.Equal(RepeatMode.Count, loaded.SimpleSettings.RepeatMode);
        Assert.Equal(5, loaded.SimpleSettings.RepeatCount);
        Assert.Equal(2, loaded.ClickPoints.Count);
        Assert.Equal(ClickType.Double, loaded.ClickPoints[1].ClickType);
    }

    [Fact]
    public void SaveAndLoad_MacroModeProfile_AllActionTypesPreserved()
    {
        var actions = new List<IMacroAction>
        {
            new ClickAction(10, 20),
            new DelayAction(500),
            new DoubleClickAction(30, 40),
            new WaitColorAction(100, 200, Color.FromArgb(255, 128, 64), tolerance: 15, timeout: TimeSpan.FromSeconds(3), pollInterval: TimeSpan.FromMilliseconds(80))
        };

        var macroConfigs = actions.Select(MacroActionConfig.FromMacroAction).ToList();

        var profile = new ProfileModel
        {
            Name = "MacroProfileTest",
            Mode = ProfileMode.Macro,
            MacroActions = macroConfigs
        };

        _storage.SaveProfile(profile);
        var loaded = _storage.LoadProfileByName("MacroProfileTest");

        Assert.Equal(ProfileMode.Macro, loaded.Mode);
        Assert.Equal(4, loaded.MacroActions.Count);

        var loadedActions = loaded.MacroActions.Select(c => c.ToMacroAction()).ToList();

        // Check Action 1: Click
        var click = Assert.IsType<ClickAction>(loadedActions[0]);
        Assert.Equal(10, click.ClientX);
        Assert.Equal(20, click.ClientY);

        // Check Action 2: Delay
        var delay = Assert.IsType<DelayAction>(loadedActions[1]);
        Assert.Equal(500, delay.Milliseconds);

        // Check Action 3: DoubleClick
        var dblClick = Assert.IsType<DoubleClickAction>(loadedActions[2]);
        Assert.Equal(30, dblClick.ClientX);
        Assert.Equal(40, dblClick.ClientY);

        // Check Action 4: WaitColor
        var waitColor = Assert.IsType<WaitColorAction>(loadedActions[3]);
        Assert.Equal(100, waitColor.ClientX);
        Assert.Equal(200, waitColor.ClientY);
        Assert.Equal(Color.FromArgb(255, 128, 64).ToArgb(), waitColor.TargetColor.ToArgb());
        Assert.Equal(15, waitColor.Tolerance);
        Assert.Equal(TimeSpan.FromSeconds(3), waitColor.Timeout);
        Assert.Equal(TimeSpan.FromMilliseconds(80), waitColor.PollInterval);
    }

    [Fact]
    public void LoadProfile_CorruptJson_ThrowsException()
    {
        string corruptPath = Path.Combine(_testDir, "corrupt.json");
        File.WriteAllText(corruptPath, "{ invalid json content !!!");

        Assert.ThrowsAny<Exception>(() => _storage.LoadProfile(corruptPath));
    }

    [Fact]
    public void LoadProfile_NonExistentFile_ThrowsFileNotFoundException()
    {
        string missingPath = Path.Combine(_testDir, "does_not_exist.json");
        Assert.Throws<FileNotFoundException>(() => _storage.LoadProfile(missingPath));
    }

    [Fact]
    public void ListProfiles_SkipsCorruptFilesAndReturnsValidHeaders()
    {
        // 1. Valid profile
        var validProfile = new ProfileModel
        {
            Name = "ValidProfile",
            Mode = ProfileMode.Simple,
            Target = new TargetDescriptor { ProcessName = "calc" },
            ClickPoints = new List<ClickPointConfig> { new(1, 2) }
        };
        _storage.SaveProfile(validProfile);

        // 2. Corrupt file
        string corruptPath = Path.Combine(_testDir, "broken.json");
        File.WriteAllText(corruptPath, "BROKEN JSON");

        var headers = _storage.ListProfiles();

        // Must find exactly 1 valid profile without throwing an unhandled exception
        Assert.Single(headers);
        Assert.Equal("ValidProfile", headers[0].Name);
        Assert.Equal(ProfileMode.Simple, headers[0].Mode);
        Assert.Equal(1, headers[0].ItemCount);
    }

    [Fact]
    public void DeleteProfile_RemovesFileFromDisk()
    {
        var profile = new ProfileModel { Name = "ToDelete" };
        _storage.SaveProfile(profile);

        string path = _storage.GetProfileFilePath("ToDelete");
        Assert.True(File.Exists(path));

        bool deleted = _storage.DeleteProfile("ToDelete");
        Assert.True(deleted);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LoadProfile_UnsupportedVersionString_ThrowsNotSupportedException()
    {
        string path = Path.Combine(_testDir, "unsupported_version.json");
        File.WriteAllText(path, """
        {
            "version": "999.0",
            "name": "FutureProfile",
            "mode": "Simple"
        }
        """);

        var ex = Assert.Throws<NotSupportedException>(() => _storage.LoadProfile(path));
        Assert.Contains("999.0", ex.Message);
    }

    [Fact]
    public void LoadProfile_UnsupportedVersionInt_ThrowsException()
    {
        string path = Path.Combine(_testDir, "int_version.json");
        File.WriteAllText(path, """
        {
            "version": 999,
            "name": "IntVersionProfile",
            "mode": "Simple"
        }
        """);

        Assert.ThrowsAny<Exception>(() => _storage.LoadProfile(path));
    }

    [Fact]
    public void LoadProfile_MissingSchemaVersion_ThrowsInvalidDataException()
    {
        string path = Path.Combine(_testDir, "missing_version.json");
        File.WriteAllText(path, """
        {
            "name": "NoVersionProfile",
            "mode": "Simple"
        }
        """);

        Assert.Throws<InvalidDataException>(() => _storage.LoadProfile(path));
    }

    [Fact]
    public void LoadProfile_MissingName_ThrowsInvalidDataException()
    {
        string path = Path.Combine(_testDir, "missing_name.json");
        File.WriteAllText(path, """
        {
            "version": "1.0",
            "mode": "Simple"
        }
        """);

        Assert.Throws<InvalidDataException>(() => _storage.LoadProfile(path));
    }

    [Fact]
    public void MacroActionConfig_UnknownActionType_ThrowsInvalidOperationException()
    {
        var config = new MacroActionConfig
        {
            ActionType = "UnknownFutureAction",
            X = 10,
            Y = 20
        };

        var ex = Assert.Throws<InvalidOperationException>(() => config.ToMacroAction());
        Assert.Contains("UnknownFutureAction", ex.Message);
    }
}
