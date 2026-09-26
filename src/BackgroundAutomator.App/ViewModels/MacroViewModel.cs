using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BackgroundAutomator.App.Models;
using BackgroundAutomator.Core.Capture;
using BackgroundAutomator.Core.Clicking;
using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Core.Macro;
using BackgroundAutomator.Core.Approval;
using BackgroundAutomator.Core.Profiles;
using BackgroundAutomator.App.Services;
using BackgroundAutomator.Core.TextDetection;

namespace BackgroundAutomator.App.ViewModels;

public sealed partial class MacroViewModel : ObservableObject
{
    private readonly MacroRunner _macroRunner;
    private readonly BackgroundClickerEngine _clicker;
    private readonly GdiWindowCaptureService _captureService;
    private readonly IBackgroundKeyboard _keyboard;
    private readonly IAppLogger _logger;
    private readonly Action<MacroRunnerState, string> _onMacroStateChanged;
    private readonly IDialogService _dialogService;
    private readonly ITargetValidator _targetValidator;

    private WindowTarget? _currentTarget;

    public ObservableCollection<MacroActionItem> Actions { get; } = new();

    public IReadOnlyList<BackgroundKey> AvailableKeys { get; } = Enum.GetValues<BackgroundKey>();

    public IReadOnlyList<TextMatchMode> AvailableMatchModes { get; } = Enum.GetValues<TextMatchMode>();

    [ObservableProperty]
    private BackgroundKey _selectedKey = BackgroundKey.Enter;

    [ObservableProperty]
    private MacroActionItem? _selectedAction;

    [ObservableProperty]
    private string _statusText = "State: IDLE | Actions: 0";

    [ObservableProperty]
    private string _statusColor = "#666666";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _canRun = true;

    [ObservableProperty]
    private bool _canStop = false;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private string? _currentProfileName;

    partial void OnCurrentProfileNameChanged(string? value) => OnPropertyChanged(nameof(CurrentProfileDisplay));
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(CurrentProfileDisplay));

    public string CurrentProfileDisplay => string.IsNullOrEmpty(CurrentProfileName) ? "[Default Profile]" : (IsDirty ? $"{CurrentProfileName} *" : CurrentProfileName);

    [ObservableProperty]
    private bool _isEditingAction;

    [ObservableProperty]
    private MacroActionItem? _editingActionItem;

    partial void OnIsEditingActionChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotEditingAction));
        OnPropertyChanged(nameof(BuilderHeader));
    }

    public bool IsNotEditingAction => !IsEditingAction;

    public string BuilderHeader => IsEditingAction
        ? $"Edit Action #{EditingActionItem?.Index}: {EditingActionItem?.Name}"
        : "+ Add Macro Action";

    // Safe Auto Confirm focus behavior
    public IReadOnlyList<FocusBehavior> AvailableFocusBehaviors { get; } = Enum.GetValues<FocusBehavior>();

    [ObservableProperty]
    private FocusBehavior _autoConfirmFocusBehavior = FocusBehavior.FastPulse;

    // Safe Auto Confirm Approval Policy (ExactRules vs FOOL MODE)
    [ObservableProperty]
    private ApprovalPolicyMode _autoConfirmPolicyMode = ApprovalPolicyMode.ExactRules;

    public bool IsExactRulesMode
    {
        get => AutoConfirmPolicyMode == ApprovalPolicyMode.ExactRules;
        set
        {
            if (value && AutoConfirmPolicyMode != ApprovalPolicyMode.ExactRules)
            {
                AutoConfirmPolicyMode = ApprovalPolicyMode.ExactRules;
            }
        }
    }

    public bool IsFoolMode
    {
        get => AutoConfirmPolicyMode == ApprovalPolicyMode.FoolMode;
        set
        {
            if (value && AutoConfirmPolicyMode != ApprovalPolicyMode.FoolMode)
            {
                AutoConfirmPolicyMode = ApprovalPolicyMode.FoolMode;
            }
        }
    }

    partial void OnAutoConfirmPolicyModeChanged(ApprovalPolicyMode value)
    {
        OnPropertyChanged(nameof(IsExactRulesMode));
        OnPropertyChanged(nameof(IsFoolMode));
        IsDirty = true;
    }

    [ObservableProperty]
    private bool _isFoolModeActive;

    // Macro Repeat Configuration
    public IReadOnlyList<MacroRepeatMode> AvailableRepeatModes { get; } = Enum.GetValues<MacroRepeatMode>();

    [ObservableProperty]
    private MacroRepeatMode _repeatMode = MacroRepeatMode.Once;

    [ObservableProperty]
    private int _repeatCount = 1;

    [ObservableProperty]
    private int _cycleDelayMilliseconds = 500;

    partial void OnRepeatModeChanged(MacroRepeatMode value)
    {
        OnPropertyChanged(nameof(IsRepeatCountVisible));
        OnPropertyChanged(nameof(IsCycleDelayVisible));
        IsDirty = true;
    }

    partial void OnRepeatCountChanged(int value) => IsDirty = true;
    partial void OnCycleDelayMillisecondsChanged(int value) => IsDirty = true;

    public bool IsRepeatCountVisible => RepeatMode == MacroRepeatMode.Count;
    public bool IsCycleDelayVisible => RepeatMode != MacroRepeatMode.Once;

    // Action builder configuration fields
    [ObservableProperty]
    private int _selectedActionCategoryIndex = 0; // 0: Mouse, 1: Keyboard, 2: Timing, 3: Wait Color, 4: Wait Text, 5: Safe Auto Confirm

    [ObservableProperty]
    private int _actionX = 50;

    [ObservableProperty]
    private int _actionY = 50;

    [ObservableProperty]
    private int _delayMilliseconds = 500;

    [ObservableProperty]
    private string _waitColorHex = "#00FF00";

    [ObservableProperty]
    private int _waitColorTol = 5;

    [ObservableProperty]
    private int _waitColorTimeoutMs = 5000;

    [ObservableProperty]
    private string _waitForTextExpected = "Run this command?";

    [ObservableProperty]
    private TextMatchMode _waitForTextMatchMode = TextMatchMode.Contains;

    [ObservableProperty]
    private int _waitForTextPollIntervalMs = 500;

    [ObservableProperty]
    private int _waitForTextTimeoutMs = 60000;

    // Safe Auto Confirm configuration fields
    public IReadOnlyList<AutoConfirmExecutionMode> AvailableExecutionModes { get; } = Enum.GetValues<AutoConfirmExecutionMode>();
    public IReadOnlyList<AutoConfirmWaitMode> AvailableWaitModes { get; } = Enum.GetValues<AutoConfirmWaitMode>();

    [ObservableProperty]
    private string _autoConfirmRuleName = "Approve BackgroundAutomator tests";

    [ObservableProperty]
    private string _autoConfirmProcess = "WindowsTerminal.exe";

    [ObservableProperty]
    private string _autoConfirmPrompt = "Run this command?";

    [ObservableProperty]
    private string _autoConfirmSelectedOption = "Yes, run command";

    [ObservableProperty]
    private string _autoConfirmAllowedCommand = "dotnet test BackgroundAutomator.sln";

    [ObservableProperty]
    private AutoConfirmExecutionMode _autoConfirmExecutionMode = AutoConfirmExecutionMode.ObserveOnly;

    [ObservableProperty]
    private AutoConfirmWaitMode _autoConfirmWaitMode = AutoConfirmWaitMode.FixedTimeout;

    [ObservableProperty]
    private int _autoConfirmPollIntervalMs = 500;

    [ObservableProperty]
    private int _autoConfirmTimeoutMs = 60000;

    // Multi-Command Rule Set UI collection
    public ObservableCollection<CommandApprovalRuleItem> AutoConfirmRules { get; } = new();

    [ObservableProperty]
    private string _newRuleName = string.Empty;

    [ObservableProperty]
    private string _newRuleCommand = string.Empty;

    partial void OnAutoConfirmWaitModeChanged(AutoConfirmWaitMode value)
    {
        OnPropertyChanged(nameof(IsAutoConfirmTimeoutEnabled));
        OnPropertyChanged(nameof(AutoConfirmTimeoutDisplay));
    }

    public bool IsAutoConfirmTimeoutEnabled => AutoConfirmWaitMode == AutoConfirmWaitMode.FixedTimeout;

    partial void OnAutoConfirmPollIntervalMsChanged(int value)
    {
        OnPropertyChanged(nameof(AutoConfirmPollIntervalDisplay));
    }

    partial void OnAutoConfirmTimeoutMsChanged(int value)
    {
        OnPropertyChanged(nameof(AutoConfirmTimeoutDisplay));
    }

    public string AutoConfirmPollIntervalDisplay => $"{AutoConfirmPollIntervalMs} ms";

    public string AutoConfirmTimeoutDisplay =>
        AutoConfirmWaitMode == AutoConfirmWaitMode.Indefinite
            ? "Indefinite"
            : (AutoConfirmTimeoutMs >= 1000 && AutoConfirmTimeoutMs % 1000 == 0
                ? $"{AutoConfirmTimeoutMs / 1000} sec"
                : $"{AutoConfirmTimeoutMs / 1000.0:0.#} sec");

    public MacroViewModel(
        MacroRunner macroRunner,
        BackgroundClickerEngine clicker,
        GdiWindowCaptureService captureService,
        IBackgroundKeyboard keyboard,
        IAppLogger logger,
        Action<MacroRunnerState, string> onMacroStateChanged)
        : this(macroRunner, clicker, captureService, keyboard, logger, onMacroStateChanged, new WpfDialogService(), new Win32TargetValidator())
    {
    }

    public MacroViewModel(
        MacroRunner macroRunner,
        BackgroundClickerEngine clicker,
        GdiWindowCaptureService captureService,
        IBackgroundKeyboard keyboard,
        IAppLogger logger,
        Action<MacroRunnerState, string> onMacroStateChanged,
        IDialogService dialogService,
        ITargetValidator targetValidator)
    {
        _macroRunner = macroRunner;
        _clicker = clicker;
        _captureService = captureService;
        _keyboard = keyboard ?? new BackgroundKeyboardEngine(logger);
        _logger = logger;
        _onMacroStateChanged = onMacroStateChanged;
        _dialogService = dialogService ?? new WpfDialogService();
        _targetValidator = targetValidator ?? new Win32TargetValidator();

        WireEvents();
    }

    public MacroViewModel(
        MacroRunner macroRunner,
        BackgroundClickerEngine clicker,
        GdiWindowCaptureService captureService,
        IAppLogger logger,
        Action<MacroRunnerState, string> onMacroStateChanged)
        : this(macroRunner, clicker, captureService, new BackgroundKeyboardEngine(logger), logger, onMacroStateChanged, new WpfDialogService(), new Win32TargetValidator())
    {
    }

    public void SetCurrentTarget(WindowTarget? target)
    {
        _currentTarget = target;
        if (target != null && _targetValidator.IsValid(target))
        {
            ActionX = Math.Clamp(target.ClientPoint.ClientX, 0, 9999);
            ActionY = Math.Clamp(target.ClientPoint.ClientY, 0, 9999);
        }
    }

    private void WireEvents()
    {
        _macroRunner.StateChanged += state =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                switch (state)
                {
                    case MacroRunnerState.Running:
                        IsRunning = true;
                        CanRun = false;
                        CanStop = true;
                        if (IsFoolModeActive)
                        {
                            StatusText = "State: RUNNING — FOOL MODE";
                            StatusColor = "#E81123";
                        }
                        else
                        {
                            StatusText = "State: RUNNING...";
                            StatusColor = "#107C10";
                        }
                        break;
                    case MacroRunnerState.Stopping:
                        IsRunning = false;
                        CanRun = false;
                        CanStop = false;
                        StatusText = "State: STOPPING...";
                        StatusColor = "#D83B01";
                        break;
                    case MacroRunnerState.Idle:
                        IsRunning = false;
                        CanRun = true;
                        CanStop = false;
                        IsFoolModeActive = false;
                        StatusText = $"State: IDLE | Actions: {Actions.Count}";
                        StatusColor = "#666666";
                        break;
                }

                _onMacroStateChanged(state, StatusText);
            });
        };

        _macroRunner.CycleStarting += cycle =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                string cycleText = RepeatMode switch
                {
                    MacroRepeatMode.Count => $"Cycle [{cycle}/{RepeatCount}]",
                    MacroRepeatMode.UntilStopped => $"Cycle [{cycle}]",
                    _ => string.Empty
                };

                string prefix = string.IsNullOrEmpty(cycleText) ? "State: RUNNING" : $"State: RUNNING | {cycleText}";
                StatusText = $"{prefix} | Starting...";
            });
        };

        _macroRunner.CycleCompleted += cycle =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                string cycleText = RepeatMode switch
                {
                    MacroRepeatMode.Count => $"Cycle [{cycle}/{RepeatCount}] completed",
                    MacroRepeatMode.UntilStopped => $"Cycle [{cycle}] completed",
                    _ => "Completed"
                };
                StatusText = $"State: RUNNING | {cycleText}";
            });
        };

        _macroRunner.ActionStarting += (index, action) =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                string cycleText = RepeatMode switch
                {
                    MacroRepeatMode.Count => $"Cycle [{_macroRunner.CurrentCycle}/{RepeatCount}] | ",
                    MacroRepeatMode.UntilStopped => $"Cycle [{_macroRunner.CurrentCycle}] | ",
                    _ => string.Empty
                };

                StatusText = $"State: RUNNING | {cycleText}Action [{index + 1}/{Actions.Count}]: {action.Name}";
                if (index >= 0 && index < Actions.Count)
                {
                    Actions[index].SetStatus("Running...", "#0078D4");
                    SelectedAction = Actions[index];
                }
            });
        };

        _macroRunner.ActionProgress += (index, action, progress) =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                string cycleText = RepeatMode switch
                {
                    MacroRepeatMode.Count => $"Cycle [{_macroRunner.CurrentCycle}/{RepeatCount}] | ",
                    MacroRepeatMode.UntilStopped => $"Cycle [{_macroRunner.CurrentCycle}] | ",
                    _ => string.Empty
                };

                StatusText = $"State: RUNNING | {cycleText}Action [{index + 1}/{Actions.Count}]: {action.Name} ({progress})";
                if (index >= 0 && index < Actions.Count)
                {
                    string color = progress.StartsWith("Blocked", StringComparison.OrdinalIgnoreCase) ? "#D83B01" : "#0078D4";
                    Actions[index].SetStatus(progress, color);
                }
            });
        };

        _macroRunner.ActionCompleted += (index, action, result) =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                if (index >= 0 && index < Actions.Count)
                {
                    string color = result.IsSuccess ? "#107C10" : "#E81123";
                    string statusDisplay = result.Status.ToString();
                    if (result.Status == MacroActionStatus.Timeout && !string.IsNullOrWhiteSpace(result.BlockerReason))
                    {
                        statusDisplay = $"Timeout — {result.BlockerReason}";
                    }
                    Actions[index].SetStatus(statusDisplay, color);
                }
            });
        };

        _macroRunner.ExecutionCompleted += result =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                IsFoolModeActive = false;
                string color = result.IsSuccess ? "#107C10" : "#E81123";
                string statusDesc = result.FinalStatus.ToString();
                if (result.FinalStatus == MacroActionStatus.Timeout && !string.IsNullOrWhiteSpace(result.BlockerReason))
                {
                    statusDesc = $"Timeout ({result.BlockerReason})";
                }
                string cycleInfo = result.CompletedCyclesCount > 0 ? $" in {result.CompletedCyclesCount} cycle(s)" : string.Empty;
                StatusText = $"Finished: {statusDesc} ({result.CompletedActionsCount}/{result.TotalActionsCount}{cycleInfo} in {result.ElapsedTime.TotalMilliseconds:F0}ms)";
                StatusColor = color;
                _onMacroStateChanged(MacroRunnerState.Idle, StatusText);
            });
        };
    }

    [RelayCommand]
    public void AddApprovalRule()
    {
        string cmd = NewRuleCommand.Trim();
        if (string.IsNullOrWhiteSpace(cmd))
        {
            _dialogService.ShowWarning("Invalid Command", "Please enter the command text to allow.");
            return;
        }

        string name = string.IsNullOrWhiteSpace(NewRuleName) ? cmd : NewRuleName.Trim();
        AutoConfirmRules.Add(new CommandApprovalRuleItem(name, cmd));
        NewRuleName = string.Empty;
        NewRuleCommand = string.Empty;
    }

    [RelayCommand]
    public void DeleteApprovalRule(CommandApprovalRuleItem? item)
    {
        if (item != null)
        {
            AutoConfirmRules.Remove(item);
        }
    }

    [RelayCommand]
    public void ClearApprovalRules()
    {
        AutoConfirmRules.Clear();
    }

    [RelayCommand]
    public void UseTargetPoint()
    {
        if (_targetValidator.IsValid(_currentTarget))
        {
            ActionX = Math.Clamp(_currentTarget!.ClientPoint.ClientX, 0, 9999);
            ActionY = Math.Clamp(_currentTarget.ClientPoint.ClientY, 0, 9999);
        }
    }

    [RelayCommand]
    public void AddClickAction()
    {
        var action = new ClickAction(ActionX, ActionY);
        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
        IsDirty = true;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro Click at ({ActionX}, {ActionY})");
    }

    [RelayCommand]
    public void AddDoubleClickAction()
    {
        var action = new DoubleClickAction(ActionX, ActionY);
        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
        IsDirty = true;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro DoubleClick at ({ActionX}, {ActionY})");
    }

    [RelayCommand]
    public void AddPressKeyAction()
    {
        var action = new PressKeyAction(SelectedKey);
        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
        IsDirty = true;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro PressKey {SelectedKey}");
    }

    [RelayCommand]
    public void AddDelayAction()
    {
        var action = new DelayAction(DelayMilliseconds);
        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
        IsDirty = true;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro Delay {DelayMilliseconds}ms");
    }

    [RelayCommand]
    public void AddWaitColorAction()
    {
        Color col;
        try
        {
            string hex = WaitColorHex.Trim();
            if (!hex.StartsWith('#')) hex = "#" + hex;
            col = ColorTranslator.FromHtml(hex);
        }
        catch
        {
            _dialogService.ShowWarning("Invalid Color", "Invalid hex color format. Use #RRGGBB (e.g. #00FF00)");
            return;
        }

        var action = new WaitColorAction(ActionX, ActionY, col, WaitColorTol, TimeSpan.FromMilliseconds(WaitColorTimeoutMs));
        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
        IsDirty = true;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro WaitColor at ({ActionX}, {ActionY}) RGB({col.R},{col.G},{col.B}) tol={WaitColorTol} timeout={WaitColorTimeoutMs}ms");
    }

    [RelayCommand]
    public void AddWaitForTextAction()
    {
        if (string.IsNullOrWhiteSpace(WaitForTextExpected))
        {
            _dialogService.ShowWarning("Invalid Text", "Please enter expected text to wait for.");
            return;
        }

        var action = new WaitForTextAction(
            WaitForTextExpected,
            WaitForTextMatchMode,
            timeout: TimeSpan.FromMilliseconds(WaitForTextTimeoutMs),
            pollInterval: TimeSpan.FromMilliseconds(WaitForTextPollIntervalMs));

        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
        IsDirty = true;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro WaitForText: \"{WaitForTextExpected}\" ({WaitForTextMatchMode}, timeout={WaitForTextTimeoutMs}ms, poll={WaitForTextPollIntervalMs}ms)");
    }

    [RelayCommand]
    public void AddSafeAutoConfirmAction()
    {
        if (string.IsNullOrWhiteSpace(AutoConfirmPrompt))
        {
            _dialogService.ShowWarning("Invalid Prompt", "Please enter the expected prompt text.");
            return;
        }

        List<CommandApprovalRule> rules;
        if (AutoConfirmRules.Count > 0)
        {
            rules = AutoConfirmRules.Select(r => r.ToRule(
                AutoConfirmProcess?.Trim() ?? "WindowsTerminal.exe",
                AutoConfirmPrompt.Trim(),
                AutoConfirmSelectedOption?.Trim() ?? "Yes, run command")).ToList();
        }
        else if (!string.IsNullOrWhiteSpace(AutoConfirmAllowedCommand))
        {
            var singleRule = new CommandApprovalRule
            {
                Name = string.IsNullOrWhiteSpace(AutoConfirmRuleName) ? "Safe Auto Confirm" : AutoConfirmRuleName.Trim(),
                ExpectedProcess = AutoConfirmProcess?.Trim() ?? "WindowsTerminal.exe",
                ExpectedPrompt = AutoConfirmPrompt.Trim(),
                ExpectedSelectedOption = AutoConfirmSelectedOption?.Trim() ?? "Yes, run command",
                AllowedCommand = AutoConfirmAllowedCommand.Trim(),
                CommandMatchMode = CommandMatchMode.Exact,
                Enabled = true
            };
            rules = new List<CommandApprovalRule> { singleRule };
        }
        else
        {
            if (AutoConfirmPolicyMode != ApprovalPolicyMode.FoolMode)
            {
                _dialogService.ShowWarning("Invalid Command", "Please enter an allowed command or add rules to the rule set.");
                return;
            }
            rules = new List<CommandApprovalRule>();
        }

        var ruleSet = new ApprovalRuleSet
        {
            Name = string.IsNullOrWhiteSpace(AutoConfirmRuleName) ? "Safe Auto Confirm" : AutoConfirmRuleName.Trim(),
            ExpectedProcess = AutoConfirmProcess?.Trim() ?? "WindowsTerminal.exe",
            ExpectedPrompt = AutoConfirmPrompt.Trim(),
            ExpectedSelectedOption = AutoConfirmSelectedOption?.Trim() ?? "Yes, run command",
            Rules = rules
        };

        var action = new SafeAutoConfirmAction(
            ruleSet,
            executionMode: AutoConfirmExecutionMode,
            deliveryMode: KeyDeliveryMode.ForegroundPulse,
            timeout: TimeSpan.FromMilliseconds(AutoConfirmTimeoutMs),
            pollInterval: TimeSpan.FromMilliseconds(AutoConfirmPollIntervalMs),
            waitMode: AutoConfirmWaitMode,
            focusBehavior: AutoConfirmFocusBehavior,
            policyMode: AutoConfirmPolicyMode);

        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
        IsDirty = true;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro SafeAutoConfirm: \"{ruleSet.Name}\" [{AutoConfirmExecutionMode}, {AutoConfirmWaitMode}, {AutoConfirmFocusBehavior}, {AutoConfirmPolicyMode}] Rules count: {rules.Count}");
    }

    [RelayCommand]
    public void EditAction(MacroActionItem? item)
    {
        var target = item ?? SelectedAction;
        if (target == null)
            return;

        EditingActionItem = target;
        IsEditingAction = true;
        SelectedAction = target;
        PopulateBuilderFromAction(target.Action);
    }

    [RelayCommand]
    public void CancelActionEdit()
    {
        IsEditingAction = false;
        EditingActionItem = null;
    }

    [RelayCommand]
    public void SaveActionEdit()
    {
        if (!IsEditingAction || EditingActionItem == null)
            return;

        IMacroAction updatedAction;
        switch (SelectedActionCategoryIndex)
        {
            case 0: // Mouse
                if (EditingActionItem.Action is DoubleClickAction)
                {
                    updatedAction = new DoubleClickAction(ActionX, ActionY);
                }
                else
                {
                    updatedAction = new ClickAction(ActionX, ActionY);
                }
                break;

            case 1: // Keyboard
                updatedAction = new PressKeyAction(SelectedKey);
                break;

            case 2: // Timing
                updatedAction = new DelayAction(DelayMilliseconds);
                break;

            case 3: // Wait for Color
                Color col;
                try
                {
                    string hex = WaitColorHex.Trim();
                    if (!hex.StartsWith('#')) hex = "#" + hex;
                    col = ColorTranslator.FromHtml(hex);
                }
                catch
                {
                    _dialogService.ShowWarning("Invalid Color", "Invalid hex color format. Use #RRGGBB (e.g. #00FF00)");
                    return;
                }
                updatedAction = new WaitColorAction(ActionX, ActionY, col, WaitColorTol, TimeSpan.FromMilliseconds(WaitColorTimeoutMs));
                break;

            case 4: // Wait for Text
                if (string.IsNullOrWhiteSpace(WaitForTextExpected))
                {
                    _dialogService.ShowWarning("Invalid Text", "Please enter expected text to wait for.");
                    return;
                }
                updatedAction = new WaitForTextAction(
                    WaitForTextExpected,
                    WaitForTextMatchMode,
                    timeout: TimeSpan.FromMilliseconds(WaitForTextTimeoutMs),
                    pollInterval: TimeSpan.FromMilliseconds(WaitForTextPollIntervalMs));
                break;

            case 5: // Safe Auto Confirm
                if (string.IsNullOrWhiteSpace(AutoConfirmPrompt))
                {
                    _dialogService.ShowWarning("Invalid Prompt", "Please enter the expected prompt text.");
                    return;
                }

                if (AutoConfirmRules.Count == 1 && !string.IsNullOrWhiteSpace(AutoConfirmAllowedCommand) && AutoConfirmRules[0].CommandText != AutoConfirmAllowedCommand.Trim())
                {
                    AutoConfirmRules[0].CommandText = AutoConfirmAllowedCommand.Trim();
                }

                List<CommandApprovalRule> rules;
                if (AutoConfirmRules.Count > 0)
                {
                    rules = AutoConfirmRules.Select(r => r.ToRule(
                        AutoConfirmProcess?.Trim() ?? "WindowsTerminal.exe",
                        AutoConfirmPrompt.Trim(),
                        AutoConfirmSelectedOption?.Trim() ?? "Yes, run command")).ToList();
                }
                else if (!string.IsNullOrWhiteSpace(AutoConfirmAllowedCommand))
                {
                    var singleRule = new CommandApprovalRule
                    {
                        Name = string.IsNullOrWhiteSpace(AutoConfirmRuleName) ? "Safe Auto Confirm" : AutoConfirmRuleName.Trim(),
                        ExpectedProcess = AutoConfirmProcess?.Trim() ?? "WindowsTerminal.exe",
                        ExpectedPrompt = AutoConfirmPrompt.Trim(),
                        ExpectedSelectedOption = AutoConfirmSelectedOption?.Trim() ?? "Yes, run command",
                        AllowedCommand = AutoConfirmAllowedCommand.Trim(),
                        CommandMatchMode = CommandMatchMode.Exact,
                        Enabled = true
                    };
                    rules = new List<CommandApprovalRule> { singleRule };
                }
                else
                {
                    if (AutoConfirmPolicyMode != ApprovalPolicyMode.FoolMode)
                    {
                        _dialogService.ShowWarning("Invalid Command", "Please enter an allowed command or add rules to the rule set.");
                        return;
                    }
                    rules = new List<CommandApprovalRule>();
                }

                var ruleSet = new ApprovalRuleSet
                {
                    Name = string.IsNullOrWhiteSpace(AutoConfirmRuleName) ? "Safe Auto Confirm" : AutoConfirmRuleName.Trim(),
                    ExpectedProcess = AutoConfirmProcess?.Trim() ?? "WindowsTerminal.exe",
                    ExpectedPrompt = AutoConfirmPrompt.Trim(),
                    ExpectedSelectedOption = AutoConfirmSelectedOption?.Trim() ?? "Yes, run command",
                    Rules = rules
                };

                updatedAction = new SafeAutoConfirmAction(
                    ruleSet,
                    executionMode: AutoConfirmExecutionMode,
                    deliveryMode: KeyDeliveryMode.ForegroundPulse,
                    timeout: TimeSpan.FromMilliseconds(AutoConfirmTimeoutMs),
                    pollInterval: TimeSpan.FromMilliseconds(AutoConfirmPollIntervalMs),
                    waitMode: AutoConfirmWaitMode,
                    focusBehavior: AutoConfirmFocusBehavior,
                    policyMode: AutoConfirmPolicyMode);
                break;

            default:
                return;
        }

        EditingActionItem.UpdateAction(updatedAction);
        _logger.Info($"Updated Macro Action #{EditingActionItem.Index} in place: {updatedAction.DisplayString}");
        IsDirty = true;
        IsEditingAction = false;
        EditingActionItem = null;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
    }

    private void PopulateBuilderFromAction(IMacroAction action)
    {
        switch (action)
        {
            case ClickAction ca:
                SelectedActionCategoryIndex = 0;
                ActionX = ca.ClientX;
                ActionY = ca.ClientY;
                break;
            case DoubleClickAction dca:
                SelectedActionCategoryIndex = 0;
                ActionX = dca.ClientX;
                ActionY = dca.ClientY;
                break;
            case PressKeyAction pka:
                SelectedActionCategoryIndex = 1;
                SelectedKey = pka.Key;
                break;
            case DelayAction da:
                SelectedActionCategoryIndex = 2;
                DelayMilliseconds = da.Milliseconds;
                break;
            case WaitColorAction wca:
                SelectedActionCategoryIndex = 3;
                ActionX = wca.ClientX;
                ActionY = wca.ClientY;
                WaitColorHex = $"#{wca.TargetColor.R:X2}{wca.TargetColor.G:X2}{wca.TargetColor.B:X2}";
                WaitColorTol = wca.Tolerance;
                WaitColorTimeoutMs = (int)wca.Timeout.TotalMilliseconds;
                break;
            case WaitForTextAction wta:
                SelectedActionCategoryIndex = 4;
                WaitForTextExpected = wta.ExpectedText;
                WaitForTextMatchMode = wta.MatchMode;
                WaitForTextTimeoutMs = (int)wta.Timeout.TotalMilliseconds;
                WaitForTextPollIntervalMs = (int)wta.PollInterval.TotalMilliseconds;
                break;
            case SafeAutoConfirmAction saca:
                SelectedActionCategoryIndex = 5;
                AutoConfirmRuleName = saca.RuleSet.Name;
                AutoConfirmProcess = saca.RuleSet.ExpectedProcess ?? "WindowsTerminal.exe";
                AutoConfirmPrompt = saca.RuleSet.ExpectedPrompt ?? "Run this command?";
                AutoConfirmSelectedOption = saca.RuleSet.ExpectedSelectedOption ?? "Yes, run command";
                AutoConfirmRules.Clear();
                foreach (var r in saca.RuleSet.Rules)
                {
                    AutoConfirmRules.Add(new CommandApprovalRuleItem(r.Name, r.AllowedCommand, r.Enabled));
                }
                if (saca.RuleSet.Rules.Count == 1)
                {
                    AutoConfirmAllowedCommand = saca.RuleSet.Rules[0].AllowedCommand;
                }
                else
                {
                    AutoConfirmAllowedCommand = string.Empty;
                }
                AutoConfirmExecutionMode = saca.ExecutionMode;
                AutoConfirmWaitMode = saca.WaitMode;
                AutoConfirmPollIntervalMs = (int)saca.PollInterval.TotalMilliseconds;
                AutoConfirmTimeoutMs = (int)saca.Timeout.TotalMilliseconds;
                AutoConfirmFocusBehavior = saca.FocusBehavior;
                AutoConfirmPolicyMode = saca.PolicyMode;
                break;
        }
    }

    [RelayCommand]
    public void MoveActionUp(MacroActionItem? item)
    {
        var target = item ?? SelectedAction;
        if (target == null)
            return;

        int index = Actions.IndexOf(target);
        if (index > 0)
        {
            Actions.RemoveAt(index);
            Actions.Insert(index - 1, target);
            ReindexActions();
            SelectedAction = target;
            IsDirty = true;
        }
    }

    [RelayCommand]
    public void MoveActionDown(MacroActionItem? item)
    {
        var target = item ?? SelectedAction;
        if (target == null)
            return;

        int index = Actions.IndexOf(target);
        if (index >= 0 && index < Actions.Count - 1)
        {
            Actions.RemoveAt(index);
            Actions.Insert(index + 1, target);
            ReindexActions();
            SelectedAction = target;
            IsDirty = true;
        }
    }

    [RelayCommand]
    public void DeleteAction(MacroActionItem? item)
    {
        var target = item ?? SelectedAction;
        if (target != null)
        {
            if (EditingActionItem == target)
            {
                CancelActionEdit();
            }

            int index = Actions.IndexOf(target);
            Actions.Remove(target);
            ReindexActions();
            if (index < Actions.Count)
            {
                SelectedAction = Actions[index];
            }
            else if (Actions.Count > 0)
            {
                SelectedAction = Actions[^1];
            }
            IsDirty = true;
            StatusText = $"State: IDLE | Actions: {Actions.Count}";
        }
    }

    [RelayCommand]
    public void ClearAllActions()
    {
        CancelActionEdit();
        Actions.Clear();
        IsDirty = true;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
    }

    private void ReindexActions()
    {
        for (int i = 0; i < Actions.Count; i++)
        {
            Actions[i].Index = i + 1;
        }
    }

    [RelayCommand]
    public void RunMacro()
    {
        if (_macroRunner.State != MacroRunnerState.Idle)
            return;

        if (!_targetValidator.IsValid(_currentTarget))
        {
            _dialogService.ShowInfo("No Target Selected",
                "Please select a valid target window in Target Inspector before running a macro.");
            return;
        }

        if (Actions.Count == 0)
        {
            _dialogService.ShowInfo("No Actions Configured",
                "Please add at least one macro action to the sequence.");
            return;
        }

        var actionList = Actions.Select(a => a.Action).ToList();

        // Check if any action requires FOOL MODE Confirm confirmation
        bool requiresFoolModeConfirmation = actionList.Any(a =>
            a is SafeAutoConfirmAction saca &&
            saca.PolicyMode == ApprovalPolicyMode.FoolMode &&
            saca.ExecutionMode == AutoConfirmExecutionMode.Confirm);

        bool isAuthorized = false;
        if (requiresFoolModeConfirmation)
        {
            bool userConfirmed = ShowFoolModeConfirmationDialog();
            if (!userConfirmed)
            {
                _logger.Info("FOOL MODE macro execution cancelled by user. Runner remains IDLE.");
                return;
            }

            isAuthorized = true;
            IsFoolModeActive = true;
            _logger.Warning("WARNING MacroRunner started with FOOL MODE authorization.");
        }
        else
        {
            IsFoolModeActive = false;
        }

        // Reset all item statuses to Ready
        foreach (var act in Actions)
        {
            act.SetStatus("Ready", "#888888");
        }

        var context = new MacroExecutionContext(_clicker, _captureService, _logger, _currentTarget!.TargetHwnd, _keyboard)
        {
            IsFoolModeAuthorized = isAuthorized
        };

        var settings = new MacroRunnerSettings(
            RepeatMode,
            Math.Max(1, RepeatCount),
            Math.Max(0, CycleDelayMilliseconds));

        _ = _macroRunner.RunAsync(actionList, context, settings);
    }

    private const string FoolModeWarningMessage =
        "⚠ FOOL MODE\n\n" +
        "This macro can automatically approve ANY command presented by the recognized permission prompt.\n\n" +
        "Command filtering is disabled.\n\n" +
        "The automation may execute destructive or unexpected commands without asking again during this run.\n\n" +
        "Are you sure you want to proceed with FOOL MODE for this run session?";

    private bool ShowFoolModeConfirmationDialog()
    {
        return _dialogService.Confirm("⚠ FOOL MODE Confirmation", FoolModeWarningMessage, DialogSeverity.Warning);
    }

    [RelayCommand]
    public void StopMacro()
    {
        IsFoolModeActive = false;
        if (_macroRunner.State == MacroRunnerState.Running)
        {
            _macroRunner.Stop();
        }
    }

    public void LoadFromProfile(List<IMacroAction> actions, MacroRunnerSettingsConfig? settings = null, string? profileName = null)
    {
        CancelActionEdit();
        Actions.Clear();
        for (int i = 0; i < actions.Count; i++)
        {
            Actions.Add(new MacroActionItem(i + 1, actions[i]));
        }

        if (settings != null)
        {
            if (Enum.TryParse<MacroRepeatMode>(settings.RepeatMode, ignoreCase: true, out var mode))
            {
                RepeatMode = mode;
            }
            RepeatCount = Math.Max(1, settings.RepeatCount);
            CycleDelayMilliseconds = Math.Max(0, settings.CycleDelayMilliseconds);
        }

        CurrentProfileName = profileName;
        IsDirty = false;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";

        // Section 15, Option A: Automatically select first macro action and populate builder
        if (Actions.Count > 0)
        {
            EditAction(Actions[0]);
        }
    }

    public List<IMacroAction> GetActions() => Actions.Select(a => a.Action).ToList();

    public MacroRunnerSettingsConfig GetMacroSettingsConfig() => new()
    {
        RepeatMode = RepeatMode.ToString(),
        RepeatCount = RepeatCount,
        CycleDelayMilliseconds = CycleDelayMilliseconds
    };
}
