using System.Windows.Forms;
using BackgroundClicker.Core.Clicking;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;
using Xunit;

namespace BackgroundClicker.Tests;

public class BackgroundClickerEngineTests
{
    private sealed class MessageRecordingForm : Form
    {
        public List<Message> ReceivedMessages { get; } = new();

        public MessageRecordingForm()
        {
            SetStyle(ControlStyles.StandardDoubleClick, true);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg is (int)NativeConstants.WM_MOUSEMOVE or
                       (int)NativeConstants.WM_LBUTTONDOWN or
                       (int)NativeConstants.WM_LBUTTONUP or
                       (int)NativeConstants.WM_LBUTTONDBLCLK)
            {
                ReceivedMessages.Add(m);
            }
            base.WndProc(ref m);
        }
    }

    [Fact]
    public void Click_WithInvalidHwnd_ReturnsInvalidTarget()
    {
        var logger = new InMemoryLogger();
        var engine = new BackgroundClickerEngine(logger);

        var resultZero = engine.Click(new TargetPoint(IntPtr.Zero, 10, 10));
        Assert.Equal(ClickResult.InvalidTarget, resultZero);

        var resultBogus = engine.Click(new TargetPoint(new IntPtr(0x7FFFFFFF), 10, 10));
        Assert.Equal(ClickResult.InvalidTarget, resultBogus);
    }

    [Fact]
    public void DoubleClick_WithInvalidHwnd_ReturnsInvalidTarget()
    {
        var logger = new InMemoryLogger();
        var engine = new BackgroundClickerEngine(logger);

        var resultZero = engine.DoubleClick(new TargetPoint(IntPtr.Zero, 10, 10));
        Assert.Equal(ClickResult.InvalidTarget, resultZero);
    }

    [Fact]
    public void Click_SendsExactSingleClickMessageSequence_WithCorrectWParamAndLParam()
    {
        using var form = new MessageRecordingForm
        {
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(200, 200),
            Size = new System.Drawing.Size(300, 200),
            ShowInTaskbar = false
        };
        form.Show();
        Application.DoEvents();

        var engine = new BackgroundClickerEngine();
        const int targetX = 55;
        const int targetY = 75;
        var target = new TargetPoint(form.Handle, targetX, targetY);

        var result = engine.Click(target);
        Assert.Equal(ClickResult.Success, result);

        // Process message queue
        Application.DoEvents();

        Assert.Equal(3, form.ReceivedMessages.Count);

        // 1. WM_MOUSEMOVE
        var msgMove = form.ReceivedMessages[0];
        Assert.Equal((int)NativeConstants.WM_MOUSEMOVE, msgMove.Msg);
        Assert.Equal(IntPtr.Zero, msgMove.WParam);
        Assert.Equal(targetX, MouseMessageHelper.GetMouseX(msgMove.LParam));
        Assert.Equal(targetY, MouseMessageHelper.GetMouseY(msgMove.LParam));

        // 2. WM_LBUTTONDOWN with MK_LBUTTON
        var msgDown = form.ReceivedMessages[1];
        Assert.Equal((int)NativeConstants.WM_LBUTTONDOWN, msgDown.Msg);
        Assert.Equal((IntPtr)NativeConstants.MK_LBUTTON, msgDown.WParam);
        Assert.Equal(targetX, MouseMessageHelper.GetMouseX(msgDown.LParam));
        Assert.Equal(targetY, MouseMessageHelper.GetMouseY(msgDown.LParam));

        // 3. WM_LBUTTONUP with 0 wParam
        var msgUp = form.ReceivedMessages[2];
        Assert.Equal((int)NativeConstants.WM_LBUTTONUP, msgUp.Msg);
        Assert.Equal(IntPtr.Zero, msgUp.WParam);
        Assert.Equal(targetX, MouseMessageHelper.GetMouseX(msgUp.LParam));
        Assert.Equal(targetY, MouseMessageHelper.GetMouseY(msgUp.LParam));

        form.Close();
    }

    [Fact]
    public void DoubleClick_SendsExactFiveMessageSequence_WithCorrectWParamAndLParam()
    {
        using var form = new MessageRecordingForm
        {
            StartPosition = FormStartPosition.Manual,
            Location = new System.Drawing.Point(200, 200),
            Size = new System.Drawing.Size(300, 200),
            ShowInTaskbar = false
        };
        form.Show();
        Application.DoEvents();

        var engine = new BackgroundClickerEngine();
        const int targetX = 120;
        const int targetY = 85;
        var target = new TargetPoint(form.Handle, targetX, targetY);

        var result = engine.DoubleClick(target);
        Assert.Equal(ClickResult.Success, result);

        Application.DoEvents();

        Assert.Equal(5, form.ReceivedMessages.Count);

        // 1. WM_MOUSEMOVE
        Assert.Equal((int)NativeConstants.WM_MOUSEMOVE, form.ReceivedMessages[0].Msg);
        Assert.Equal(IntPtr.Zero, form.ReceivedMessages[0].WParam);
        Assert.Equal(targetX, MouseMessageHelper.GetMouseX(form.ReceivedMessages[0].LParam));
        Assert.Equal(targetY, MouseMessageHelper.GetMouseY(form.ReceivedMessages[0].LParam));

        // 2. WM_LBUTTONDOWN (MK_LBUTTON)
        Assert.Equal((int)NativeConstants.WM_LBUTTONDOWN, form.ReceivedMessages[1].Msg);
        Assert.Equal((IntPtr)NativeConstants.MK_LBUTTON, form.ReceivedMessages[1].WParam);

        // 3. WM_LBUTTONUP
        Assert.Equal((int)NativeConstants.WM_LBUTTONUP, form.ReceivedMessages[2].Msg);
        Assert.Equal(IntPtr.Zero, form.ReceivedMessages[2].WParam);

        // 4. WM_LBUTTONDBLCLK (MK_LBUTTON)
        Assert.Equal((int)NativeConstants.WM_LBUTTONDBLCLK, form.ReceivedMessages[3].Msg);
        Assert.Equal((IntPtr)NativeConstants.MK_LBUTTON, form.ReceivedMessages[3].WParam);
        Assert.Equal(targetX, MouseMessageHelper.GetMouseX(form.ReceivedMessages[3].LParam));
        Assert.Equal(targetY, MouseMessageHelper.GetMouseY(form.ReceivedMessages[3].LParam));

        // 5. WM_LBUTTONUP
        Assert.Equal((int)NativeConstants.WM_LBUTTONUP, form.ReceivedMessages[4].Msg);
        Assert.Equal(IntPtr.Zero, form.ReceivedMessages[4].WParam);

        form.Close();
    }
}
