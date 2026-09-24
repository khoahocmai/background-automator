using CommunityToolkit.Mvvm.ComponentModel;
using BackgroundClicker.Core.Capture;
using BackgroundClicker.Core.Clicking;
using BackgroundClicker.Core.Coordinates;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Macro;
using BackgroundClicker.Core.Profiles;
using BackgroundClicker.Core.Runner;
using BackgroundClicker.Core.Security;
using BackgroundClicker.Core.Targeting;

namespace BackgroundClicker.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly IAppLogger _logger;
    private readonly CoordinateService _coordinateService;
    private readonly WindowTargetService _targetService;
    private readonly BackgroundClickerEngine _clicker;
    private readonly ClickRunner _runner;
    private readonly GdiWindowCaptureService _captureService;
    private readonly MacroRunner _macroRunner;
    private readonly ProcessElevationService _elevationService;
    private readonly TargetResolver _targetResolver;
    private readonly ProfileStorageService _profileStorage;

    public TargetViewModel TargetVM { get; }
    public SimpleViewModel SimpleVM { get; }
    public MacroViewModel MacroVM { get; }
    public ProfilesViewModel ProfilesVM { get; }
    public DiagnosticsViewModel DiagnosticsVM { get; }

    [ObservableProperty]
    private WindowTarget? _currentTarget;

    [ObservableProperty]
    private string _statusTargetText = "Target: None";

    [ObservableProperty]
    private string _statusRunnerText = "Runner: IDLE";

    [ObservableProperty]
    private string _statusRunnerColor = "#666666";

    [ObservableProperty]
    private string _statusHotkeysText = "F6 Start/Stop | F7 Emergency Stop";

    public Type? CurrentActivePageType { get; set; }

    public MainViewModel()
    {
        _logger = new InMemoryLogger(maxEntries: 500);
        _coordinateService = new CoordinateService(_logger);
        _targetService = new WindowTargetService(_coordinateService, _logger);
        _clicker = new BackgroundClickerEngine(_logger);
        _runner = new ClickRunner(_clicker, _logger);
        _captureService = new GdiWindowCaptureService(_logger);
        _macroRunner = new MacroRunner(_logger);
        _elevationService = new ProcessElevationService(_logger);
        _targetResolver = new TargetResolver(_targetService, _coordinateService, _logger);
        _profileStorage = new ProfileStorageService(logger: _logger);

        TargetVM = new TargetViewModel(
            _targetService,
            _elevationService,
            _logger,
            OnTargetCommitted);

        SimpleVM = new SimpleViewModel(
            _runner,
            _logger,
            OnSimpleRunnerStateChanged);

        MacroVM = new MacroViewModel(
            _macroRunner,
            _clicker,
            _captureService,
            _logger,
            OnMacroRunnerStateChanged);

        ProfilesVM = new ProfilesViewModel(
            _profileStorage,
            _targetResolver,
            _logger,
            OnTargetCommitted,
            () => CurrentTarget,
            (pts, settings) => SimpleVM.LoadFromProfile(pts, settings),
            acts => MacroVM.LoadFromProfile(acts),
            () => SimpleVM.GetPoints(),
            () => new ClickRunnerSettingsConfig
            {
                IntervalMs = SimpleVM.IntervalMilliseconds,
                RepeatMode = SimpleVM.IsCount ? RepeatMode.Count : RepeatMode.UntilStopped,
                RepeatCount = SimpleVM.RepeatCount
            },
            () => MacroVM.GetActions());

        DiagnosticsVM = new DiagnosticsViewModel(_logger);
    }

    public void Initialize(IntPtr mainWindowHwnd)
    {
        TargetVM.MainWindowHwnd = mainWindowHwnd;
        TargetVM.Initialize();
        ProfilesVM.Initialize();
        _logger.Info("BackgroundClicker initialized (WPF Fluent UI)");
    }

    private void OnTargetCommitted(WindowTarget? target)
    {
        CurrentTarget = target;
        if (target != null && TargetVM.CurrentTarget != target)
        {
            TargetVM.CommitTarget(target);
        }
        SimpleVM.SetCurrentTarget(target);
        MacroVM.SetCurrentTarget(target);
        ProfilesVM.SetCurrentTarget(target);

        if (target != null)
        {
            StatusTargetText = $"Target: {target.ProcessName} ({HwndFormatter.FormatShort(target.TargetHwnd)})";
        }
        else
        {
            StatusTargetText = "Target: None";
        }
    }

    private void OnSimpleRunnerStateChanged(RunnerState state, int cycles, int clicks)
    {
        switch (state)
        {
            case RunnerState.Running:
                StatusRunnerText = "Runner: RUNNING";
                StatusRunnerColor = "#107C10";
                break;
            case RunnerState.Stopping:
                StatusRunnerText = "Runner: STOPPING";
                StatusRunnerColor = "#D83B01";
                break;
            case RunnerState.Idle:
                // Only reset if macro is not running
                if (_macroRunner.State == MacroRunnerState.Idle)
                {
                    StatusRunnerText = "Runner: IDLE";
                    StatusRunnerColor = "#666666";
                }
                break;
        }
    }

    private void OnMacroRunnerStateChanged(MacroRunnerState state, string statusDetail)
    {
        switch (state)
        {
            case MacroRunnerState.Running:
                StatusRunnerText = "Macro: RUNNING";
                StatusRunnerColor = "#107C10";
                break;
            case MacroRunnerState.Stopping:
                StatusRunnerText = "Macro: STOPPING";
                StatusRunnerColor = "#D83B01";
                break;
            case MacroRunnerState.Idle:
                // Only reset if simple runner is not running
                if (_runner.State == RunnerState.Idle)
                {
                    StatusRunnerText = "Runner: IDLE";
                    StatusRunnerColor = "#666666";
                }
                break;
        }
    }

    public void HandleF6()
    {
        if (MacroVM.IsRunning)
        {
            MacroVM.StopMacro();
        }
        else if (SimpleVM.IsRunning)
        {
            SimpleVM.StopRunner();
        }
        else
        {
            // If idle, check which tab is currently active
            if (CurrentActivePageType != null && CurrentActivePageType.Name.Contains("Macro"))
            {
                MacroVM.RunMacro();
            }
            else
            {
                SimpleVM.StartRunner();
            }
        }
    }

    public void HandleF7()
    {
        _logger.Warning("Emergency Stop triggered via F7!");
        SimpleVM.StopRunner();
        MacroVM.StopMacro();
    }

    public void Cleanup()
    {
        TargetVM.Cleanup();
        _runner.Dispose();
        _macroRunner.Dispose();
        _logger.Info("BackgroundClicker shutting down cleanly");
    }
}
