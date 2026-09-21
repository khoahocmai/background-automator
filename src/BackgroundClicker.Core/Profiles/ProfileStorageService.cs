using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackgroundClicker.Core.Logging;

namespace BackgroundClicker.Core.Profiles;

/// <summary>
/// Service responsible for saving, loading, listing, and deleting profiles.
/// Uses atomic file writes (temporary file + move/replace) to prevent corruption.
/// </summary>
public class ProfileStorageService
{
    private readonly string _profilesDirectory;
    private readonly IAppLogger? _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ProfilesDirectory => _profilesDirectory;

    public ProfileStorageService(string? customDirectory = null, IAppLogger? logger = null)
    {
        _logger = logger;
        if (!string.IsNullOrWhiteSpace(customDirectory))
        {
            _profilesDirectory = customDirectory;
        }
        else
        {
            _profilesDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BackgroundClicker",
                "profiles");
        }

        try
        {
            Directory.CreateDirectory(_profilesDirectory);
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to create profiles directory at '{_profilesDirectory}': {ex.Message}");
        }
    }

    /// <summary>
    /// Computes a safe file path for a profile given its display name.
    /// </summary>
    public string GetProfileFilePath(string profileName)
    {
        string safeName = SanitizeFileName(profileName);
        return Path.Combine(_profilesDirectory, $"{safeName}.json");
    }

    /// <summary>
    /// Atomically saves a profile to disk using write-to-temp and file replacement.
    /// </summary>
    public void SaveProfile(ProfileModel profile, string? targetFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new ArgumentException("Profile name cannot be empty.", nameof(profile));

        if (string.IsNullOrWhiteSpace(profile.Version))
            profile.Version = ProfileModel.CurrentSchemaVersion;

        profile.UpdatedAt = DateTime.UtcNow;

        string finalPath = targetFilePath ?? GetProfileFilePath(profile.Name);
        string dir = Path.GetDirectoryName(finalPath) ?? _profilesDirectory;
        Directory.CreateDirectory(dir);

        string tempPath = Path.Combine(dir, $"{Guid.NewGuid():N}.tmp");
        string json = JsonSerializer.Serialize(profile, JsonOptions);

        try
        {
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(fs, Encoding.UTF8))
            {
                writer.Write(json);
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }

            File.Move(tempPath, finalPath, overwrite: true);
            _logger?.Info($"Profile '{profile.Name}' saved atomically to '{finalPath}'");
        }
        catch (Exception ex)
        {
            _logger?.Error($"Failed to save profile '{profile.Name}' to '{finalPath}': {ex.Message}", ex);
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
                // Ignore cleanup error
            }
            throw;
        }
    }

    /// <summary>
    /// Loads and deserializes a profile from disk.
    /// </summary>
    public ProfileModel LoadProfile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Profile file does not exist: {filePath}", filePath);

        string json = File.ReadAllText(filePath, Encoding.UTF8);
        var profile = JsonSerializer.Deserialize<ProfileModel>(json, JsonOptions);

        if (profile == null)
            throw new InvalidDataException($"Profile at '{filePath}' parsed to null.");

        if (string.IsNullOrWhiteSpace(profile.Version))
            throw new InvalidDataException($"Profile at '{filePath}' is missing the schema version.");

        if (!ProfileModel.IsVersionSupported(profile.Version))
            throw new NotSupportedException($"Profile schema version '{profile.Version}' is not supported. Supported versions: {string.Join(", ", ProfileModel.SupportedVersions)}.");

        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new InvalidDataException($"Profile at '{filePath}' is missing a required name.");

        _logger?.Info($"Profile '{profile.Name}' loaded from '{filePath}' (Version: {profile.Version}, Mode: {profile.Mode})");
        return profile;
    }

    /// <summary>
    /// Loads a profile by its name.
    /// </summary>
    public ProfileModel LoadProfileByName(string profileName)
    {
        string path = GetProfileFilePath(profileName);
        return LoadProfile(path);
    }

    /// <summary>
    /// Lists summary headers for all saved profiles in the storage directory.
    /// Skips and logs corrupt or unreadable files without failing the entire enumeration.
    /// </summary>
    public IReadOnlyList<ProfileHeader> ListProfiles()
    {
        var headers = new List<ProfileHeader>();

        if (!Directory.Exists(_profilesDirectory))
            return headers;

        string[] files;
        try
        {
            files = Directory.GetFiles(_profilesDirectory, "*.json");
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Failed to list profile files from '{_profilesDirectory}': {ex.Message}");
            return headers;
        }

        foreach (var file in files)
        {
            try
            {
                var profile = LoadProfile(file);
                string targetDesc = profile.Target != null
                    ? $"{profile.Target.ProcessName} ({profile.Target.WindowTitle ?? "*"})"
                    : "[No Target]";

                int itemCount = profile.Mode == ProfileMode.Simple
                    ? profile.ClickPoints.Count
                    : profile.MacroActions.Count;

                headers.Add(new ProfileHeader(
                    profile.Name,
                    file,
                    profile.Mode,
                    profile.UpdatedAt,
                    targetDesc,
                    itemCount));
            }
            catch (Exception ex)
            {
                _logger?.Warning($"Skipping corrupt or incompatible profile '{file}': {ex.Message}");
            }
        }

        return headers.OrderByDescending(h => h.UpdatedAt).ToList();
    }

    /// <summary>
    /// Deletes a profile by its file path or name.
    /// </summary>
    public bool DeleteProfile(string nameOrPath)
    {
        string targetPath = File.Exists(nameOrPath) ? nameOrPath : GetProfileFilePath(nameOrPath);

        try
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
                _logger?.Info($"Profile at '{targetPath}' deleted.");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.Warning($"Failed to delete profile at '{targetPath}': {ex.Message}");
        }

        return false;
    }

    private static string SanitizeFileName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            sb.Append(invalidChars.Contains(c) ? '_' : c);
        }

        string sanitized = sb.ToString().Trim();
        return string.IsNullOrEmpty(sanitized) ? "UnnamedProfile" : sanitized;
    }
}
