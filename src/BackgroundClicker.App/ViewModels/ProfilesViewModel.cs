using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BackgroundClicker.App.Models;
using BackgroundClicker.Core.Clicking;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Macro;
using BackgroundClicker.Core.Profiles;
using BackgroundClicker.Core.Targeting;

namespace BackgroundClicker.App.ViewModels;

public sealed partial class ProfilesViewModel : ObservableObject
{
    private readonly ProfileStorageService _profileStorage;
    private readonly TargetResolver _targetResolver;
    private readonly IAppLogger _logger;
    private readonly Action<WindowTarget> _onTargetCommitted;
    private readonly Func<WindowTarget?> _getCurrentTarget;
    private readonly Action<List<ClickPoint>, ClickRunnerSettingsConfig?> _loadSimpleProfile;
    private readonly Action<List<IMacroAction>> _loadMacroProfile;
    private readonly Func<List<ClickPoint>> _getSimplePoints;
    private readonly Func<ClickRunnerSettingsConfig> _getSimpleSettings;
    private readonly Func<List<IMacroAction>> _getMacroActions;

    public ObservableCollection<ProfileListItem> Profiles { get; } = new();

    [ObservableProperty]
    private ProfileListItem? _selectedProfile;

    [ObservableProperty]
    private string _profileName = "DefaultProfile";

    [ObservableProperty]
    private int _selectedModeIndex = 0; // 0: Simple Mode, 1: Macro Mode

    [ObservableProperty]
    private string _targetDescriptorText = "Target Descriptor: [No target selected]";

    [ObservableProperty]
    private TargetDescriptor? _activeProfileTarget;

    public ProfilesViewModel(
        ProfileStorageService profileStorage,
        TargetResolver targetResolver,
        IAppLogger logger,
        Action<WindowTarget> onTargetCommitted,
        Func<WindowTarget?> getCurrentTarget,
        Action<List<ClickPoint>, ClickRunnerSettingsConfig?> loadSimpleProfile,
        Action<List<IMacroAction>> loadMacroProfile,
        Func<List<ClickPoint>> getSimplePoints,
        Func<ClickRunnerSettingsConfig> getSimpleSettings,
        Func<List<IMacroAction>> getMacroActions)
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
            TargetDescriptorText = $"Target: {ActiveProfileTarget.ProcessName} (Title: '{ActiveProfileTarget.WindowTitle ?? "*"}' Mode: {ActiveProfileTarget.MatchMode})";
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
            if (profile.Target != null)
            {
                TargetDescriptorText = $"Target: {profile.Target.ProcessName} (Title: '{profile.Target.WindowTitle ?? "*"}' Mode: {profile.Target.MatchMode})";
            }
            else
            {
                TargetDescriptorText = "Target Descriptor: [No target in profile]";
            }
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
            Profiles.Clear();
            foreach (var h in headers)
            {
                Profiles.Add(new ProfileListItem(h));
            }
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
            Mode = mode
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
        }

        try
        {
            _profileStorage.SaveProfile(profile);
            ActiveProfileTarget = profile.Target;
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
    public void LoadSelectedProfile()
    {
        if (SelectedProfile == null)
            return;

        string name = SelectedProfile.Name;
        try
        {
            var profile = _profileStorage.LoadProfileByName(name);
            ProfileName = profile.Name;
            SelectedModeIndex = profile.Mode == ProfileMode.Macro ? 1 : 0;
            ActiveProfileTarget = profile.Target;

            if (profile.Target != null)
            {
                TargetDescriptorText = $"Target: {profile.Target.ProcessName} (Title: '{profile.Target.WindowTitle ?? "*"}' Mode: {profile.Target.MatchMode})";

                // Attempt automatic re-resolution
                var res = _targetResolver.Resolve(profile.Target);
                if (res.IsSuccess && res.Target != null)
                {
                    _onTargetCommitted(res.Target);
                    _logger.Info($"Re-resolved profile target to HWND {HwndFormatter.Format(res.Target.TargetHwnd)}");
                }
                else if (res.Status == TargetResolutionStatus.Ambiguous)
                {
                    _logger.Warning($"Target re-resolution ambiguous: {res.Candidates.Count} matching windows found.");
                    MessageBox.Show($"Multiple matching windows ({res.Candidates.Count}) found for process '{profile.Target.ProcessName}'. Please select the specific window in Target Inspector.",
                        "Ambiguous Target", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    _logger.Warning($"Target re-resolution: {res.Message}");
                    TargetDescriptorText = $"Target not found: {profile.Target.ProcessName}";
                }
            }
            else
            {
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
                _loadMacroProfile(actions);
            }

            _logger.Info($"Profile '{profile.Name}' loaded successfully.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load profile '{name}'", ex);
            MessageBox.Show($"Failed to load profile: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void ReResolveTarget()
    {
        var currentTarget = _getCurrentTarget();
        if (ActiveProfileTarget == null && currentTarget != null)
        {
            ActiveProfileTarget = TargetDescriptor.FromWindowTarget(currentTarget, TitleMatchMode.Contains);
        }

        if (ActiveProfileTarget == null)
        {
            MessageBox.Show("No target descriptor is currently loaded or configured.", "Re-resolve Target", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var res = _targetResolver.Resolve(ActiveProfileTarget);
        if (res.IsSuccess && res.Target != null)
        {
            _onTargetCommitted(res.Target);
            _logger.Info($"Target re-resolved successfully to HWND {HwndFormatter.Format(res.Target.TargetHwnd)}");
            MessageBox.Show($"Target re-resolved successfully to HWND {HwndFormatter.Format(res.Target.TargetHwnd)} ({res.Target.ProcessName})",
                "Re-resolve Target", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else if (res.Status == TargetResolutionStatus.Ambiguous)
        {
            MessageBox.Show($"Found {res.Candidates.Count} matching windows. Ambiguity must be resolved manually via Target Inspector.",
                "Ambiguous Target", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show($"Target not found: {res.Message}", "Target Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
            RefreshProfiles();
            _logger.Info($"Profile '{name}' deleted.");
        }
    }
}
