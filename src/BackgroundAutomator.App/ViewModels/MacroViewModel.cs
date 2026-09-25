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

    // Action builder configuration fields
    [ObservableProperty]
    private int _selectedActionCategoryIndex = 0; // 0: Mouse, 1: Keyboard, 2: Timing, 3: Condition

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
    private int _autoConfirmPollIntervalMs = 500;

    [ObservableProperty]
    private int _autoConfirmTimeoutMs = 60000;

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

        _macroRunner.ActionStarting += (index, action) =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                StatusText = $"State: RUNNING | Action [{index + 1}/{Actions.Count}]: {action.Name}";
                if (index >= 0 && index < Actions.Count)
                {
                    Actions[index].SetStatus("Running...", "#0078D4");
                    SelectedAction = Actions[index];
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
                    Actions[index].SetStatus(result.Status.ToString(), color);
                }
            });
        };

        _macroRunner.ExecutionCompleted += result =>
        {
            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                string color = result.IsSuccess ? "#107C10" : "#E81123";
                StatusText = $"Finished: {result.FinalStatus} ({result.CompletedActionsCount}/{result.TotalActionsCount} in {result.ElapsedTime.TotalMilliseconds:F0}ms)";
                StatusColor = color;
                _onMacroStateChanged(MacroRunnerState.Idle, StatusText);
            });
        };
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
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro Click at ({ActionX}, {ActionY})");
    }

    [RelayCommand]
    public void AddDoubleClickAction()
    {
        var action = new DoubleClickAction(ActionX, ActionY);
        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro DoubleClick at ({ActionX}, {ActionY})");
    }

    [RelayCommand]
    public void AddPressKeyAction()
    {
        var action = new PressKeyAction(SelectedKey);
        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro PressKey {SelectedKey}");
    }

    [RelayCommand]
    public void AddDelayAction()
    {
        var action = new DelayAction(DelayMilliseconds);
        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
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
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro WaitForText: \"{WaitForTextExpected}\" ({WaitForTextMatchMode}, timeout={WaitForTextTimeoutMs}ms, poll={WaitForTextPollIntervalMs}ms)");
    }

    [RelayCommand]
    public void AddSafeAutoConfirmAction()
    {
        if (string.IsNullOrWhiteSpace(AutoConfirmAllowedCommand))
        {
            MessageBox.Show("Please enter the exact allowed command to auto-confirm.", "Invalid Command", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(AutoConfirmPrompt))
        {
            MessageBox.Show("Please enter the expected prompt text.", "Invalid Prompt", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var rule = new CommandApprovalRule
        {
            Name = string.IsNullOrWhiteSpace(AutoConfirmRuleName) ? "Safe Auto Confirm" : AutoConfirmRuleName.Trim(),
            ExpectedProcess = AutoConfirmProcess?.Trim() ?? "WindowsTerminal.exe",
            ExpectedPrompt = AutoConfirmPrompt.Trim(),
            ExpectedSelectedOption = AutoConfirmSelectedOption?.Trim() ?? "Yes, run command",
            AllowedCommand = AutoConfirmAllowedCommand.Trim(),
            CommandMatchMode = CommandMatchMode.Exact,
            Enabled = true
        };

        var action = new SafeAutoConfirmAction(
            rule,
            executionMode: AutoConfirmExecutionMode,
            deliveryMode: KeyDeliveryMode.ForegroundPulse,
            timeout: TimeSpan.FromMilliseconds(AutoConfirmTimeoutMs),
            pollInterval: TimeSpan.FromMilliseconds(AutoConfirmPollIntervalMs));

        Actions.Add(new MacroActionItem(Actions.Count + 1, action));
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
        _logger.Info($"Added Macro SafeAutoConfirm: \"{rule.Name}\" [{AutoConfirmExecutionMode}] Command: \"{rule.AllowedCommand}\"");
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
            StatusText = $"State: IDLE | Actions: {Actions.Count}";
        }
    }

    [RelayCommand]
    public void ClearAllActions()
    {
        Actions.Clear();
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
        _ = _macroRunner.RunAsync(actionList, context);
    }

    [RelayCommand]
    public void StopMacro()
    {
        if (_macroRunner.State == MacroRunnerState.Running)
        {
            _macroRunner.Stop();
        }
    }

    public void LoadFromProfile(List<IMacroAction> actions)
    {
        Actions.Clear();
        for (int i = 0; i < actions.Count; i++)
        {
            Actions.Add(new MacroActionItem(i + 1, actions[i]));
        }
        StatusText = $"State: IDLE | Actions: {Actions.Count}";
    }

    public List<IMacroAction> GetActions() => Actions.Select(a => a.Action).ToList();
}
