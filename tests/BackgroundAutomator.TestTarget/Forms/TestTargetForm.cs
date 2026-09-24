using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.TestTarget.Forms;

public sealed class TestTargetForm : Form, IMessageFilter
{
    private readonly List<RawMessageEntry> _messageLog = new();
    private readonly object _logLock = new();
    private const int MaxLogEntries = 1000;

    private readonly string? _logFilePath;
    private readonly bool _initialLogMouseMove;

    // UI Controls
    private Label _lblProcessInfo = null!;
    private GroupBox _grpControls = null!;
    private Panel _testPanel = null!;
    private Button _testButton = null!;
    private Label _panelLabel = null!;
    private Label _lblSingleClicks = null!;
    private Label _lblDoubleClicks = null!;
    private Label _lblLastClick = null!;
    private Label _lblLastMessage = null!;
    private Button _btnResetCounter = null!;
    private Label _lblCoordinates = null!;
    private TextBox _txtKeyLogger = null!;
    private CheckBox _chkLogMouseMove = null!;
    private Button _btnClearLog = null!;
    private Label _lblLogStats = null!;
    private ListView _lvMessages = null!;

    // Color Test Panel Controls
    private Panel _colorPanel = null!;
    private Label _lblColorInfo = null!;
    private Button _btnSetRed = null!;
    private Button _btnSetGreen = null!;
    private Button _btnSetBlue = null!;
    private Button _btnDelayGreen = null!;

    private Color? _initialColor;
    private int _delayedChangeMs;
    private Color? _delayedColor;

    private int _singleClickCount = 0;
    private int _doubleClickCount = 0;
    private int _lastClickX = -1;
    private int _lastClickY = -1;
    private string _lastMessage = "None";

    public TestTargetForm(string[]? args = null)
    {
        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--log-file", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    _logFilePath = args[i + 1];
                    i++;
                }
                else if (string.Equals(args[i], "--log-mouse-move", StringComparison.OrdinalIgnoreCase))
                {
                    _initialLogMouseMove = true;
                }
                else if (string.Equals(args[i], "--initial-color", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    _initialColor = ParseColor(args[++i]);
                }
                else if (string.Equals(args[i], "--change-color-after", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    string spec = args[++i];
                    int colon = spec.IndexOf(':');
                    if (colon > 0 && int.TryParse(spec.Substring(0, colon), out int ms))
                    {
                        _delayedChangeMs = ms;
                        _delayedColor = ParseColor(spec.Substring(colon + 1));
                    }
                }
            }
        }

        InitializeComponents();
        if (_initialLogMouseMove && _chkLogMouseMove != null)
        {
            _chkLogMouseMove.Checked = true;
        }
        Application.AddMessageFilter(this);
    }

    private void InitializeComponents()
    {
        Text = "BackgroundAutomator Test Target";
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
            Height = 255,
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

        // Click counter labels
        _lblSingleClicks = new Label
        {
            Text = "Single Clicks: 0",
            Location = new Point(370, 24),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(16, 124, 65)
        };

        _lblDoubleClicks = new Label
        {
            Text = "Double Clicks: 0",
            Location = new Point(370, 46),
            AutoSize = true,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(0, 100, 180)
        };

        _lblLastClick = new Label
        {
            Text = "Last Click: X: - | Y: -",
            Location = new Point(370, 68),
            AutoSize = true,
            Font = new Font("Consolas", 8.8F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(60, 60, 60)
        };

        _lblLastMessage = new Label
        {
            Text = "Last Msg: None",
            Location = new Point(370, 88),
            AutoSize = true,
            Font = new Font("Consolas", 8.8F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = Color.FromArgb(60, 60, 60)
        };

        _btnResetCounter = new Button
        {
            Text = "Reset Counters",
            Location = new Point(370, 112),
            Size = new Size(130, 26),
            UseVisualStyleBackColor = true
        };
        _btnResetCounter.Click += (s, e) =>
        {
            _singleClickCount = 0;
            _doubleClickCount = 0;
            _lastClickX = -1;
            _lastClickY = -1;
            _lastMessage = "Reset";
            UpdateClickDisplay();
        };

        // Real-time Coordinate display
        _lblCoordinates = new Label
        {
            Text = "Cursor: (Screen: 0, 0 | Form Client: 0, 0 | Button Client: 0, 0)",
            Location = new Point(16, 142),
            AutoSize = true,
            Font = new Font("Consolas", 9F, FontStyle.Regular, GraphicsUnit.Point)
        };

        // Solid Color Test Panel & Controls
        _colorPanel = new Panel
        {
            Name = "ColorPanel",
            Location = new Point(16, 170),
            Size = new Size(110, 50),
            BackColor = _initialColor ?? Color.FromArgb(255, 0, 0),
            BorderStyle = BorderStyle.FixedSingle
        };

        _lblColorInfo = new Label
        {
            Text = $"Color: RGB({_colorPanel.BackColor.R}, {_colorPanel.BackColor.G}, {_colorPanel.BackColor.B})",
            Location = new Point(135, 170),
            AutoSize = true,
            Font = new Font("Consolas", 8.8F, FontStyle.Bold, GraphicsUnit.Point)
        };

        _btnSetRed = new Button
        {
            Text = "Set Red",
            Location = new Point(135, 192),
            Size = new Size(72, 28),
            UseVisualStyleBackColor = true
        };
        _btnSetRed.Click += (s, e) => SetPanelColor(Color.FromArgb(255, 0, 0));

        _btnSetGreen = new Button
        {
            Text = "Set Green",
            Location = new Point(212, 192),
            Size = new Size(75, 28),
            UseVisualStyleBackColor = true
        };
        _btnSetGreen.Click += (s, e) => SetPanelColor(Color.FromArgb(0, 255, 0));

        _btnSetBlue = new Button
        {
            Text = "Set Blue",
            Location = new Point(292, 192),
            Size = new Size(72, 28),
            UseVisualStyleBackColor = true
        };
        _btnSetBlue.Click += (s, e) => SetPanelColor(Color.FromArgb(0, 0, 255));

        _btnDelayGreen = new Button
        {
            Text = "Delay Green 400ms",
            Location = new Point(369, 192),
            Size = new Size(130, 28),
            UseVisualStyleBackColor = true
        };
        _btnDelayGreen.Click += (s, e) => ChangeColorAfter(Color.FromArgb(0, 255, 0), 400);

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
        _grpControls.Controls.Add(_lblSingleClicks);
        _grpControls.Controls.Add(_lblDoubleClicks);
        _grpControls.Controls.Add(_lblLastClick);
        _grpControls.Controls.Add(_lblLastMessage);
        _grpControls.Controls.Add(_btnResetCounter);
        _grpControls.Controls.Add(_lblCoordinates);
        _grpControls.Controls.Add(_colorPanel);
        _grpControls.Controls.Add(_lblColorInfo);
        _grpControls.Controls.Add(_btnSetRed);
        _grpControls.Controls.Add(_btnSetGreen);
        _grpControls.Controls.Add(_btnSetBlue);
        _grpControls.Controls.Add(_btnDelayGreen);
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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_delayedChangeMs > 0 && _delayedColor.HasValue)
        {
            ChangeColorAfter(_delayedColor.Value, _delayedChangeMs);
        }
    }

    public void SetPanelColor(Color color)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetPanelColor(color));
            return;
        }

        _colorPanel.BackColor = color;
        _lblColorInfo.Text = $"Color: RGB({color.R}, {color.G}, {color.B})";
    }

    public void ChangeColorAfter(Color color, int delayMs)
    {
        Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            SetPanelColor(color);
        });
    }

    private void UpdateHeaderInfo()
    {
        uint pid = Kernel32.GetCurrentProcessId();
        uint tid = Kernel32.GetCurrentThreadId();
        _lblProcessInfo.Text = $"Process: BackgroundAutomator.TestTarget.exe | PID: {pid} | Thread: {tid} | Form HWND: {HwndFormatter.Format(Handle)} | Button HWND: {HwndFormatter.Format(_testButton.Handle)} | Panel HWND: {HwndFormatter.Format(_testPanel.Handle)} | Color HWND: {HwndFormatter.Format(_colorPanel.Handle)} | KeyLogger HWND: {HwndFormatter.Format(_txtKeyLogger.Handle)}";
    }

    private void OnTargetButtonClick(object? sender, EventArgs e)
    {
        _singleClickCount++;
        UpdateClickDisplay();
    }

    private void UpdateClickDisplay()
    {
        if (InvokeRequired)
        {
            BeginInvoke(UpdateClickDisplay);
            return;
        }

        _lblSingleClicks.Text = $"Single Clicks: {_singleClickCount}";
        _lblDoubleClicks.Text = $"Double Clicks: {_doubleClickCount}";
        _lblLastClick.Text = _lastClickX >= 0 ? $"Last Click: X: {_lastClickX} | Y: {_lastClickY}" : "Last Click: X: - | Y: -";
        _lblLastMessage.Text = $"Last Msg: {_lastMessage}";
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
            if (_testButton.IsHandleCreated && m.HWnd == _testButton.Handle)
            {
                if (msg == NativeConstants.WM_LBUTTONDOWN)
                {
                    _lastClickX = MouseMessageHelper.GetMouseX(m.LParam);
                    _lastClickY = MouseMessageHelper.GetMouseY(m.LParam);
                    _lastMessage = "WM_LBUTTONDOWN";
                    UpdateClickDisplay();
                }
                else if (msg == NativeConstants.WM_LBUTTONDBLCLK)
                {
                    _doubleClickCount++;
                    _lastClickX = MouseMessageHelper.GetMouseX(m.LParam);
                    _lastClickY = MouseMessageHelper.GetMouseY(m.LParam);
                    _lastMessage = "WM_LBUTTONDBLCLK";
                    UpdateClickDisplay();
                }
                else if (msg == NativeConstants.WM_LBUTTONUP)
                {
                    _lastMessage = "WM_LBUTTONUP";
                    UpdateClickDisplay();
                }
            }

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

        if (!string.IsNullOrEmpty(_logFilePath))
        {
            try
            {
                string line = $"{entry.Timestamp:O}\t{entry.MessageName}\t{entry.Hwnd.ToInt64():X16}\t{entry.TargetName}\t{entry.WParam}\t{entry.LParam}\t{entry.Decoded}\t{entry.ThreadId}{Environment.NewLine}";
                using var fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(fs);
                writer.Write(line);
                writer.Flush();
            }
            catch
            {
                // Best effort log file output for test automation
            }
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
        if (_colorPanel.IsHandleCreated && hwnd == _colorPanel.Handle) return "ColorPanel";
        if (_btnSetRed.IsHandleCreated && hwnd == _btnSetRed.Handle) return "BtnSetRed";
        if (_btnSetGreen.IsHandleCreated && hwnd == _btnSetGreen.Handle) return "BtnSetGreen";
        if (_btnSetBlue.IsHandleCreated && hwnd == _btnSetBlue.Handle) return "BtnSetBlue";
        if (_btnDelayGreen.IsHandleCreated && hwnd == _btnDelayGreen.Handle) return "BtnDelayGreen";
        return "[Child Control]";
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_APP_SET_COLOR = 0x8000 + 101;
        if (m.Msg == WM_APP_SET_COLOR)
        {
            int rgb = (int)m.LParam.ToInt64();
            int r = (rgb >> 16) & 0xFF;
            int g = (rgb >> 8) & 0xFF;
            int b = rgb & 0xFF;
            SetPanelColor(Color.FromArgb(r, g, b));
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    private static Color ParseColor(string str)
    {
        if (string.Equals(str, "red", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 0, 0);
        if (string.Equals(str, "green", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(0, 255, 0);
        if (string.Equals(str, "blue", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(0, 0, 255);
        if (string.Equals(str, "white", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 255, 255);
        if (string.Equals(str, "black", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(0, 0, 0);
        if (string.Equals(str, "yellow", StringComparison.OrdinalIgnoreCase)) return Color.FromArgb(255, 255, 0);
        if (str.StartsWith("#") && str.Length == 7)
        {
            int rgb = Convert.ToInt32(str[1..], 16);
            return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }
        return Color.FromName(str);
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
