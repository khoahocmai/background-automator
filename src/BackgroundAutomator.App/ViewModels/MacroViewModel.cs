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
    {
        _macroRunner = macroRunner;
        _clicker = clicker;
        _captureService = captureService;
        _keyboard = keyboard ?? new BackgroundKeyboardEngine(logger);
        _logger = logger;
        _onMacroStateChanged = onMacroStateChanged;

        WireEvents();
    }

    public MacroViewModel(
        MacroRunner macroRunner,
        BackgroundClickerEngine clicker,
        GdiWindowCaptureService captureService,
        IAppLogger logger,
        Action<MacroRunnerState, string> onMacroStateChanged)
        : this(macroRunner, clicker, captureService, new BackgroundKeyboardEngine(logger), logger, onMacroStateChanged)
    {
    }

    public void SetCurrentTarget(WindowTarget? target)
    {
        _currentTarget = target;
        if (target != null && target.IsWindowValid())
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
                        StatusText = "State: RUNNING...";
                        StatusColor = "#107C10";
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
            MessageBox.Show("Please enter the command text to allow.", "Invalid Command", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        if (_currentTarget != null && _currentTarget.IsWindowValid())
        {
            ActionX = Math.Clamp(_currentTarget.ClientPoint.ClientX, 0, 9999);
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
            MessageBox.Show("Invalid hex color format. Use #RRGGBB (e.g. #00FF00)", "Invalid Color", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("Please enter expected text to wait for.", "Invalid Text", MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show("Please enter the expected prompt text.", "Invalid Prompt", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        else
        {
            if (string.IsNullOrWhiteSpace(AutoConfirmAllowedCommand))
            {
                MessageBox.Show("Please enter an allowed command or add rules to the rule set.", "Invalid Command", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
            waitMode: AutoConfirmWaitMode);

        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
        IsDirty = true;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro SafeAutoConfirm: \"{ruleSet.Name}\" [{AutoConfirmExecutionMode}, {AutoConfirmWaitMode}] Rules count: {rules.Count}");
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

        if (_currentTarget == null || !_currentTarget.IsWindowValid())
        {
            MessageBox.Show("Please select a valid target window in Target Inspector before running a macro.",
                "No Target Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (Actions.Count == 0)
        {
            MessageBox.Show("Please add at least one macro action to the sequence.",
                "No Actions Configured", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Reset all item statuses to Ready
        foreach (var act in Actions)
        {
            act.SetStatus("Ready", "#888888");
        }

        var context = new MacroExecutionContext(_clicker, _captureService, _logger, _currentTarget.TargetHwnd, _keyboard);
        var actionList = Actions.Select(a => a.Action).ToList();
        var settings = new MacroRunnerSettings(
            RepeatMode,
            Math.Max(1, RepeatCount),
            Math.Max(0, CycleDelayMilliseconds));

        _ = _macroRunner.RunAsync(actionList, context, settings);
    }

    [RelayCommand]
    public void StopMacro()
    {
        if (_macroRunner.State == MacroRunnerState.Running)
        {
            _macroRunner.Stop();
        }
    }

    public void LoadFromProfile(List<IMacroAction> actions, MacroRunnerSettingsConfig? settings = null)
    {
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

        IsDirty = false;
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
    }

    public List<IMacroAction> GetActions() => Actions.Select(a => a.Action).ToList();

    public MacroRunnerSettingsConfig GetMacroSettingsConfig() => new()
    {
        RepeatMode = RepeatMode.ToString(),
        RepeatCount = RepeatCount,
        CycleDelayMilliseconds = CycleDelayMilliseconds
    };
}
