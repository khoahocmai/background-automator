using System.Drawing;
using System.Windows.Forms;
using BackgroundClicker.Core.Coordinates;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;

namespace BackgroundClicker.App.Forms;

public sealed class MainForm : Form
{
    private readonly CoordinateService _coordinateService;
    private readonly WindowTargetService _targetService;
    private readonly IAppLogger _logger;

    // State
    private WindowTarget? _currentTarget;
    private WindowTarget? _candidateTarget;
    private bool _isDraggingCrosshair;

    // Controls - Dropdown & Controls
    private ComboBox _cboWindows = null!;
    private Button _btnRefresh = null!;
    private Button _btnCrosshair = null!;

    // Controls - Inspector Labels
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

    // Diagnostic Log Box
    private ListBox _lstDiagLog = null!;
    private Button _btnClearLog = null!;

    // Background validation timer
    private readonly System.Windows.Forms.Timer _validationTimer = new();

    public MainForm()
    {
        _logger = new InMemoryLogger(maxEntries: 500);
        _coordinateService = new CoordinateService(_logger);
        _targetService = new WindowTargetService(_coordinateService, _logger);

        InitializeComponents();
        _logger.MessageLogged += OnLogMessageReceived;

        _validationTimer.Interval = 500;
        _validationTimer.Tick += OnValidationTimerTick;
        _validationTimer.Start();
    }

    private void InitializeComponents()
    {
        Text = "BackgroundClicker — Target Window Inspector (Phase 1)";
        ClientSize = new Size(720, 680);
        MinimumSize = new Size(650, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var pnlMain = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        pnlMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pnlMain.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        pnlMain.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        // Group 1: Window Selection & Crosshair
        var grpTargeting = new GroupBox
        {
            Text = "Target Selection",
            Dock = DockStyle.Top,
            Height = 110,
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
            Size = new Size(420, 25),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _cboWindows.SelectedIndexChanged += OnWindowDropdownSelectedIndexChanged;

        _btnRefresh = new Button
        {
            Text = "Refresh List",
            Location = new Point(440, 43),
            Size = new Size(95, 27),
            UseVisualStyleBackColor = true
        };
        _btnRefresh.Click += (s, e) => RefreshWindowList();

        _btnCrosshair = new Button
        {
            Text = "◎ Drag crosshair to target window / control",
            Location = new Point(12, 75),
            Size = new Size(523, 28),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
            BackColor = Color.FromArgb(235, 243, 250),
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand
        };
        _btnCrosshair.MouseDown += OnCrosshairMouseDown;
        _btnCrosshair.MouseMove += OnCrosshairMouseMove;
        _btnCrosshair.MouseUp += OnCrosshairMouseUp;

        grpTargeting.Controls.Add(lblSelect);
        grpTargeting.Controls.Add(_cboWindows);
        grpTargeting.Controls.Add(_btnRefresh);
        grpTargeting.Controls.Add(_btnCrosshair);

        // Group 2: Inspector Grid
        var grpInspector = new GroupBox
        {
            Text = "Target Window & Coordinate Inspector",
            Dock = DockStyle.Top,
            Height = 310,
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

        // Row 0: Process & Title
        _lblProcess = CreateValueLabel("-");
        _lblTitle = CreateValueLabel("-");
        tblGrid.Controls.Add(CreateHeaderLabel("Process:"), 0, 0);
        tblGrid.Controls.Add(_lblProcess, 1, 0);
        tblGrid.Controls.Add(CreateHeaderLabel("Title:"), 2, 0);
        tblGrid.Controls.Add(_lblTitle, 3, 0);

        // Row 1: PID & Thread ID
        _lblPid = CreateValueLabel("-");
        _lblThreadId = CreateValueLabel("-");
        tblGrid.Controls.Add(CreateHeaderLabel("PID:"), 0, 1);
        tblGrid.Controls.Add(_lblPid, 1, 1);
        tblGrid.Controls.Add(CreateHeaderLabel("Thread ID:"), 2, 1);
        tblGrid.Controls.Add(_lblThreadId, 3, 1);

        // Row 2: Root HWND & Target HWND
        _lblRootHwnd = CreateValueLabel("-", isCode: true);
        _lblTargetHwnd = CreateValueLabel("-", isCode: true);
        tblGrid.Controls.Add(CreateHeaderLabel("Root HWND:"), 0, 2);
        tblGrid.Controls.Add(_lblRootHwnd, 1, 2);
        tblGrid.Controls.Add(CreateHeaderLabel("Target HWND:"), 2, 2);
        tblGrid.Controls.Add(_lblTargetHwnd, 3, 2);

        // Row 3: Parent HWND & Class
        _lblParentHwnd = CreateValueLabel("-", isCode: true);
        _lblClass = CreateValueLabel("-");
        tblGrid.Controls.Add(CreateHeaderLabel("Parent HWND:"), 0, 3);
        tblGrid.Controls.Add(_lblParentHwnd, 1, 3);
        tblGrid.Controls.Add(CreateHeaderLabel("Class:"), 2, 3);
        tblGrid.Controls.Add(_lblClass, 3, 3);

        // Row 4: Screen Coordinates & Client Coordinates
        _lblScreenCoords = CreateValueLabel("X: - | Y: -", isCode: true);
        _lblClientCoords = CreateValueLabel("X: - | Y: -", isCode: true);
        tblGrid.Controls.Add(CreateHeaderLabel("Screen X/Y:"), 0, 4);
        tblGrid.Controls.Add(_lblScreenCoords, 1, 4);
        tblGrid.Controls.Add(CreateHeaderLabel("Client X/Y:"), 2, 4);
        tblGrid.Controls.Add(_lblClientCoords, 3, 4);

        // Row 5: Status
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

        // Row 6: Buttons
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

        grpInspector.Controls.Add(tblGrid);

        // Group 3: Diagnostics Log
        var grpLog = new GroupBox
        {
            Text = "Diagnostic Log",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };

        var pnlLogBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 30
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
            Font = new Font("Consolas", 8.5F, FontStyle.Regular, GraphicsUnit.Point),
            IntegralHeight = false
        };

        grpLog.Controls.Add(_lstDiagLog);
        grpLog.Controls.Add(pnlLogBar);

        pnlMain.Controls.Add(grpTargeting, 0, 0);
        pnlMain.Controls.Add(grpInspector, 0, 1);
        pnlMain.Controls.Add(grpLog, 0, 2);

        Controls.Add(pnlMain);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RefreshWindowList();
        _logger.Info("BackgroundClicker inspector initialized");
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
            // Point is outside or over our own application
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

    private void CommitTarget(WindowTarget target)
    {
        _currentTarget = target;
        DisplayTargetInfo(target, statusText: "Target valid", statusColor: Color.ForestGreen);

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

        _lblStatus.Text = "Target cleared";
        _lblStatus.ForeColor = Color.DimGray;

        _cboWindows.SelectedIndex = -1;
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
            _logger.Debug($"Coordinates refreshed: Screen ({refreshed.ScreenPoint.X}, {refreshed.ScreenPoint.Y}) -> Client ({refreshed.ClientPoint.ClientX}, {refreshed.ClientPoint.ClientY})");
        }
    }

    private void OnValidationTimerTick(object? sender, EventArgs e)
    {
        if (_isDraggingCrosshair || _currentTarget == null)
            return;

        // Check if selected target still exists
        if (!_currentTarget.IsWindowValid())
        {
            _lblStatus.Text = "Target unavailable (window closed)";
            _lblStatus.ForeColor = Color.Crimson;
            _logger.Warning($"Target window {HwndFormatter.Format(_currentTarget.TargetHwnd)} is no longer valid");
        }
        else
        {
            // Verify and refresh coordinates in case target window moved or resized
            var refreshed = _targetService.RefreshTarget(_currentTarget);
            if (refreshed != null)
            {
                _currentTarget = refreshed;
                _lblScreenCoords.Text = $"X: {refreshed.ScreenPoint.X} | Y: {refreshed.ScreenPoint.Y}";
                _lblClientCoords.Text = $"X: {refreshed.ClientPoint.ClientX} | Y: {refreshed.ClientPoint.ClientY}";
                _lblStatus.Text = "Target valid";
                _lblStatus.ForeColor = Color.ForestGreen;
            }
        }
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
            _validationTimer.Stop();
            _validationTimer.Dispose();
            _logger.MessageLogged -= OnLogMessageReceived;
        }
        base.Dispose(disposing);
    }
}
