using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;

namespace BackgroundClicker.TestTarget.Forms;

public sealed class TestTargetForm : Form, IMessageFilter
{
    private readonly List<RawMessageEntry> _messageLog = new();
    private readonly object _logLock = new();
    private const int MaxLogEntries = 1000;

    // UI Controls
    private Label _lblProcessInfo = null!;
    private GroupBox _grpControls = null!;
    private Panel _testPanel = null!;
    private Button _testButton = null!;
    private Label _panelLabel = null!;
    private Label _lblClickCounter = null!;
    private Button _btnResetCounter = null!;
    private Label _lblCoordinates = null!;
    private TextBox _txtKeyLogger = null!;
    private CheckBox _chkLogMouseMove = null!;
    private Button _btnClearLog = null!;
    private Label _lblLogStats = null!;
    private ListView _lvMessages = null!;

    private int _clickCount = 0;

    public TestTargetForm()
    {
        InitializeComponents();
        Application.AddMessageFilter(this);
    }

    private void InitializeComponents()
    {
        Text = "BackgroundClicker Test Target";
        ClientSize = new Size(950, 680);
        MinimumSize = new Size(800, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        // Header / Process Info
        _lblProcessInfo = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 0, 12, 0),
            BackColor = Color.FromArgb(240, 243, 246),
            Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point)
        };

        // GroupBox containing test interactive controls
        _grpControls = new GroupBox
        {
            Text = "Target Interactive Controls",
            Dock = DockStyle.Top,
            Height = 220,
            Padding = new Padding(12)
        };

        // Distinctly colored Panel inside GroupBox
        _testPanel = new Panel
        {
            Location = new Point(16, 28),
            Size = new Size(340, 110),
            BackColor = Color.FromArgb(220, 235, 252),
            BorderStyle = BorderStyle.FixedSingle
        };

        _testButton = new Button
        {
            Text = "Target Button (Click Me)",
            Location = new Point(16, 16),
            Size = new Size(180, 42),
            BackColor = Color.White,
            UseVisualStyleBackColor = true
        };
        _testButton.Click += OnTargetButtonClick;

        _panelLabel = new Label
        {
            Text = "Panel Control Area",
            Location = new Point(16, 68),
            AutoSize = true,
            ForeColor = Color.FromArgb(40, 60, 100)
        };

        _testPanel.Controls.Add(_testButton);
        _testPanel.Controls.Add(_panelLabel);

        // Click counter label
        _lblClickCounter = new Label
        {
            Text = "Clicks: 0",
            Location = new Point(380, 32),
            AutoSize = true,
            Font = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(16, 124, 65)
        };

        _btnResetCounter = new Button
        {
            Text = "Reset Counter",
            Location = new Point(380, 68),
            Size = new Size(130, 32),
            UseVisualStyleBackColor = true
        };
        _btnResetCounter.Click += (s, e) =>
        {
            _clickCount = 0;
            UpdateClickDisplay();
        };

        // Real-time Coordinate display
        _lblCoordinates = new Label
        {
            Text = "Cursor: (Screen: 0, 0 | Form Client: 0, 0 | Button Client: 0, 0)",
            Location = new Point(16, 150),
            AutoSize = true,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };

        // Keyboard logger input
        var lblKeyPrompt = new Label
        {
            Text = "Keyboard Event Test Area (Focus & Type):",
            Location = new Point(530, 28),
            AutoSize = true
        };

        _txtKeyLogger = new TextBox
        {
            Location = new Point(530, 52),
            Size = new Size(390, 86),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            PlaceholderText = "Click here and press keys to test WM_KEYDOWN / WM_CHAR..."
        };
        _txtKeyLogger.KeyDown += OnKeyLoggerKeyDown;
        _txtKeyLogger.KeyPress += OnKeyLoggerKeyPress;

        _grpControls.Controls.Add(_testPanel);
        _grpControls.Controls.Add(_lblClickCounter);
        _grpControls.Controls.Add(_btnResetCounter);
        _grpControls.Controls.Add(_lblCoordinates);
        _grpControls.Controls.Add(lblKeyPrompt);
        _grpControls.Controls.Add(_txtKeyLogger);

        // Raw Windows Message Log Section
        var pnlLogHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 40,
            Padding = new Padding(12, 6, 12, 6)
        };

        var lblLogTitle = new Label
        {
            Text = "Raw Windows Message Log (Bounded 1000):",
            Dock = DockStyle.Left,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft
        };

        _chkLogMouseMove = new CheckBox
        {
            Text = "Log WM_MOUSEMOVE",
            Dock = DockStyle.Right,
            AutoSize = true,
            Checked = false
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
            lock (_logLock)
            {
                _messageLog.Clear();
            }
            RefreshVirtualList();
        };

        _lblLogStats = new Label
        {
            Text = "Entries: 0 / 1000",
            Dock = DockStyle.Right,
            Width = 140,
            TextAlign = ContentAlignment.MiddleRight
        };

        pnlLogHeader.Controls.Add(lblLogTitle);
        pnlLogHeader.Controls.Add(_lblLogStats);
        pnlLogHeader.Controls.Add(_chkLogMouseMove);
        pnlLogHeader.Controls.Add(_btnClearLog);

        _lvMessages = new DoubleBufferedListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            VirtualMode = true,
            Font = new Font("Consolas", 8.5F, FontStyle.Regular, GraphicsUnit.Point)
        };

        _lvMessages.Columns.Add("Time", 100);
        _lvMessages.Columns.Add("Message", 190);
        _lvMessages.Columns.Add("Target HWND", 160);
        _lvMessages.Columns.Add("Target Control", 120);
        _lvMessages.Columns.Add("wParam", 90);
        _lvMessages.Columns.Add("lParam", 100);
        _lvMessages.Columns.Add("Decoded X,Y", 100);
        _lvMessages.Columns.Add("Thread ID", 80);

        _lvMessages.RetrieveVirtualItem += OnRetrieveVirtualItem;

        Controls.Add(_lvMessages);
        Controls.Add(pnlLogHeader);
        Controls.Add(_grpControls);
        Controls.Add(_lblProcessInfo);

        // Tracking cursor coordinates
        var coordTimer = new System.Windows.Forms.Timer { Interval = 50 };
        coordTimer.Tick += OnCoordTimerTick;
        coordTimer.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateHeaderInfo();
    }

    private void UpdateHeaderInfo()
    {
        uint pid = Kernel32.GetCurrentProcessId();
        uint tid = Kernel32.GetCurrentThreadId();
        _lblProcessInfo.Text = $"Process: BackgroundClicker.TestTarget.exe | PID: {pid} | Thread: {tid} | Form HWND: {HwndFormatter.Format(Handle)} | Button HWND: {HwndFormatter.Format(_testButton.Handle)} | Panel HWND: {HwndFormatter.Format(_testPanel.Handle)}";
    }

    private void OnTargetButtonClick(object? sender, EventArgs e)
    {
        _clickCount++;
        UpdateClickDisplay();
    }

    private void UpdateClickDisplay()
    {
        _lblClickCounter.Text = $"Clicks: {_clickCount}";
    }

    private void OnCoordTimerTick(object? sender, EventArgs e)
    {
        if (!IsHandleCreated)
            return;

        Point screen = Cursor.Position;
        Point formClient = PointToClient(screen);
        Point buttonClient = _testButton.PointToClient(screen);

        _lblCoordinates.Text = $"Cursor: Screen: ({screen.X}, {screen.Y}) | Form Client: ({formClient.X}, {formClient.Y}) | Button Client: ({buttonClient.X}, {buttonClient.Y})";
    }

    private void OnKeyLoggerKeyDown(object? sender, KeyEventArgs e)
    {
        // Handled via message filter
    }

    private void OnKeyLoggerKeyPress(object? sender, KeyPressEventArgs e)
    {
        // Handled via message filter
    }

    public bool PreFilterMessage(ref Message m)
    {
        uint msg = (uint)m.Msg;

        // Filter messages we care about
        bool isTargetMsg = msg switch
        {
            NativeConstants.WM_MOUSEMOVE => _chkLogMouseMove.Checked,
            NativeConstants.WM_LBUTTONDOWN or
            NativeConstants.WM_LBUTTONUP or
            NativeConstants.WM_LBUTTONDBLCLK or
            NativeConstants.WM_RBUTTONDOWN or
            NativeConstants.WM_RBUTTONUP or
            NativeConstants.WM_MBUTTONDOWN or
            NativeConstants.WM_MBUTTONUP or
            NativeConstants.WM_KEYDOWN or
            NativeConstants.WM_KEYUP or
            NativeConstants.WM_CHAR => true,
            _ => false
        };

        if (isTargetMsg)
        {
            LogRawMessage(m);
        }

        return false; // Do not consume
    }

    private void LogRawMessage(Message m)
    {
        uint msg = (uint)m.Msg;
        string msgName = GetMessageName(msg);
        IntPtr hwnd = m.HWnd;
        string targetName = ResolveControlName(hwnd);

        string wParamStr = $"0x{m.WParam.ToInt64():X4}";
        string lParamStr = $"0x{m.LParam.ToInt64():X8}";

        string decodedCoord = "-";
        if (msg >= NativeConstants.WM_MOUSEMOVE && msg <= NativeConstants.WM_MBUTTONDBLCLK)
        {
            short x = unchecked((short)(m.LParam.ToInt64() & 0xFFFF));
            short y = unchecked((short)((m.LParam.ToInt64() >> 16) & 0xFFFF));
            decodedCoord = $"({x}, {y})";
        }
        else if (msg is NativeConstants.WM_KEYDOWN or NativeConstants.WM_KEYUP)
        {
            var key = (Keys)(int)m.WParam;
            decodedCoord = $"Key: {key}";
        }
        else if (msg == NativeConstants.WM_CHAR)
        {
            char ch = (char)(int)m.WParam;
            decodedCoord = $"Char: '{ch}'";
        }

        var entry = new RawMessageEntry(
            DateTime.Now,
            msgName,
            hwnd,
            targetName,
            wParamStr,
            lParamStr,
            decodedCoord,
            Kernel32.GetCurrentThreadId());

        lock (_logLock)
        {
            if (_messageLog.Count >= MaxLogEntries)
            {
                _messageLog.RemoveAt(0);
            }
            _messageLog.Add(entry);
        }

        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(RefreshVirtualList);
        }
    }

    private void RefreshVirtualList()
    {
        lock (_logLock)
        {
            _lvMessages.VirtualListSize = _messageLog.Count;
            _lblLogStats.Text = $"Entries: {_messageLog.Count} / {MaxLogEntries}";
        }
        _lvMessages.Invalidate();
    }

    private void OnRetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        RawMessageEntry entry;
        lock (_logLock)
        {
            if (e.ItemIndex >= 0 && e.ItemIndex < _messageLog.Count)
            {
                entry = _messageLog[e.ItemIndex];
            }
            else
            {
                e.Item = new ListViewItem();
                return;
            }
        }

        var item = new ListViewItem(entry.Timestamp.ToString("HH:mm:ss.fff"));
        item.SubItems.Add(entry.MessageName);
        item.SubItems.Add(HwndFormatter.Format(entry.Hwnd));
        item.SubItems.Add(entry.TargetName);
        item.SubItems.Add(entry.WParam);
        item.SubItems.Add(entry.LParam);
        item.SubItems.Add(entry.Decoded);
        item.SubItems.Add(entry.ThreadId.ToString());

        e.Item = item;
    }

    private string ResolveControlName(IntPtr hwnd)
    {
        if (hwnd == Handle) return "Form";
        if (_testButton.IsHandleCreated && hwnd == _testButton.Handle) return "Button";
        if (_testPanel.IsHandleCreated && hwnd == _testPanel.Handle) return "Panel";
        if (_txtKeyLogger.IsHandleCreated && hwnd == _txtKeyLogger.Handle) return "TextBox";
        if (_lvMessages.IsHandleCreated && hwnd == _lvMessages.Handle) return "ListView";
        return "[Child Control]";
    }

    private static string GetMessageName(uint msg) => msg switch
    {
        NativeConstants.WM_MOUSEMOVE => "WM_MOUSEMOVE (0x0200)",
        NativeConstants.WM_LBUTTONDOWN => "WM_LBUTTONDOWN (0x0201)",
        NativeConstants.WM_LBUTTONUP => "WM_LBUTTONUP (0x0202)",
        NativeConstants.WM_LBUTTONDBLCLK => "WM_LBUTTONDBLCLK (0x0203)",
        NativeConstants.WM_RBUTTONDOWN => "WM_RBUTTONDOWN (0x0204)",
        NativeConstants.WM_RBUTTONUP => "WM_RBUTTONUP (0x0205)",
        NativeConstants.WM_MBUTTONDOWN => "WM_MBUTTONDOWN (0x0207)",
        NativeConstants.WM_MBUTTONUP => "WM_MBUTTONUP (0x0208)",
        NativeConstants.WM_KEYDOWN => "WM_KEYDOWN (0x0100)",
        NativeConstants.WM_KEYUP => "WM_KEYUP (0x0101)",
        NativeConstants.WM_CHAR => "WM_CHAR (0x0102)",
        _ => $"0x{msg:X4}"
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.RemoveMessageFilter(this);
        }
        base.Dispose(disposing);
    }
    private sealed class DoubleBufferedListView : ListView
    {
        public DoubleBufferedListView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
    }
}

public sealed record RawMessageEntry(
    DateTime Timestamp,
    string MessageName,
    IntPtr Hwnd,
    string TargetName,
    string WParam,
    string LParam,
    string Decoded,
    uint ThreadId);
