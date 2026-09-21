using System.Drawing;
using System.Windows.Forms;
using BackgroundClicker.App.Hotkeys;
using BackgroundClicker.Core.Capture;
using BackgroundClicker.Core.Clicking;
using BackgroundClicker.Core.Coordinates;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Macro;
using BackgroundClicker.Core.Profiles;
using BackgroundClicker.Core.Runner;
using BackgroundClicker.Core.Security;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;

namespace BackgroundClicker.App.Forms;

public sealed class MainForm : Form
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
    private GlobalHotkeyManager? _hotkeyManager;
    private TargetDescriptor? _activeProfileTarget;

    // Macro Mode State
    private readonly List<IMacroAction> _macroActions = new();

    // Phase 1 Target State
    private WindowTarget? _currentTarget;
    private WindowTarget? _candidateTarget;
    private bool _isDraggingCrosshair = false;

    // Simple Mode State
    private readonly List<ClickPoint> _simplePoints = new();
    private int _cyclesCompleted = 0;
    private int _totalClicksExecuted = 0;

    // Controls - Inspector
    private ComboBox _cboWindows = null!;
    private Button _btnRefresh = null!;
    private Button _btnCrosshair = null!;
    private Label _lblProcess = null!;
    private Label _lblTitle = null!;
    private Label _lblPid = null!;
    private Label _lblThreadId = null!;
    private Label _lblRootHwnd = null!;
    private Label _lblTargetHwnd = null!;
    private Label _lblParentHwnd = null!;
    private Label _lblClass = null!;
    private Label _lblScreenCoords = null!;
    private Label _lblClientCoords = null!;
    private Label _lblStatus = null!;
    private Button _btnClear = null!;
    private Button _btnRefreshCoords = null!;

    // Controls - Simple Mode
    private Label _lblSimpleTargetInfo = null!;
    private NumericUpDown _numX = null!;
    private NumericUpDown _numY = null!;
    private RadioButton _radSingle = null!;
    private RadioButton _radDouble = null!;
    private Button _btnAddCurrentTarget = null!;
    private Button _btnAddPoint = null!;
    private ListView _lvPoints = null!;
    private Button _btnRemovePoint = null!;
    private Button _btnMoveUp = null!;
    private Button _btnMoveDown = null!;
    private Button _btnClearPoints = null!;
    private NumericUpDown _numInterval = null!;
    private RadioButton _radUntilStopped = null!;
    private RadioButton _radCount = null!;
    private NumericUpDown _numRepeatCount = null!;
    private Button _btnStart = null!;
    private Button _btnStop = null!;
    private Label _lblRunnerStatus = null!;

    // Controls - Diagnostic Log
    private ListBox _lstDiagLog = null!;
    private Button _btnClearLog = null!;

    // Controls - Macro Mode
    private NumericUpDown _numMacroX = null!;
    private NumericUpDown _numMacroY = null!;
    private Button _btnMacroUseTargetPoint = null!;
    private Button _btnMacroAddClick = null!;
    private Button _btnMacroAddDoubleClick = null!;
    private NumericUpDown _numMacroDelay = null!;
    private Button _btnMacroAddDelay = null!;
    private Button _btnMacroPickColor = null!;
    private NumericUpDown _numMacroTol = null!;
    private NumericUpDown _numMacroTimeout = null!;
    private Button _btnMacroAddWaitColor = null!;
    private ListView _lvMacroActions = null!;
    private Button _btnMacroMoveUp = null!;
    private Button _btnMacroMoveDown = null!;
    private Button _btnMacroDelete = null!;
    private Button _btnMacroClear = null!;
    private Button _btnMacroRun = null!;
    private Button _btnMacroStop = null!;
    private Label _lblMacroStatus = null!;

    // Controls - Inspector Elevation & UIPI
    private Label _lblElevation = null!;
    private Button _btnRestartAdmin = null!;

    // Controls - Profiles Tab
    private TextBox _txtProfileName = null!;
    private ComboBox _cboProfileMode = null!;
    private Label _lblProfileTarget = null!;
    private Button _btnSaveProfile = null!;
    private Button _btnLoadProfile = null!;
    private Button _btnReResolve = null!;
    private Button _btnDeleteProfile = null!;
    private ListView _lvProfiles = null!;

    // Bottom Status Strip
    private Label _lblStatusTarget = null!;
    private Label _lblStatusRunner = null!;
    private Label _lblStatusHotkeys = null!;

    // Background validation timer
    private readonly System.Windows.Forms.Timer _validationTimer = new();

    public MainForm()
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

        InitializeComponents();
        WireRunnerEvents();
        WireMacroRunnerEvents();

        _logger.MessageLogged += OnLogMessageReceived;

        _validationTimer.Interval = 500;
        _validationTimer.Tick += OnValidationTimerTick;
        _validationTimer.Start();
    }

    private void InitializeComponents()
    {
        Text = "BackgroundClicker — Background Clicker (Phase 4 — Hardened Release)";
        ClientSize = new Size(780, 740);
        MinimumSize = new Size(720, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        // Main Tab Control
        var tabControl = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };

        var tabTarget = new TabPage("Target Inspector");
        var tabSimple = new TabPage("Simple Mode");
        var tabMacro = new TabPage("Macro Mode");
        var tabProfiles = new TabPage("Profiles");
        var tabLog = new TabPage("Diagnostic Log");

        // Build Tabs
        BuildTargetTab(tabTarget);
        BuildSimpleTab(tabSimple);
        BuildMacroTab(tabMacro);
        BuildProfilesTab(tabProfiles);
        BuildLogTab(tabLog);

        tabControl.TabPages.Add(tabTarget);
        tabControl.TabPages.Add(tabSimple);
        tabControl.TabPages.Add(tabMacro);
        tabControl.TabPages.Add(tabProfiles);
        tabControl.TabPages.Add(tabLog);

        // Bottom Status Strip
        var pnlStatusStrip = BuildStatusStrip();

        Controls.Add(tabControl);
        Controls.Add(pnlStatusStrip);
    }

    private void BuildTargetTab(TabPage page)
    {
        var pnlTargetLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        pnlTargetLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pnlTargetLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // Group 1: Window Selection & Crosshair
        var grpTargeting = new GroupBox
        {
            Text = "Target Selection",
            Dock = DockStyle.Top,
            Height = 115,
            Padding = new Padding(10)
        };

        var lblSelect = new Label
        {
            Text = "Select Window:",
            Location = new Point(12, 24),
            AutoSize = true
        };

        _cboWindows = new ComboBox
        {
            Location = new Point(12, 44),
            Size = new Size(460, 25),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cboWindows.SelectedIndexChanged += OnWindowDropdownSelectedIndexChanged;

        _btnRefresh = new Button
        {
            Text = "Refresh List",
            Location = new Point(480, 43),
            Size = new Size(100, 27),
            UseVisualStyleBackColor = true
        };
        _btnRefresh.Click += (s, e) => RefreshWindowList();

        _btnCrosshair = new Button
        {
            Text = "◎ Drag crosshair to target window / control",
            Location = new Point(12, 75),
            Size = new Size(568, 28),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = Color.FromArgb(235, 243, 250),
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand
        };
        _btnCrosshair.MouseDown += OnCrosshairMouseDown;
        _btnCrosshair.MouseMove += OnCrosshairMouseMove;
        _btnCrosshair.MouseUp += OnCrosshairMouseUp;
        _btnCrosshair.MouseCaptureChanged += OnCrosshairMouseCaptureChanged;

        grpTargeting.Controls.Add(lblSelect);
        grpTargeting.Controls.Add(_cboWindows);
        grpTargeting.Controls.Add(_btnRefresh);
        grpTargeting.Controls.Add(_btnCrosshair);

        // Group 2: Inspector Grid
        var grpInspector = new GroupBox
        {
            Text = "Target Window & Coordinate Inspector",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        var tblGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 8,
            Padding = new Padding(6)
        };
        tblGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        tblGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        tblGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110F));
        tblGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        for (int i = 0; i < 8; i++)
        {
            tblGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        }

        _lblProcess = CreateValueLabel("-");
        _lblTitle = CreateValueLabel("-");
        tblGrid.Controls.Add(CreateHeaderLabel("Process:"), 0, 0);
        tblGrid.Controls.Add(_lblProcess, 1, 0);
        tblGrid.Controls.Add(CreateHeaderLabel("Title:"), 2, 0);
        tblGrid.Controls.Add(_lblTitle, 3, 0);

        _lblPid = CreateValueLabel("-");
        _lblThreadId = CreateValueLabel("-");
        tblGrid.Controls.Add(CreateHeaderLabel("PID:"), 0, 1);
        tblGrid.Controls.Add(_lblPid, 1, 1);
        tblGrid.Controls.Add(CreateHeaderLabel("Thread ID:"), 2, 1);
        tblGrid.Controls.Add(_lblThreadId, 3, 1);

        _lblRootHwnd = CreateValueLabel("-", isCode: true);
        _lblTargetHwnd = CreateValueLabel("-", isCode: true);
        tblGrid.Controls.Add(CreateHeaderLabel("Root HWND:"), 0, 2);
        tblGrid.Controls.Add(_lblRootHwnd, 1, 2);
        tblGrid.Controls.Add(CreateHeaderLabel("Target HWND:"), 2, 2);
        tblGrid.Controls.Add(_lblTargetHwnd, 3, 2);

        _lblParentHwnd = CreateValueLabel("-", isCode: true);
        _lblClass = CreateValueLabel("-");
        tblGrid.Controls.Add(CreateHeaderLabel("Parent HWND:"), 0, 3);
        tblGrid.Controls.Add(_lblParentHwnd, 1, 3);
        tblGrid.Controls.Add(CreateHeaderLabel("Class:"), 2, 3);
        tblGrid.Controls.Add(_lblClass, 3, 3);

        _lblScreenCoords = CreateValueLabel("X: - | Y: -", isCode: true);
        _lblClientCoords = CreateValueLabel("X: - | Y: -", isCode: true);
        tblGrid.Controls.Add(CreateHeaderLabel("Screen X/Y:"), 0, 4);
        tblGrid.Controls.Add(_lblScreenCoords, 1, 4);
        tblGrid.Controls.Add(CreateHeaderLabel("Client X/Y:"), 2, 4);
        tblGrid.Controls.Add(_lblClientCoords, 3, 4);

        _lblStatus = new Label
        {
            Text = "No target selected",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.DimGray
        };
        tblGrid.Controls.Add(CreateHeaderLabel("Status:"), 0, 5);
        tblGrid.Controls.Add(_lblStatus, 1, 5);
        tblGrid.SetColumnSpan(_lblStatus, 3);

        var pnlActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };
        _btnClear = new Button
        {
            Text = "Clear Target",
            Size = new Size(110, 26),
            UseVisualStyleBackColor = true
        };
        _btnClear.Click += (s, e) => ClearTarget();

        _btnRefreshCoords = new Button
        {
            Text = "Refresh Coordinates",
            Size = new Size(140, 26),
            UseVisualStyleBackColor = true
        };
        _btnRefreshCoords.Click += (s, e) => RefreshCoordinates();

        pnlActions.Controls.Add(_btnClear);
        pnlActions.Controls.Add(_btnRefreshCoords);

        tblGrid.Controls.Add(pnlActions, 1, 6);
        tblGrid.SetColumnSpan(pnlActions, 3);

        _lblElevation = CreateValueLabel("-");
        _btnRestartAdmin = new Button
        {
            Text = "🛡️ Restart as Administrator",
            Size = new Size(190, 24),
            BackColor = Color.FromArgb(255, 240, 230),
            ForeColor = Color.DarkRed,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
            Visible = false
        };
        _btnRestartAdmin.Click += (s, e) =>
        {
            if (ProcessElevationService.RestartAsAdministrator())
            {
                Close();
            }
        };

        var pnlElevation = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0)
        };
        pnlElevation.Controls.Add(_lblElevation);
        pnlElevation.Controls.Add(_btnRestartAdmin);

        tblGrid.Controls.Add(CreateHeaderLabel("Elevation:"), 0, 7);
        tblGrid.Controls.Add(pnlElevation, 1, 7);
        tblGrid.SetColumnSpan(pnlElevation, 3);

        grpInspector.Controls.Add(tblGrid);

        pnlTargetLayout.Controls.Add(grpTargeting, 0, 0);
        pnlTargetLayout.Controls.Add(grpInspector, 0, 1);

        page.Controls.Add(pnlTargetLayout);
    }

    private void BuildSimpleTab(TabPage page)
    {
        var pnlSimple = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };

        // Section 1: Target Summary & Point Setup
        var grpPointSetup = new GroupBox
        {
            Text = "Target & Point Setup",
            Dock = DockStyle.Top,
            Height = 125,
            Padding = new Padding(10)
        };

        _lblSimpleTargetInfo = new Label
        {
            Text = "Target: [No target selected in Target Inspector]",
            Location = new Point(12, 22),
            Size = new Size(690, 20),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(40, 70, 110)
        };

        var lblX = new Label { Text = "X:", Location = new Point(12, 54), AutoSize = true };
        _numX = new NumericUpDown
        {
            Location = new Point(32, 52),
            Size = new Size(70, 23),
            Minimum = -32768,
            Maximum = 32767,
            Value = 0
        };

        var lblY = new Label { Text = "Y:", Location = new Point(115, 54), AutoSize = true };
        _numY = new NumericUpDown
        {
            Location = new Point(135, 52),
            Size = new Size(70, 23),
            Minimum = -32768,
            Maximum = 32767,
            Value = 0
        };

        _radSingle = new RadioButton
        {
            Text = "Single Click",
            Location = new Point(220, 52),
            AutoSize = true,
            Checked = true
        };

        _radDouble = new RadioButton
        {
            Text = "Double Click",
            Location = new Point(320, 52),
            AutoSize = true
        };

        _btnAddCurrentTarget = new Button
        {
            Text = "+ Add Target Point (From Inspector)",
            Location = new Point(12, 85),
            Size = new Size(240, 28),
            UseVisualStyleBackColor = true
        };
        _btnAddCurrentTarget.Click += (s, e) => AddCurrentTargetPoint();

        _btnAddPoint = new Button
        {
            Text = "+ Add Custom X/Y Point",
            Location = new Point(260, 85),
            Size = new Size(180, 28),
            UseVisualStyleBackColor = true
        };
        _btnAddPoint.Click += (s, e) => AddCustomPoint();

        grpPointSetup.Controls.Add(_lblSimpleTargetInfo);
        grpPointSetup.Controls.Add(lblX);
        grpPointSetup.Controls.Add(_numX);
        grpPointSetup.Controls.Add(lblY);
        grpPointSetup.Controls.Add(_numY);
        grpPointSetup.Controls.Add(_radSingle);
        grpPointSetup.Controls.Add(_radDouble);
        grpPointSetup.Controls.Add(_btnAddCurrentTarget);
        grpPointSetup.Controls.Add(_btnAddPoint);

        // Section 2: Points List & Ordering
        var grpPointsList = new GroupBox
        {
            Text = "Ordered Click Points",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        _lvPoints = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        _lvPoints.Columns.Add("#", 40);
        _lvPoints.Columns.Add("Target HWND", 170);
        _lvPoints.Columns.Add("Client X", 80);
        _lvPoints.Columns.Add("Client Y", 80);
        _lvPoints.Columns.Add("Click Action", 110);

        var pnlPointActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 120,
            Padding = new Padding(4),
            FlowDirection = FlowDirection.TopDown
        };

        _btnRemovePoint = new Button { Text = "Remove", Width = 110, Height = 28, UseVisualStyleBackColor = true };
        _btnRemovePoint.Click += (s, e) => RemoveSelectedPoint();

        _btnMoveUp = new Button { Text = "Move Up ▲", Width = 110, Height = 28, UseVisualStyleBackColor = true };
        _btnMoveUp.Click += (s, e) => MoveSelectedPoint(-1);

        _btnMoveDown = new Button { Text = "Move Down ▼", Width = 110, Height = 28, UseVisualStyleBackColor = true };
        _btnMoveDown.Click += (s, e) => MoveSelectedPoint(1);

        _btnClearPoints = new Button { Text = "Clear All", Width = 110, Height = 28, UseVisualStyleBackColor = true };
        _btnClearPoints.Click += (s, e) => ClearAllPoints();

        pnlPointActions.Controls.Add(_btnRemovePoint);
        pnlPointActions.Controls.Add(_btnMoveUp);
        pnlPointActions.Controls.Add(_btnMoveDown);
        pnlPointActions.Controls.Add(_btnClearPoints);

        grpPointsList.Controls.Add(_lvPoints);
        grpPointsList.Controls.Add(pnlPointActions);

        // Section 3: Execution Settings & Controls
        var grpExecution = new GroupBox
        {
            Text = "Execution & Controls",
            Dock = DockStyle.Bottom,
            Height = 135,
            Padding = new Padding(10)
        };

        var lblInterval = new Label { Text = "Interval:", Location = new Point(12, 26), AutoSize = true };
        _numInterval = new NumericUpDown
        {
            Location = new Point(70, 24),
            Size = new Size(80, 23),
            Minimum = 10,
            Maximum = 60000,
            Value = 500
        };
        var lblMs = new Label { Text = "ms", Location = new Point(155, 26), AutoSize = true };

        var lblRepeat = new Label { Text = "Repeat:", Location = new Point(210, 26), AutoSize = true };
        _radUntilStopped = new RadioButton
        {
            Text = "Until stopped",
            Location = new Point(265, 24),
            AutoSize = true,
            Checked = true
        };
        _radCount = new RadioButton
        {
            Text = "Count:",
            Location = new Point(375, 24),
            AutoSize = true
        };
        _numRepeatCount = new NumericUpDown
        {
            Location = new Point(440, 24),
            Size = new Size(80, 23),
            Minimum = 1,
            Maximum = 1000000,
            Value = 10,
            Enabled = false
        };
        _radCount.CheckedChanged += (s, e) => _numRepeatCount.Enabled = _radCount.Checked;

        _btnStart = new Button
        {
            Text = "▶ Start (F6)",
            Location = new Point(12, 60),
            Size = new Size(130, 32),
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = Color.FromArgb(225, 245, 225),
            UseVisualStyleBackColor = false
        };
        _btnStart.Click += (s, e) => StartRunner();

        _btnStop = new Button
        {
            Text = "⏹ Stop (F7)",
            Location = new Point(150, 60),
            Size = new Size(130, 32),
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = Color.FromArgb(255, 230, 230),
            UseVisualStyleBackColor = false,
            Enabled = false
        };
        _btnStop.Click += (s, e) => StopRunner();

        _lblRunnerStatus = new Label
        {
            Text = "State: IDLE | Cycles: 0 | Clicks: 0",
            Location = new Point(295, 66),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.DimGray
        };

        grpExecution.Controls.Add(lblInterval);
        grpExecution.Controls.Add(_numInterval);
        grpExecution.Controls.Add(lblMs);
        grpExecution.Controls.Add(lblRepeat);
        grpExecution.Controls.Add(_radUntilStopped);
        grpExecution.Controls.Add(_radCount);
        grpExecution.Controls.Add(_numRepeatCount);
        grpExecution.Controls.Add(_btnStart);
        grpExecution.Controls.Add(_btnStop);
        grpExecution.Controls.Add(_lblRunnerStatus);

        pnlSimple.Controls.Add(grpPointsList);
        pnlSimple.Controls.Add(grpPointSetup);
        pnlSimple.Controls.Add(grpExecution);

        page.Controls.Add(pnlSimple);
    }

    private void BuildMacroTab(TabPage page)
    {
        var pnlMacro = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        // 1. Action Creation Setup GroupBox
        var grpMacroActionSetup = new GroupBox
        {
            Text = "Configure & Add Macro Action",
            Dock = DockStyle.Top,
            Height = 135,
            Padding = new Padding(10)
        };

        // Row 1: Click / DoubleClick
        var lblCoord = new Label { Text = "Client X:", Location = new Point(12, 26), AutoSize = true };
        _numMacroX = new NumericUpDown { Location = new Point(68, 24), Width = 65, Maximum = 9999, Minimum = 0, Value = 50 };
        var lblCoordY = new Label { Text = "Y:", Location = new Point(140, 26), AutoSize = true };
        _numMacroY = new NumericUpDown { Location = new Point(158, 24), Width = 65, Maximum = 9999, Minimum = 0, Value = 50 };

        _btnMacroUseTargetPoint = new Button
        {
            Text = "🎯 From Target",
            Location = new Point(230, 22),
            Size = new Size(110, 27),
            UseVisualStyleBackColor = true
        };
        _btnMacroUseTargetPoint.Click += (s, e) =>
        {
            if (_currentTarget != null)
            {
                _numMacroX.Value = Math.Clamp(_currentTarget.ClientPoint.ClientX, 0, 9999);
                _numMacroY.Value = Math.Clamp(_currentTarget.ClientPoint.ClientY, 0, 9999);
            }
        };

        _btnMacroAddClick = new Button
        {
            Text = "+ Click",
            Location = new Point(348, 22),
            Size = new Size(85, 27),
            UseVisualStyleBackColor = true
        };
        _btnMacroAddClick.Click += (s, e) => AddMacroClick();

        _btnMacroAddDoubleClick = new Button
        {
            Text = "+ DoubleClick",
            Location = new Point(440, 22),
            Size = new Size(100, 27),
            UseVisualStyleBackColor = true
        };
        _btnMacroAddDoubleClick.Click += (s, e) => AddMacroDoubleClick();

        // Row 2: Delay
        var lblDelay = new Label { Text = "Delay:", Location = new Point(12, 60), AutoSize = true };
        _numMacroDelay = new NumericUpDown { Location = new Point(68, 58), Width = 75, Maximum = 60000, Minimum = 0, Value = 500 };
        var lblDelayMs = new Label { Text = "ms", Location = new Point(148, 60), AutoSize = true };

        _btnMacroAddDelay = new Button
        {
            Text = "+ Add Delay",
            Location = new Point(180, 56),
            Size = new Size(110, 27),
            UseVisualStyleBackColor = true
        };
        _btnMacroAddDelay.Click += (s, e) => AddMacroDelay();

        // Row 3: WaitColor
        var lblColor = new Label { Text = "WaitColor:", Location = new Point(12, 96), AutoSize = true };
        _btnMacroPickColor = new Button
        {
            Text = "Color",
            Location = new Point(78, 93),
            Size = new Size(60, 26),
            BackColor = Color.FromArgb(0, 255, 0),
            UseVisualStyleBackColor = false
        };
        _btnMacroPickColor.Click += (s, e) =>
        {
            using var dlg = new ColorDialog { Color = _btnMacroPickColor.BackColor };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _btnMacroPickColor.BackColor = dlg.Color;
            }
        };

        var lblTol = new Label { Text = "Tol:", Location = new Point(145, 96), AutoSize = true };
        _numMacroTol = new NumericUpDown { Location = new Point(175, 94), Width = 45, Maximum = 255, Minimum = 0, Value = 5 };

        var lblTimeout = new Label { Text = "Timeout:", Location = new Point(228, 96), AutoSize = true };
        _numMacroTimeout = new NumericUpDown { Location = new Point(285, 94), Width = 65, Maximum = 60000, Minimum = 100, Value = 5000 };
        var lblTimeoutMs = new Label { Text = "ms", Location = new Point(355, 96), AutoSize = true };

        _btnMacroAddWaitColor = new Button
        {
            Text = "+ Add WaitColor",
            Location = new Point(385, 93),
            Size = new Size(125, 27),
            UseVisualStyleBackColor = true
        };
        _btnMacroAddWaitColor.Click += (s, e) => AddMacroWaitColor();

        grpMacroActionSetup.Controls.Add(lblCoord);
        grpMacroActionSetup.Controls.Add(_numMacroX);
        grpMacroActionSetup.Controls.Add(lblCoordY);
        grpMacroActionSetup.Controls.Add(_numMacroY);
        grpMacroActionSetup.Controls.Add(_btnMacroUseTargetPoint);
        grpMacroActionSetup.Controls.Add(_btnMacroAddClick);
        grpMacroActionSetup.Controls.Add(_btnMacroAddDoubleClick);

        grpMacroActionSetup.Controls.Add(lblDelay);
        grpMacroActionSetup.Controls.Add(_numMacroDelay);
        grpMacroActionSetup.Controls.Add(lblDelayMs);
        grpMacroActionSetup.Controls.Add(_btnMacroAddDelay);

        grpMacroActionSetup.Controls.Add(lblColor);
        grpMacroActionSetup.Controls.Add(_btnMacroPickColor);
        grpMacroActionSetup.Controls.Add(lblTol);
        grpMacroActionSetup.Controls.Add(_numMacroTol);
        grpMacroActionSetup.Controls.Add(lblTimeout);
        grpMacroActionSetup.Controls.Add(_numMacroTimeout);
        grpMacroActionSetup.Controls.Add(lblTimeoutMs);
        grpMacroActionSetup.Controls.Add(_btnMacroAddWaitColor);

        // 2. Bottom Execution Control GroupBox
        var grpMacroExecution = new GroupBox
        {
            Text = "Macro Execution Control",
            Dock = DockStyle.Bottom,
            Height = 72,
            Padding = new Padding(10)
        };

        _btnMacroRun = new Button
        {
            Text = "▶ Run Macro",
            Location = new Point(14, 24),
            Size = new Size(125, 34),
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = Color.FromArgb(225, 245, 225),
            UseVisualStyleBackColor = false
        };
        _btnMacroRun.Click += (s, e) => StartMacro();

        _btnMacroStop = new Button
        {
            Text = "⏹ Stop",
            Location = new Point(148, 24),
            Size = new Size(110, 34),
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = Color.FromArgb(255, 230, 230),
            UseVisualStyleBackColor = false,
            Enabled = false
        };
        _btnMacroStop.Click += (s, e) => StopMacro();

        _lblMacroStatus = new Label
        {
            Text = "State: IDLE | Actions: 0",
            Location = new Point(275, 32),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.DimGray
        };

        grpMacroExecution.Controls.Add(_btnMacroRun);
        grpMacroExecution.Controls.Add(_btnMacroStop);
        grpMacroExecution.Controls.Add(_lblMacroStatus);

        // 3. Middle Configured Macro Actions Sequence GroupBox
        var grpMacroList = new GroupBox
        {
            Text = "Configured Macro Action Sequence",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        var pnlListButtons = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            Padding = new Padding(2)
        };

        _btnMacroMoveUp = new Button { Text = "▲ Move Up", Dock = DockStyle.Left, Width = 95, UseVisualStyleBackColor = true };
        _btnMacroMoveUp.Click += (s, e) => MoveMacroAction(-1);

        _btnMacroMoveDown = new Button { Text = "▼ Move Down", Dock = DockStyle.Left, Width = 95, UseVisualStyleBackColor = true };
        _btnMacroMoveDown.Click += (s, e) => MoveMacroAction(1);

        _btnMacroDelete = new Button { Text = "❌ Delete", Dock = DockStyle.Left, Width = 85, UseVisualStyleBackColor = true };
        _btnMacroDelete.Click += (s, e) => DeleteMacroAction();

        _btnMacroClear = new Button { Text = "🗑 Clear All", Dock = DockStyle.Right, Width = 95, UseVisualStyleBackColor = true };
        _btnMacroClear.Click += (s, e) => ClearMacroActions();

        pnlListButtons.Controls.Add(_btnMacroClear);
        pnlListButtons.Controls.Add(_btnMacroDelete);
        pnlListButtons.Controls.Add(_btnMacroMoveDown);
        pnlListButtons.Controls.Add(_btnMacroMoveUp);

        _lvMacroActions = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };
        _lvMacroActions.Columns.Add("#", 38);
        _lvMacroActions.Columns.Add("Action", 95);
        _lvMacroActions.Columns.Add("Configuration / Target Parameters", 430);
        _lvMacroActions.Columns.Add("Status", 100);

        grpMacroList.Controls.Add(_lvMacroActions);
        grpMacroList.Controls.Add(pnlListButtons);

        pnlMacro.Controls.Add(grpMacroList);
        pnlMacro.Controls.Add(grpMacroActionSetup);
        pnlMacro.Controls.Add(grpMacroExecution);

        page.Controls.Add(pnlMacro);
    }

    private void BuildProfilesTab(TabPage page)
    {
        var pnlProfiles = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        var grpConfig = new GroupBox
        {
            Text = "Profile Configuration & Target Re-Resolution",
            Dock = DockStyle.Top,
            Height = 145,
            Padding = new Padding(10)
        };

        var lblName = new Label { Text = "Profile Name:", Location = new Point(12, 24), AutoSize = true };
        _txtProfileName = new TextBox { Location = new Point(100, 21), Width = 200, Text = "DefaultProfile" };

        var lblMode = new Label { Text = "Mode:", Location = new Point(315, 24), AutoSize = true };
        _cboProfileMode = new ComboBox
        {
            Location = new Point(365, 21),
            Width = 120,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cboProfileMode.Items.AddRange(new object[] { "Simple Mode", "Macro Mode" });
        _cboProfileMode.SelectedIndex = 0;

        _lblProfileTarget = new Label
        {
            Text = "Target Descriptor: [No target selected]",
            Location = new Point(12, 54),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.DarkSlateBlue
        };

        var pnlButtons = new FlowLayoutPanel
        {
            Location = new Point(12, 85),
            Size = new Size(720, 48),
            FlowDirection = FlowDirection.LeftToRight
        };

        _btnSaveProfile = new Button
        {
            Text = "💾 Save Profile",
            Size = new Size(130, 32),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point)
        };
        _btnSaveProfile.Click += (s, e) => SaveCurrentProfile();

        _btnLoadProfile = new Button
        {
            Text = "📂 Load Profile",
            Size = new Size(130, 32),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point)
        };
        _btnLoadProfile.Click += (s, e) => LoadSelectedProfile();

        _btnReResolve = new Button
        {
            Text = "🔄 Re-resolve Target",
            Size = new Size(150, 32),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point)
        };
        _btnReResolve.Click += (s, e) => ReResolveCurrentProfileTarget();

        _btnDeleteProfile = new Button
        {
            Text = "🗑 Delete Profile",
            Size = new Size(120, 32),
            ForeColor = Color.DarkRed
        };
        _btnDeleteProfile.Click += (s, e) => DeleteSelectedProfile();

        pnlButtons.Controls.Add(_btnSaveProfile);
        pnlButtons.Controls.Add(_btnLoadProfile);
        pnlButtons.Controls.Add(_btnReResolve);
        pnlButtons.Controls.Add(_btnDeleteProfile);

        grpConfig.Controls.Add(lblName);
        grpConfig.Controls.Add(_txtProfileName);
        grpConfig.Controls.Add(lblMode);
        grpConfig.Controls.Add(_cboProfileMode);
        grpConfig.Controls.Add(_lblProfileTarget);
        grpConfig.Controls.Add(pnlButtons);

        var grpList = new GroupBox
        {
            Text = "Saved Profiles",
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        _lvProfiles = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false
        };
        _lvProfiles.Columns.Add("Profile Name", 160);
        _lvProfiles.Columns.Add("Mode", 90);
        _lvProfiles.Columns.Add("Target Process / Window", 220);
        _lvProfiles.Columns.Add("Items", 60);
        _lvProfiles.Columns.Add("Last Updated", 140);

        _lvProfiles.SelectedIndexChanged += OnProfileSelectionChanged;
        _lvProfiles.DoubleClick += (s, e) => LoadSelectedProfile();

        grpList.Controls.Add(_lvProfiles);

        pnlProfiles.Controls.Add(grpList);
        pnlProfiles.Controls.Add(grpConfig);

        page.Controls.Add(pnlProfiles);
    }

    private void OnProfileSelectionChanged(object? sender, EventArgs e)
    {
        if (_lvProfiles.SelectedItems.Count > 0)
        {
            string name = _lvProfiles.SelectedItems[0].Text;
            _txtProfileName.Text = name;
            try
            {
                var profile = _profileStorage.LoadProfileByName(name);
                _cboProfileMode.SelectedIndex = profile.Mode == ProfileMode.Macro ? 1 : 0;
                if (profile.Target != null)
                {
                    _lblProfileTarget.Text = $"Target: {profile.Target.ProcessName} (Title: '{profile.Target.WindowTitle ?? "*"}' Mode: {profile.Target.MatchMode})";
                }
                else
                {
                    _lblProfileTarget.Text = "Target Descriptor: [No target in profile]";
                }
            }
            catch
            {
                // Ignore preview failure
            }
        }
    }

    private void SaveCurrentProfile()
    {
        string name = _txtProfileName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Please enter a valid profile name.", "Save Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var mode = _cboProfileMode.SelectedIndex == 1 ? ProfileMode.Macro : ProfileMode.Simple;
        var profile = new ProfileModel
        {
            Name = name,
            Mode = mode
        };

        if (_currentTarget != null)
        {
            profile.Target = TargetDescriptor.FromWindowTarget(_currentTarget, TitleMatchMode.Contains);
        }
        else if (_activeProfileTarget != null)
        {
            profile.Target = _activeProfileTarget;
        }

        if (mode == ProfileMode.Simple)
        {
            profile.SimpleSettings = new ClickRunnerSettingsConfig
            {
                IntervalMs = (int)_numInterval.Value,
                RepeatMode = _radCount.Checked ? RepeatMode.Count : RepeatMode.UntilStopped,
                RepeatCount = (int)_numRepeatCount.Value
            };
            profile.ClickPoints = _simplePoints.Select(p => ClickPointConfig.FromClickPoint(p)).ToList();
        }
        else
        {
            profile.MacroActions = _macroActions.Select(MacroActionConfig.FromMacroAction).ToList();
        }

        try
        {
            _profileStorage.SaveProfile(profile);
            _activeProfileTarget = profile.Target;
            RefreshProfileList();
            _logger.Info($"Profile '{name}' saved successfully.");
            MessageBox.Show($"Profile '{name}' saved successfully.", "Save Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to save profile '{name}'", ex);
            MessageBox.Show($"Failed to save profile: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadSelectedProfile()
    {
        if (_lvProfiles.SelectedItems.Count == 0)
            return;

        string name = _lvProfiles.SelectedItems[0].Text;
        try
        {
            var profile = _profileStorage.LoadProfileByName(name);
            _txtProfileName.Text = profile.Name;
            _cboProfileMode.SelectedIndex = profile.Mode == ProfileMode.Macro ? 1 : 0;
            _activeProfileTarget = profile.Target;

            if (profile.Target != null)
            {
                _lblProfileTarget.Text = $"Target: {profile.Target.ProcessName} (Title: '{profile.Target.WindowTitle ?? "*"}' Mode: {profile.Target.MatchMode})";

                // Attempt automatic re-resolution
                var res = _targetResolver.Resolve(profile.Target);
                if (res.IsSuccess && res.Target != null)
                {
                    CommitTarget(res.Target);
                    _logger.Info($"Re-resolved profile target to HWND {HwndFormatter.Format(res.Target.TargetHwnd)}");
                }
                else if (res.Status == TargetResolutionStatus.Ambiguous)
                {
                    _logger.Warning($"Target re-resolution ambiguous: {res.Candidates.Count} matching windows found.");
                    MessageBox.Show($"Multiple matching windows ({res.Candidates.Count}) found for process '{profile.Target.ProcessName}'. Please select the specific window in Target Inspector.", "Ambiguous Target", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    _logger.Warning($"Target re-resolution: {res.Message}");
                    _lblStatus.Text = $"Target not found: {profile.Target.ProcessName}";
                    _lblStatus.ForeColor = Color.Crimson;
                }
            }
            else
            {
                _lblProfileTarget.Text = "Target Descriptor: [None]";
            }

            if (profile.Mode == ProfileMode.Simple)
            {
                _simplePoints.Clear();

                if (profile.SimpleSettings != null)
                {
                    _numInterval.Value = Math.Clamp(profile.SimpleSettings.IntervalMs, (int)_numInterval.Minimum, (int)_numInterval.Maximum);
                    if (profile.SimpleSettings.RepeatMode == RepeatMode.Count)
                    {
                        _radCount.Checked = true;
                        _numRepeatCount.Value = Math.Clamp(profile.SimpleSettings.RepeatCount, (int)_numRepeatCount.Minimum, (int)_numRepeatCount.Maximum);
                    }
                    else
                    {
                        _radUntilStopped.Checked = true;
                    }
                }

                IntPtr targetHwnd = _currentTarget?.TargetHwnd ?? IntPtr.Zero;
                foreach (var ptConfig in profile.ClickPoints)
                {
                    _simplePoints.Add(ptConfig.ToClickPoint(targetHwnd));
                }
                RefreshPointsList();
            }
            else
            {
                _macroActions.Clear();

                foreach (var actConfig in profile.MacroActions)
                {
                    _macroActions.Add(actConfig.ToMacroAction());
                }
                RefreshMacroList();
            }

            _logger.Info($"Profile '{profile.Name}' loaded successfully.");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load profile '{name}'", ex);
            MessageBox.Show($"Failed to load profile: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ReResolveCurrentProfileTarget()
    {
        if (_activeProfileTarget == null && _currentTarget != null)
        {
            _activeProfileTarget = TargetDescriptor.FromWindowTarget(_currentTarget, TitleMatchMode.Contains);
        }

        if (_activeProfileTarget == null)
        {
            MessageBox.Show("No target descriptor is currently loaded or configured.", "Re-resolve Target", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var res = _targetResolver.Resolve(_activeProfileTarget);
        if (res.IsSuccess && res.Target != null)
        {
            CommitTarget(res.Target);
            _logger.Info($"Target re-resolved successfully to HWND {HwndFormatter.Format(res.Target.TargetHwnd)}");
            MessageBox.Show($"Target re-resolved successfully to HWND {HwndFormatter.Format(res.Target.TargetHwnd)} ({res.Target.ProcessName})", "Re-resolve Target", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (res.Status == TargetResolutionStatus.Ambiguous)
        {
            MessageBox.Show($"Found {res.Candidates.Count} matching windows. Ambiguity must be resolved manually via Target Inspector.", "Ambiguous Target", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            MessageBox.Show($"Target not found: {res.Message}", "Target Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteSelectedProfile()
    {
        if (_lvProfiles.SelectedItems.Count == 0)
            return;

        string name = _lvProfiles.SelectedItems[0].Text;
        if (MessageBox.Show($"Are you sure you want to delete profile '{name}'?", "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _profileStorage.DeleteProfile(name);
            RefreshProfileList();
            _logger.Info($"Profile '{name}' deleted.");
        }
    }

    private void RefreshProfileList()
    {
        try
        {
            var headers = _profileStorage.ListProfiles();
            _lvProfiles.BeginUpdate();
            _lvProfiles.Items.Clear();

            foreach (var h in headers)
            {
                var lvi = new ListViewItem(h.Name);
                lvi.SubItems.Add(h.Mode == ProfileMode.Simple ? "Simple" : "Macro");
                lvi.SubItems.Add(h.TargetDescription);
                lvi.SubItems.Add(h.ItemCount.ToString());
                lvi.SubItems.Add(h.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                _lvProfiles.Items.Add(lvi);
            }

            _lvProfiles.EndUpdate();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to refresh profile list", ex);
        }
    }

    private void BuildLogTab(TabPage page)
    {
        var pnlLog = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(10)
        };

        var pnlLogBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 35
        };

        _btnClearLog = new Button
        {
            Text = "Clear Log",
            Dock = DockStyle.Right,
            Width = 90,
            UseVisualStyleBackColor = true
        };
        _btnClearLog.Click += (s, e) =>
        {
            _logger.Clear();
            _lstDiagLog.Items.Clear();
        };
        pnlLogBar.Controls.Add(_btnClearLog);

        _lstDiagLog = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 8.8F, FontStyle.Regular, GraphicsUnit.Point),
            IntegralHeight = false
        };

        pnlLog.Controls.Add(_lstDiagLog);
        pnlLog.Controls.Add(pnlLogBar);

        page.Controls.Add(pnlLog);
    }

    private Panel BuildStatusStrip()
    {
        var pnlStatus = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 28,
            BackColor = Color.FromArgb(240, 240, 240),
            Padding = new Padding(8, 4, 8, 4)
        };

        _lblStatusTarget = new Label
        {
            Text = "Target: None",
            Dock = DockStyle.Left,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(50, 50, 50)
        };

        _lblStatusHotkeys = new Label
        {
            Text = "Hotkeys: F6 (Start/Stop) | F7 (Emergency Stop)",
            Dock = DockStyle.Right,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(70, 70, 70)
        };

        _lblStatusRunner = new Label
        {
            Text = "Runner: IDLE",
            Dock = DockStyle.Right,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 20, 0),
            Font = new Font("Segoe UI Semibold", 8.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.DimGray
        };

        pnlStatus.Controls.Add(_lblStatusTarget);
        pnlStatus.Controls.Add(_lblStatusRunner);
        pnlStatus.Controls.Add(_lblStatusHotkeys);

        return pnlStatus;
    }

    private void WireRunnerEvents()
    {
        _runner.StateChanged += (s, state) =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => OnRunnerStateChanged(state));
            }
        };

        _runner.PointExecuted += (s, point) =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => OnPointExecuted(point));
            }
        };

        _runner.CycleCompleted += (s, cycle) =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => OnCycleCompleted(cycle));
            }
        };

        _runner.ErrorOccurred += (s, error) =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() => OnRunnerErrorOccurred(error));
            }
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // Register Global Hotkeys
        _hotkeyManager = new GlobalHotkeyManager(
            Handle,
            onStartStop: ToggleRunner,
            onEmergencyStop: EmergencyStopRunner,
            logger: _logger);

        _hotkeyManager.RegisterHotkeys();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == (int)NativeConstants.WM_HOTKEY)
        {
            int hotkeyId = m.WParam.ToInt32();
            if (_hotkeyManager?.ProcessHotkey(hotkeyId) == true)
            {
                return;
            }
        }

        base.WndProc(ref m);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RefreshWindowList();
        RefreshProfileList();
        _logger.Info("BackgroundClicker initialized (Phase 4 — Hardened Release)");
    }

    private static Label CreateHeaderLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Font = new Font("Segoe UI Semibold", 8.8F, FontStyle.Bold, GraphicsUnit.Point),
        ForeColor = Color.FromArgb(70, 70, 70)
    };

    private static Label CreateValueLabel(string initialText, bool isCode = false) => new()
    {
        Text = initialText,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Font = isCode
            ? new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point)
            : new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
        AutoEllipsis = true
    };

    private void RefreshWindowList()
    {
        try
        {
            var windows = _targetService.EnumerateTopLevelWindows(ignoreRootHwnd: Handle);
            _cboWindows.BeginUpdate();
            _cboWindows.Items.Clear();

            foreach (var win in windows)
            {
                _cboWindows.Items.Add(win);
            }

            _cboWindows.EndUpdate();
            _logger.Info($"Enumerated {windows.Count} usable top-level windows");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to enumerate windows", ex);
        }
    }

    private void OnWindowDropdownSelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isDraggingCrosshair)
            return;

        if (_cboWindows.SelectedItem is WindowTargetCandidate candidate)
        {
            var target = _targetService.ResolveTargetFromCandidate(candidate);
            if (target != null)
            {
                CommitTarget(target);
            }
            else
            {
                _lblStatus.Text = "Target unavailable (failed to resolve)";
                _lblStatus.ForeColor = Color.Crimson;
            }
        }
    }

    private void OnCrosshairMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _isDraggingCrosshair = true;
            _candidateTarget = null;
            Cursor = Cursors.Cross;
            User32.SetCapture(_btnCrosshair.Handle);

            _lblStatus.Text = "Dragging crosshair... Hover over target control";
            _lblStatus.ForeColor = Color.DarkOrange;
            _logger.Debug("Crosshair drag started");
        }
    }

    private void OnCrosshairMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_isDraggingCrosshair)
            return;

        if (!User32.GetCursorPos(out POINT screenPt))
            return;

        Point pt = new(screenPt.X, screenPt.Y);
        var candidate = _targetService.ResolveTargetFromScreenPoint(pt, ignoreProcessRootHwnd: Handle);

        if (candidate != null)
        {
            _candidateTarget = candidate;
            DisplayTargetInfo(candidate, statusText: "Inspecting candidate...", statusColor: Color.DarkBlue);
        }
        else
        {
            _lblScreenCoords.Text = $"X: {pt.X} | Y: {pt.Y}";
            _lblStatus.Text = "Hovering over BackgroundClicker (selection ignored)";
            _lblStatus.ForeColor = Color.Gray;
        }
    }

    private void OnCrosshairMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isDraggingCrosshair)
            return;

        _isDraggingCrosshair = false;
        User32.ReleaseCapture();
        Cursor = Cursors.Default;

        if (_candidateTarget != null)
        {
            CommitTarget(_candidateTarget);
            _logger.Info($"Committed target: HWND {_currentTarget?.TargetHwnd} ({_currentTarget?.ProcessName})");
        }
        else
        {
            if (_currentTarget == null)
            {
                _lblStatus.Text = "Crosshair released: No external target selected";
                _lblStatus.ForeColor = Color.DimGray;
            }
            else
            {
                DisplayTargetInfo(_currentTarget, statusText: "Target valid", statusColor: Color.Green);
            }
        }
    }

    private void OnCrosshairMouseCaptureChanged(object? sender, EventArgs e)
    {
        if (_isDraggingCrosshair && !_btnCrosshair.Capture)
        {
            _isDraggingCrosshair = false;
            Cursor = Cursors.Default;
            if (_candidateTarget != null)
            {
                CommitTarget(_candidateTarget);
            }
            else if (_currentTarget != null)
            {
                DisplayTargetInfo(_currentTarget, statusText: "Target valid", statusColor: Color.Green);
            }
            else
            {
                _lblStatus.Text = "Crosshair released: No target selected";
                _lblStatus.ForeColor = Color.DimGray;
            }
        }
    }

    private void CommitTarget(WindowTarget target)
    {
        _currentTarget = target;
        DisplayTargetInfo(target, statusText: "Target valid", statusColor: Color.ForestGreen);

        // Synchronize target with Simple Mode UI
        _lblSimpleTargetInfo.Text = $"Target: {target.ProcessName} | HWND: {HwndFormatter.Format(target.TargetHwnd)} | Local: ({target.ClientPoint.ClientX}, {target.ClientPoint.ClientY})";
        _lblStatusTarget.Text = $"Target: {target.ProcessName} ({HwndFormatter.FormatShort(target.TargetHwnd)})";

        _numX.Value = Math.Clamp(target.ClientPoint.ClientX, (int)_numX.Minimum, (int)_numX.Maximum);
        _numY.Value = Math.Clamp(target.ClientPoint.ClientY, (int)_numY.Minimum, (int)_numY.Maximum);

        // Synchronize dropdown if candidate exists in it
        for (int i = 0; i < _cboWindows.Items.Count; i++)
        {
            if (_cboWindows.Items[i] is WindowTargetCandidate candidate && candidate.Hwnd == target.RootHwnd)
            {
                if (_cboWindows.SelectedIndex != i)
                {
                    _cboWindows.SelectedIndexChanged -= OnWindowDropdownSelectedIndexChanged;
                    _cboWindows.SelectedIndex = i;
                    _cboWindows.SelectedIndexChanged += OnWindowDropdownSelectedIndexChanged;
                }
                break;
            }
        }
    }

    private void DisplayTargetInfo(WindowTarget target, string statusText, Color statusColor)
    {
        _lblProcess.Text = string.IsNullOrWhiteSpace(target.ProcessName) ? "[Unknown]" : target.ProcessName;
        _lblTitle.Text = string.IsNullOrWhiteSpace(target.WindowTitle) ? "[No Title]" : target.WindowTitle;
        _lblPid.Text = target.ProcessId.ToString();
        _lblThreadId.Text = target.ThreadId.ToString();

        _lblRootHwnd.Text = HwndFormatter.Format(target.RootHwnd);
        _lblTargetHwnd.Text = HwndFormatter.Format(target.TargetHwnd);
        _lblParentHwnd.Text = target.ParentHwnd == IntPtr.Zero ? "0x0000000000000000" : HwndFormatter.Format(target.ParentHwnd);
        _lblClass.Text = string.IsNullOrWhiteSpace(target.WindowClass) ? "[Unknown]" : target.WindowClass;

        _lblScreenCoords.Text = $"X: {target.ScreenPoint.X} | Y: {target.ScreenPoint.Y}";
        _lblClientCoords.Text = $"X: {target.ClientPoint.ClientX} | Y: {target.ClientPoint.ClientY}";

        _lblStatus.Text = statusText;
        _lblStatus.ForeColor = statusColor;

        // UIPI / Elevation diagnostic check
        var elevationResult = _elevationService.CheckCompatibility(target.ProcessId);
        if (elevationResult.Compatibility == ElevationCompatibility.UipiMismatch)
        {
            _lblElevation.Text = "⚠️ UIPI Mismatch: Target is Elevated! Clicks will be blocked.";
            _lblElevation.ForeColor = Color.Crimson;
            _btnRestartAdmin.Visible = true;
        }
        else if (elevationResult.Compatibility == ElevationCompatibility.Compatible)
        {
            _lblElevation.Text = elevationResult.CurrentProcessElevated ? "Compatible (Running as Admin)" : "Compatible (Standard User)";
            _lblElevation.ForeColor = Color.ForestGreen;
            _btnRestartAdmin.Visible = false;
        }
        else
        {
            _lblElevation.Text = "Elevation Status Unknown";
            _lblElevation.ForeColor = Color.DarkOrange;
            _btnRestartAdmin.Visible = true;
        }
    }

    private void ClearTarget()
    {
        _currentTarget = null;
        _candidateTarget = null;

        _lblProcess.Text = "-";
        _lblTitle.Text = "-";
        _lblPid.Text = "-";
        _lblThreadId.Text = "-";
        _lblRootHwnd.Text = "-";
        _lblTargetHwnd.Text = "-";
        _lblParentHwnd.Text = "-";
        _lblClass.Text = "-";
        _lblScreenCoords.Text = "X: - | Y: -";
        _lblClientCoords.Text = "X: - | Y: -";
        _lblElevation.Text = "-";
        _lblElevation.ForeColor = Color.Black;
        _btnRestartAdmin.Visible = false;

        _lblStatus.Text = "Target cleared";
        _lblStatus.ForeColor = Color.DimGray;

        _cboWindows.SelectedIndex = -1;
        _lblSimpleTargetInfo.Text = "Target: [No target selected in Target Inspector]";
        _lblStatusTarget.Text = "Target: None";
        _logger.Info("Target cleared by user");
    }

    private void RefreshCoordinates()
    {
        if (_currentTarget == null)
            return;

        if (!_currentTarget.IsWindowValid())
        {
            _lblStatus.Text = "Target unavailable (window closed)";
            _lblStatus.ForeColor = Color.Crimson;
            _logger.Warning($"Target window {HwndFormatter.Format(_currentTarget.TargetHwnd)} is closed");
            return;
        }

        var refreshed = _targetService.RefreshTarget(_currentTarget);
        if (refreshed != null)
        {
            _currentTarget = refreshed;
            DisplayTargetInfo(refreshed, statusText: "Coordinates refreshed (Target valid)", statusColor: Color.ForestGreen);
        }
    }

    private void OnValidationTimerTick(object? sender, EventArgs e)
    {
        if (_isDraggingCrosshair || _currentTarget == null)
            return;

        if (!_currentTarget.IsWindowValid())
        {
            _lblStatus.Text = "Target unavailable (window closed)";
            _lblStatus.ForeColor = Color.Crimson;
            _lblSimpleTargetInfo.Text = "Target: [Window closed or invalid]";
            _lblStatusTarget.Text = "Target: Closed";
        }
        else
        {
            var refreshed = _targetService.RefreshTarget(_currentTarget);
            if (refreshed != null)
            {
                _currentTarget = refreshed;
                _lblScreenCoords.Text = $"X: {refreshed.ScreenPoint.X} | Y: {refreshed.ScreenPoint.Y}";
                _lblClientCoords.Text = $"X: {refreshed.ClientPoint.ClientX} | Y: {refreshed.ClientPoint.ClientY}";
            }
        }
    }

    // --- Simple Mode Handlers ---

    private void AddCurrentTargetPoint()
    {
        if (_currentTarget == null || !_currentTarget.IsWindowValid())
        {
            MessageBox.Show(this, "Please inspect and lock onto a valid target window/control first.", "No Valid Target", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var clickType = _radDouble.Checked ? ClickType.Double : ClickType.Single;
        var point = new ClickPoint(_currentTarget.TargetHwnd, _currentTarget.ClientPoint.ClientX, _currentTarget.ClientPoint.ClientY, clickType);
        _simplePoints.Add(point);
        RefreshPointsList();
        _logger.Info($"Added point #{_simplePoints.Count}: HWND {HwndFormatter.FormatShort(point.Hwnd)} at ({point.ClientX}, {point.ClientY}) [{point.ClickType}]");
    }

    private void AddCustomPoint()
    {
        if (_currentTarget == null || !_currentTarget.IsWindowValid())
        {
            MessageBox.Show(this, "Please select a target window/control first so the coordinates attach to a valid HWND.", "No Valid Target", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var clickType = _radDouble.Checked ? ClickType.Double : ClickType.Single;
        var point = new ClickPoint(_currentTarget.TargetHwnd, (int)_numX.Value, (int)_numY.Value, clickType);
        _simplePoints.Add(point);
        RefreshPointsList();
        _logger.Info($"Added custom point #{_simplePoints.Count}: HWND {HwndFormatter.FormatShort(point.Hwnd)} at ({point.ClientX}, {point.ClientY}) [{point.ClickType}]");
    }

    private void RemoveSelectedPoint()
    {
        if (_lvPoints.SelectedIndices.Count > 0)
        {
            int index = _lvPoints.SelectedIndices[0];
            _simplePoints.RemoveAt(index);
            RefreshPointsList();
            if (index < _simplePoints.Count)
            {
                _lvPoints.Items[index].Selected = true;
            }
            else if (_simplePoints.Count > 0)
            {
                _lvPoints.Items[_simplePoints.Count - 1].Selected = true;
            }
        }
    }

    private void MoveSelectedPoint(int direction)
    {
        if (_lvPoints.SelectedIndices.Count > 0)
        {
            int index = _lvPoints.SelectedIndices[0];
            int newIndex = index + direction;
            if (newIndex >= 0 && newIndex < _simplePoints.Count)
            {
                var item = _simplePoints[index];
                _simplePoints.RemoveAt(index);
                _simplePoints.Insert(newIndex, item);
                RefreshPointsList();
                _lvPoints.Items[newIndex].Selected = true;
            }
        }
    }

    private void ClearAllPoints()
    {
        _simplePoints.Clear();
        RefreshPointsList();
    }

    private void RefreshPointsList()
    {
        _lvPoints.BeginUpdate();
        _lvPoints.Items.Clear();

        for (int i = 0; i < _simplePoints.Count; i++)
        {
            var p = _simplePoints[i];
            var lvi = new ListViewItem((i + 1).ToString());
            lvi.SubItems.Add(HwndFormatter.Format(p.Hwnd));
            lvi.SubItems.Add(p.ClientX.ToString());
            lvi.SubItems.Add(p.ClientY.ToString());
            lvi.SubItems.Add(p.ClickType.ToString());
            _lvPoints.Items.Add(lvi);
        }

        _lvPoints.EndUpdate();
    }

    private void ToggleRunner()
    {
        if (_runner.State == RunnerState.Running)
        {
            StopRunner();
        }
        else if (_runner.State == RunnerState.Idle)
        {
            StartRunner();
        }
    }

    private void EmergencyStopRunner()
    {
        _logger.Warning("Emergency Stop triggered via F7!");
        StopRunner();
        StopMacro();
    }

    private void StartMacro()
    {
        if (_macroRunner.State != MacroRunnerState.Idle)
            return;

        if (_currentTarget == null || !_currentTarget.IsWindowValid())
        {
            MessageBox.Show(this, "Please select a valid target window in Target Inspector before running a macro.", "No Target Selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_macroActions.Count == 0)
        {
            MessageBox.Show(this, "Please add at least one macro action to the sequence.", "No Actions Configured", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var context = new MacroExecutionContext(_clicker, _captureService, _logger, _currentTarget.TargetHwnd);
        _ = _macroRunner.RunAsync(_macroActions.ToList(), context);
    }

    private void StopMacro()
    {
        if (_macroRunner.State == MacroRunnerState.Running)
        {
            _macroRunner.Stop();
        }
    }

    private void AddMacroClick()
    {
        _macroActions.Add(new ClickAction((int)_numMacroX.Value, (int)_numMacroY.Value));
        RefreshMacroList();
        _logger.Info($"Added Macro Click at ({(int)_numMacroX.Value}, {(int)_numMacroY.Value})");
    }

    private void AddMacroDoubleClick()
    {
        _macroActions.Add(new DoubleClickAction((int)_numMacroX.Value, (int)_numMacroY.Value));
        RefreshMacroList();
        _logger.Info($"Added Macro DoubleClick at ({(int)_numMacroX.Value}, {(int)_numMacroY.Value})");
    }

    private void AddMacroDelay()
    {
        int ms = (int)_numMacroDelay.Value;
        _macroActions.Add(new DelayAction(ms));
        RefreshMacroList();
        _logger.Info($"Added Macro Delay {ms}ms");
    }

    private void AddMacroWaitColor()
    {
        int x = (int)_numMacroX.Value;
        int y = (int)_numMacroY.Value;
        Color col = _btnMacroPickColor.BackColor;
        int tol = (int)_numMacroTol.Value;
        int timeoutMs = (int)_numMacroTimeout.Value;

        _macroActions.Add(new WaitColorAction(x, y, col, tol, TimeSpan.FromMilliseconds(timeoutMs)));
        RefreshMacroList();
        _logger.Info($"Added Macro WaitColor at ({x}, {y}) RGB({col.R},{col.G},{col.B}) tol={tol} timeout={timeoutMs}ms");
    }

    private void MoveMacroAction(int direction)
    {
        if (_lvMacroActions.SelectedIndices.Count > 0)
        {
            int index = _lvMacroActions.SelectedIndices[0];
            int newIndex = index + direction;
            if (newIndex >= 0 && newIndex < _macroActions.Count)
            {
                var item = _macroActions[index];
                _macroActions.RemoveAt(index);
                _macroActions.Insert(newIndex, item);
                RefreshMacroList();
                _lvMacroActions.Items[newIndex].Selected = true;
            }
        }
    }

    private void DeleteMacroAction()
    {
        if (_lvMacroActions.SelectedIndices.Count > 0)
        {
            int index = _lvMacroActions.SelectedIndices[0];
            _macroActions.RemoveAt(index);
            RefreshMacroList();
            if (index < _macroActions.Count)
            {
                _lvMacroActions.Items[index].Selected = true;
            }
            else if (_macroActions.Count > 0)
            {
                _lvMacroActions.Items[_macroActions.Count - 1].Selected = true;
            }
        }
    }

    private void ClearMacroActions()
    {
        _macroActions.Clear();
        RefreshMacroList();
    }

    private void RefreshMacroList()
    {
        _lvMacroActions.BeginUpdate();
        _lvMacroActions.Items.Clear();

        for (int i = 0; i < _macroActions.Count; i++)
        {
            var action = _macroActions[i];
            var lvi = new ListViewItem((i + 1).ToString());
            lvi.SubItems.Add(action.Name);
            lvi.SubItems.Add(action.DisplayString);
            lvi.SubItems.Add("Ready");
            _lvMacroActions.Items.Add(lvi);
        }

        _lvMacroActions.EndUpdate();
        _lblMacroStatus.Text = $"State: {(_macroRunner.State == MacroRunnerState.Running ? "RUNNING" : "IDLE")} | Actions: {_macroActions.Count}";
    }

    private void WireMacroRunnerEvents()
    {
        _macroRunner.StateChanged += state =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() =>
                {
                    switch (state)
                    {
                        case MacroRunnerState.Running:
                            _btnMacroRun.Enabled = false;
                            _btnMacroStop.Enabled = true;
                            _lblMacroStatus.Text = "State: RUNNING...";
                            _lblMacroStatus.ForeColor = Color.ForestGreen;
                            break;
                        case MacroRunnerState.Stopping:
                            _btnMacroRun.Enabled = false;
                            _btnMacroStop.Enabled = false;
                            _lblMacroStatus.Text = "State: STOPPING...";
                            _lblMacroStatus.ForeColor = Color.DarkOrange;
                            break;
                        case MacroRunnerState.Idle:
                            _btnMacroRun.Enabled = true;
                            _btnMacroStop.Enabled = false;
                            _lblMacroStatus.Text = $"State: IDLE | Actions: {_macroActions.Count}";
                            _lblMacroStatus.ForeColor = Color.DimGray;
                            break;
                    }
                });
            }
        };

        _macroRunner.ActionStarting += (index, action) =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() =>
                {
                    _lblMacroStatus.Text = $"State: RUNNING | Action [{index + 1}/{_macroActions.Count}]: {action.Name}";
                    if (index >= 0 && index < _lvMacroActions.Items.Count)
                    {
                        _lvMacroActions.Items[index].SubItems[3].Text = "Running...";
                        _lvMacroActions.Items[index].Selected = true;
                        _lvMacroActions.Items[index].EnsureVisible();
                    }
                });
            }
        };

        _macroRunner.ActionCompleted += (index, action, result) =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() =>
                {
                    if (index >= 0 && index < _lvMacroActions.Items.Count)
                    {
                        _lvMacroActions.Items[index].SubItems[3].Text = result.Status.ToString();
                        _lvMacroActions.Items[index].ForeColor = result.IsSuccess ? Color.DarkGreen : Color.Crimson;
                    }
                });
            }
        };

        _macroRunner.ExecutionCompleted += result =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(() =>
                {
                    _lblMacroStatus.Text = $"Finished: {result.FinalStatus} ({result.CompletedActionsCount}/{result.TotalActionsCount} in {result.ElapsedTime.TotalMilliseconds:F0}ms)";
                    _lblMacroStatus.ForeColor = result.IsSuccess ? Color.ForestGreen : Color.Crimson;
                });
            }
        };
    }

    private void StartRunner()
    {
        if (_runner.State != RunnerState.Idle)
            return;

        if (_simplePoints.Count == 0)
        {
            MessageBox.Show(this, "Please add at least one click point before starting.", "No Points Configured", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var config = new ClickRunnerConfig
        {
            Points = _simplePoints.ToList(),
            IntervalMilliseconds = (int)_numInterval.Value,
            RepeatMode = _radCount.Checked ? RepeatMode.Count : RepeatMode.UntilStopped,
            RepeatCount = (int)_numRepeatCount.Value
        };

        _cyclesCompleted = 0;
        _totalClicksExecuted = 0;

        bool started = _runner.Start(config);
        if (!started)
        {
            _logger.Warning("Runner failed to start (already active).");
        }
    }

    private void StopRunner()
    {
        if (_runner.State == RunnerState.Running)
        {
            _runner.Stop();
        }
    }

    private void OnRunnerStateChanged(RunnerState state)
    {
        switch (state)
        {
            case RunnerState.Running:
                _btnStart.Enabled = false;
                _btnStop.Enabled = true;
                _lblRunnerStatus.Text = $"State: RUNNING | Cycles: {_cyclesCompleted} | Clicks: {_totalClicksExecuted}";
                _lblRunnerStatus.ForeColor = Color.ForestGreen;
                _lblStatusRunner.Text = "Runner: RUNNING";
                _lblStatusRunner.ForeColor = Color.ForestGreen;
                break;
            case RunnerState.Stopping:
                _btnStart.Enabled = false;
                _btnStop.Enabled = false;
                _lblRunnerStatus.Text = "State: STOPPING...";
                _lblRunnerStatus.ForeColor = Color.DarkOrange;
                _lblStatusRunner.Text = "Runner: STOPPING";
                _lblStatusRunner.ForeColor = Color.DarkOrange;
                break;
            case RunnerState.Idle:
                _btnStart.Enabled = true;
                _btnStop.Enabled = false;
                _lblRunnerStatus.Text = $"State: IDLE | Cycles: {_cyclesCompleted} | Clicks: {_totalClicksExecuted}";
                _lblRunnerStatus.ForeColor = Color.DimGray;
                _lblStatusRunner.Text = "Runner: IDLE";
                _lblStatusRunner.ForeColor = Color.DimGray;
                break;
        }
    }

    private void OnPointExecuted(ClickPoint point)
    {
        _totalClicksExecuted++;
        _lblRunnerStatus.Text = $"State: RUNNING | Cycles: {_cyclesCompleted} | Clicks: {_totalClicksExecuted}";
    }

    private void OnCycleCompleted(int cycle)
    {
        _cyclesCompleted = cycle;
        _lblRunnerStatus.Text = $"State: RUNNING | Cycles: {_cyclesCompleted} | Clicks: {_totalClicksExecuted}";
    }

    private void OnRunnerErrorOccurred(string error)
    {
        _lblRunnerStatus.Text = $"State: IDLE (Error: {error})";
        _lblRunnerStatus.ForeColor = Color.Crimson;
        _lblStatus.Text = error;
        _lblStatus.ForeColor = Color.Crimson;
    }

    private void OnLogMessageReceived(LogEntry entry)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(() =>
            {
                _lstDiagLog.Items.Add(entry.ToString());
                if (_lstDiagLog.Items.Count > 500)
                {
                    _lstDiagLog.Items.RemoveAt(0);
                }
                _lstDiagLog.TopIndex = _lstDiagLog.Items.Count - 1;
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hotkeyManager?.Dispose();
            _runner.Dispose();
            _macroRunner.Dispose();
            _validationTimer.Stop();
            _validationTimer.Dispose();
            _logger.MessageLogged -= OnLogMessageReceived;
        }
        base.Dispose(disposing);
    }
}

