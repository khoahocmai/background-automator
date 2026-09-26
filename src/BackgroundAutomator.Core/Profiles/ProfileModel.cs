using BackgroundAutomator.Core.Targeting;

namespace BackgroundAutomator.Core.Profiles;

/// <summary>
/// Root model representing a saved BackgroundAutomator profile.
/// Uses durable TargetDescriptors rather than raw HWND handles.
/// </summary>
public sealed class ProfileModel
{
    public const string CurrentSchemaVersion = "1.0";
    public static readonly string[] SupportedVersions = { CurrentSchemaVersion };

    public static bool IsVersionSupported(string? version) =>
        !string.IsNullOrWhiteSpace(version) && SupportedVersions.Contains(version, StringComparer.OrdinalIgnoreCase);

    public string Version { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public TargetDescriptor? Target { get; set; }
    public ProfileMode Mode { get; set; } = ProfileMode.Simple;
    public ClickRunnerSettingsConfig? SimpleSettings { get; set; }
    public List<ClickPointConfig> ClickPoints { get; set; } = new();
    public List<MacroActionConfig> MacroActions { get; set; } = new();
    public MacroRunnerSettingsConfig? MacroSettings { get; set; }
    public bool IsStartupProfile { get; set; }
}
