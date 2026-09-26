using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BackgroundAutomator.App.Models;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Profiles;
using BackgroundAutomator.Core.Targeting;

namespace BackgroundAutomator.App.ViewModels;

public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly ProfileStorageService _profileStorage;
    private readonly TargetResolver _targetResolver;
    private readonly IAppLogger _logger;
    private readonly Action<WindowTarget?> _onTargetCommitted;
    private readonly Func<WindowTarget?> _getCurrentTarget;
    private readonly Action<List<ClickPoint>, ClickRunnerSettingsConfig?> _loadSimpleProfile;
    private readonly Action<List<IMacroAction>, MacroRunnerSettingsConfig?, string?> _loadMacroProfile;
    private readonly Func<List<ClickPoint>> _getSimplePoints;
    private readonly Func<ClickRunnerSettingsConfig> _getSimpleSettings;
    private readonly Func<List<IMacroAction>> _getMacroActions;
    private readonly Func<MacroRunnerSettingsConfig>? _getMacroSettings;
    private readonly Func<bool>? _getIsDirty;
    private readonly Action? _markClean;

    public ObservableCollection<ProfileListItem> Profiles { get; } = new();

    [ObservableProperty]
    private ProfileListItem? _selectedProfile;

    [ObservableProperty]
    private string _profileName = "DefaultProfile";

    [ObservableProperty]
    private string? _currentLoadedProfileName;

    partial void OnCurrentLoadedProfileNameChanged(string? value) => OnPropertyChanged(nameof(LoadedProfileDisplay));

    public string LoadedProfileDisplay => string.IsNullOrEmpty(CurrentLoadedProfileName)
        ? "Loaded Profile: [None]"
        : $"Loaded Profile: {CurrentLoadedProfileName}";

    [ObservableProperty]
    private int _selectedModeIndex = 0; // 0: Simple Mode, 1: Macro Mode

    [ObservableProperty]
    private string _savedTargetText = "Saved Target: [No target loaded]";

    [ObservableProperty]
    private string _runtimeTargetText = "Runtime Target: None";

    [ObservableProperty]
    private string _targetDescriptorText = "Saved Target: [No target loaded]";

    [ObservableProperty]
    private TargetDescriptor? _activeProfileTarget;

    [ObservableProperty]
    private bool _isStartupProfile;

    public bool HasUnsavedChanges => _getIsDirty?.Invoke() ?? false;

    public ProfilesViewModel(
        ProfileStorageService profileStorage,
        TargetResolver targetResolver,
        IAppLogger logger,
        Action<WindowTarget?> onTargetCommitted,
        Func<WindowTarget?> getCurrentTarget,
        Action<List<ClickPoint>, ClickRunnerSettingsConfig?> loadSimpleProfile,
        Action<List<IMacroAction>, MacroRunnerSettingsConfig?, string?> loadMacroProfile,
        Func<List<ClickPoint>> getSimplePoints,
        Func<ClickRunnerSettingsConfig> getSimpleSettings,
        Func<List<IMacroAction>> getMacroActions,
        Func<MacroRunnerSettingsConfig>? getMacroSettings = null,
        Func<bool>? getIsDirty = null,
        Action? markClean = null)
    {
        _profileStorage = profileStorage;
        _targetResolver = targetResolver;
        _logger = logger;
        _onTargetCommitted = onTargetCommitted;
        _getCurrentTarget = getCurrentTarget;
        _loadSimpleProfile = loadSimpleProfile;
        _loadMacroProfile = loadMacroProfile;
        _getSimplePoints = getSimplePoints;
        _getSimpleSettings = getSimpleSettings;
        _getMacroActions = getMacroActions;
        _getMacroSettings = getMacroSettings;
        _getIsDirty = getIsDirty;
        _markClean = markClean;
    }

    public void Initialize()
    {
        RefreshProfiles();
    }

    public void SetCurrentTarget(WindowTarget? target)
    {
        if (target != null && target.IsWindowValid())
        {
            ActiveProfileTarget = TargetDescriptor.FromWindowTarget(target, TitleMatchMode.Contains);
            SavedTargetText = $"Saved Target: {ActiveProfileTarget.ProcessName} (Title: '{ActiveProfileTarget.WindowTitle ?? "*"}' Mode: {ActiveProfileTarget.MatchMode})";
            RuntimeTargetText = $"Runtime Target: Resolved — {target.ProcessName} ({HwndFormatter.FormatShort(target.TargetHwnd)})";
            TargetDescriptorText = $"{SavedTargetText}\n{RuntimeTargetText}";
        }
        else
        {
            RuntimeTargetText = "Runtime Target: None";
            TargetDescriptorText = $"{SavedTargetText}\n{RuntimeTargetText}";
        }
    }

    partial void OnSelectedProfileChanged(ProfileListItem? value)
    {
        if (value == null)
            return;

        ProfileName = value.Name;
        try
        {
            var profile = _profileStorage.LoadProfileByName(value.Name);
            SelectedModeIndex = profile.Mode == ProfileMode.Macro ? 1 : 0;
            IsStartupProfile = string.Equals(value.Name, _profileStorage.GetStartupProfileName(), StringComparison.OrdinalIgnoreCase);

            if (profile.Target != null)
            {
                SavedTargetText = $"Saved Target: {profile.Target.ProcessName} (Title: '{profile.Target.WindowTitle ?? "*"}' Mode: {profile.Target.MatchMode})";
            }
            else
            {
                SavedTargetText = "Saved Target: [No target in profile]";
            }
            TargetDescriptorText = $"{SavedTargetText}\n{RuntimeTargetText}";
        }
        catch
        {
            // Ignore preview read errors
        }
    }

    [RelayCommand]
    public void RefreshProfiles()
    {
        try
        {
            var headers = _profileStorage.ListProfiles();
            string? startupName = _profileStorage.GetStartupProfileName();

            Profiles.Clear();
            foreach (var h in headers)
            {
                bool isStartup = string.Equals(h.Name, startupName, StringComparison.OrdinalIgnoreCase);
                Profiles.Add(new ProfileListItem(h, isStartup));
            }

            IsStartupProfile = string.Equals(ProfileName, startupName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to refresh profile list", ex);
        }
    }

    [RelayCommand]
    public void SaveProfile()
    {
        string name = ProfileName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter a valid profile name.", "Save Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var mode = SelectedModeIndex == 1 ? ProfileMode.Macro : ProfileMode.Simple;
        var profile = new ProfileModel
        {
            Name = name,
            Mode = mode,
            IsStartupProfile = IsStartupProfile
        };

        var currentTarget = _getCurrentTarget();
        if (currentTarget != null && currentTarget.IsWindowValid())
        {
            profile.Target = TargetDescriptor.FromWindowTarget(currentTarget, TitleMatchMode.Contains);
        }
        else if (ActiveProfileTarget != null)
        {
            profile.Target = ActiveProfileTarget;
        }

        if (mode == ProfileMode.Simple)
        {
            profile.SimpleSettings = _getSimpleSettings();
            profile.ClickPoints = _getSimplePoints().Select(p => ClickPointConfig.FromClickPoint(p)).ToList();
        }
        else
        {
            profile.MacroActions = _getMacroActions().Select(MacroActionConfig.FromMacroAction).ToList();
            profile.MacroSettings = _getMacroSettings?.Invoke();
        }

        try
        {
            _profileStorage.SaveProfile(profile);
            _profileStorage.SetLastUsedProfileName(name);

            if (IsStartupProfile)
            {
                _profileStorage.SetStartupProfileName(name);
            }
            else if (string.Equals(_profileStorage.GetStartupProfileName(), name, StringComparison.OrdinalIgnoreCase))
            {
                _profileStorage.SetStartupProfileName(null);
            }

            ActiveProfileTarget = profile.Target;
            _markClean?.Invoke();
            RefreshProfiles();
            _logger.Info($"Profile '{name}' saved successfully.");
            MessageBox.Show($"Profile '{name}' saved successfully.", "Save Profile", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save profile '{name}'", ex);
            MessageBox.Show($"Failed to save profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void ToggleStartupProfile()
    {
        string name = ProfileName.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return;

        string? currentStartup = _profileStorage.GetStartupProfileName();
        if (string.Equals(currentStartup, name, StringComparison.OrdinalIgnoreCase))
        {
            _profileStorage.SetStartupProfileName(null);
            IsStartupProfile = false;
            _logger.Info($"Startup profile cleared (was '{name}').");
            MessageBox.Show($"Startup profile cleared.", "Startup Profile", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            _profileStorage.SetStartupProfileName(name);
            IsStartupProfile = true;
            _logger.Info($"Profile '{name}' set as startup profile.");
            MessageBox.Show($"Profile '{name}' set as startup profile.", "Startup Profile", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        RefreshProfiles();
    }

    [RelayCommand]
    public void LoadSelectedProfile()
    {
        if (SelectedProfile == null)
            return;

        LoadProfileByName(SelectedProfile.Name, showDialogs: true);
    }

    public void LoadProfileByNameSilently(string name)
    {
        LoadProfileByName(name, showDialogs: false);
    }

    private void LoadProfileByName(string name, bool showDialogs)
    {
        try
        {
            var profile = _profileStorage.LoadProfileByName(name);
            ProfileName = profile.Name;
            CurrentLoadedProfileName = profile.Name;
            SelectedModeIndex = profile.Mode == ProfileMode.Macro ? 1 : 0;
            ActiveProfileTarget = profile.Target;
            string? startupName = _profileStorage.GetStartupProfileName();
            IsStartupProfile = string.Equals(name, startupName, StringComparison.OrdinalIgnoreCase);

            _profileStorage.SetLastUsedProfileName(name);

            if (profile.Target != null)
            {
                SavedTargetText = $"Saved Target: {profile.Target.ProcessName} (Title: '{profile.Target.WindowTitle ?? "*"}' Mode: {profile.Target.MatchMode})";

                // Section 20: If currently selected live target is compatible, reuse it
                var currentLiveTarget = _getCurrentTarget();
                if (currentLiveTarget != null && currentLiveTarget.IsWindowValid() && IsTargetCompatible(currentLiveTarget, profile.Target))
                {
                    _onTargetCommitted(currentLiveTarget);
                    RuntimeTargetText = $"Runtime Target: Resolved — {currentLiveTarget.ProcessName} ({HwndFormatter.FormatShort(currentLiveTarget.TargetHwnd)})";
                    TargetDescriptorText = $"{SavedTargetText}\n{RuntimeTargetText}";
                    _logger.Info($"Reused existing live target HWND {HwndFormatter.Format(currentLiveTarget.TargetHwnd)} for profile '{profile.Name}'");
                }
                else
                {
                    var res = _targetResolver.Resolve(profile.Target);
                    if (res.IsSuccess && res.Target != null)
                    {
                        _onTargetCommitted(res.Target);
                        RuntimeTargetText = $"Runtime Target: Resolved — {res.Target.ProcessName} ({HwndFormatter.FormatShort(res.Target.TargetHwnd)})";
                        TargetDescriptorText = $"{SavedTargetText}\n{RuntimeTargetText}";
                        _logger.Info($"Re-resolved profile target to HWND {HwndFormatter.Format(res.Target.TargetHwnd)}");
                    }
                    else if (res.Status == TargetResolutionStatus.Ambiguous)
                    {
                        _onTargetCommitted(null);
                        RuntimeTargetText = $"Runtime Target: Ambiguous — {res.Candidates.Count} matching windows found";
                        TargetDescriptorText = $"{SavedTargetText}\n{RuntimeTargetText}";
                        _logger.Warning($"Target re-resolution ambiguous: {res.Candidates.Count} matching windows found.");
                        if (showDialogs)
                        {
                            MessageBox.Show($"Multiple matching windows ({res.Candidates.Count}) found for process '{profile.Target.ProcessName}'. Please select the specific window in Target Inspector.",
                                "Ambiguous Target", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        _onTargetCommitted(null);
                        RuntimeTargetText = $"Runtime Target: Not found ({profile.Target.ProcessName})";
                        TargetDescriptorText = $"{SavedTargetText}\n{RuntimeTargetText}";
                        _logger.Warning($"Target re-resolution: {res.Message}");
                    }
                }
            }
            else
            {
                _onTargetCommitted(null);
                SavedTargetText = "Saved Target: [No target in profile]";
                RuntimeTargetText = "Runtime Target: None";
                TargetDescriptorText = "Target Descriptor: [None]";
            }

            if (profile.Mode == ProfileMode.Simple)
            {
                var targetHwnd = _getCurrentTarget()?.TargetHwnd ?? IntPtr.Zero;
                var pts = profile.ClickPoints.Select(p => p.ToClickPoint(targetHwnd)).ToList();
                _loadSimpleProfile(pts, profile.SimpleSettings);
            }
            else
            {
                var actions = profile.MacroActions.Select(a => a.ToMacroAction()).ToList();
                _loadMacroProfile(actions, profile.MacroSettings, profile.Name);
            }

            _markClean?.Invoke();
            _logger.Info($"Profile '{profile.Name}' loaded successfully (Auto-run not triggered).");
            if (showDialogs)
            {
                MessageBox.Show($"Loaded profile: {profile.Name}", "Profile Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load profile '{name}'", ex);
            if (showDialogs)
            {
                MessageBox.Show($"Failed to load profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    public void ReResolveTarget() => ReResolveTarget(showDialogs: true);

    public void ReResolveTargetSilently() => ReResolveTarget(showDialogs: false);

    public void ReResolveTarget(bool showDialogs)
    {
        var currentTarget = _getCurrentTarget();
        if (ActiveProfileTarget == null && currentTarget != null)
        {
            ActiveProfileTarget = TargetDescriptor.FromWindowTarget(currentTarget, TitleMatchMode.Contains);
        }

        if (ActiveProfileTarget == null)
        {
            if (showDialogs)
            {
                MessageBox.Show("No target descriptor is currently loaded or configured.", "Re-resolve Target", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            return;
        }

        SavedTargetText = $"Saved Target: {ActiveProfileTarget.ProcessName} (Title: '{ActiveProfileTarget.WindowTitle ?? "*"}' Mode: {ActiveProfileTarget.MatchMode})";

        var res = _targetResolver.Resolve(ActiveProfileTarget);
        if (res.IsSuccess && res.Target != null)
        {
            _onTargetCommitted(res.Target);
            RuntimeTargetText = $"Runtime Target: Resolved — {res.Target.ProcessName} ({HwndFormatter.FormatShort(res.Target.TargetHwnd)})";
            TargetDescriptorText = $"{SavedTargetText}\n{RuntimeTargetText}";
            _logger.Info($"Target re-resolved successfully to HWND {HwndFormatter.Format(res.Target.TargetHwnd)}");
            if (showDialogs)
            {
                MessageBox.Show($"Target re-resolved successfully to HWND {HwndFormatter.Format(res.Target.TargetHwnd)} ({res.Target.ProcessName})",
                    "Re-resolve Target", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        else if (res.Status == TargetResolutionStatus.Ambiguous)
        {
            _onTargetCommitted(null);
            RuntimeTargetText = $"Runtime Target: Ambiguous — {res.Candidates.Count} matching windows found";
            TargetDescriptorText = $"{SavedTargetText}\n{RuntimeTargetText}";
            _logger.Warning($"Target re-resolution ambiguous: {res.Candidates.Count} matching windows found.");
            if (showDialogs)
            {
                MessageBox.Show($"Found {res.Candidates.Count} matching windows. Ambiguity must be resolved manually via Target Inspector.",
                    "Ambiguous Target", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        else
        {
            _onTargetCommitted(null);
            RuntimeTargetText = $"Runtime Target: Not found ({ActiveProfileTarget.ProcessName})";
            TargetDescriptorText = $"{SavedTargetText}\n{RuntimeTargetText}";
            _logger.Warning($"Target re-resolution: {res.Message}");
            if (showDialogs)
            {
                MessageBox.Show($"Target not found: {res.Message}", "Target Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    public static bool IsTargetCompatible(WindowTarget target, TargetDescriptor descriptor)
    {
        if (target == null || descriptor == null)
            return false;

        if (!string.IsNullOrWhiteSpace(descriptor.ProcessName))
        {
            string tProc = NormalizeProcessName(target.ProcessName);
            string dProc = NormalizeProcessName(descriptor.ProcessName);
            if (!string.Equals(tProc, dProc, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (!string.IsNullOrWhiteSpace(descriptor.WindowClass))
        {
            if (!string.Equals(target.WindowClass, descriptor.WindowClass, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (descriptor.MatchMode != TitleMatchMode.Any && !string.IsNullOrEmpty(descriptor.WindowTitle))
        {
            bool titleMatches = descriptor.MatchMode switch
            {
                TitleMatchMode.Exact => string.Equals(target.WindowTitle, descriptor.WindowTitle, StringComparison.Ordinal),
                TitleMatchMode.Contains => target.WindowTitle.Contains(descriptor.WindowTitle, StringComparison.OrdinalIgnoreCase),
                TitleMatchMode.StartsWith => target.WindowTitle.StartsWith(descriptor.WindowTitle, StringComparison.OrdinalIgnoreCase),
                _ => true
            };
            if (!titleMatches)
                return false;
        }

        if (descriptor.ChildDescriptor != null)
        {
            if (!string.IsNullOrWhiteSpace(descriptor.ChildDescriptor.ControlClass) &&
                !string.Equals(target.WindowClass, descriptor.ChildDescriptor.ControlClass, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeProcessName(string procName)
    {
        if (procName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return procName[..^4];
        return procName;
    }

    [RelayCommand]
    public void DeleteSelectedProfile()
    {
        if (SelectedProfile == null)
            return;

        string name = SelectedProfile.Name;
        if (MessageBox.Show($"Are you sure you want to delete profile '{name}'?",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _profileStorage.DeleteProfile(name);
            if (string.Equals(_profileStorage.GetStartupProfileName(), name, StringComparison.OrdinalIgnoreCase))
            {
                _profileStorage.SetStartupProfileName(null);
            }
            if (string.Equals(_profileStorage.GetLastUsedProfileName(), name, StringComparison.OrdinalIgnoreCase))
            {
                _profileStorage.SetLastUsedProfileName(null);
            }
            RefreshProfiles();
            _logger.Info($"Profile '{name}' deleted.");
        }
    }
}
