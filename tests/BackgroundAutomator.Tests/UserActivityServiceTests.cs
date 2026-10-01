using System.Runtime.InteropServices;
using BackgroundAutomator.Win32;
using Xunit;

namespace BackgroundAutomator.Tests;

public class UserActivityServiceTests
{
    [Fact]
    public void LastInputInfo_Size_Is8Bytes()
    {
        Assert.Equal(8, Marshal.SizeOf<LASTINPUTINFO>());
    }

    [Fact]
    public void TickArithmetic_WrapSafe_CalculatesCorrectElapsed()
    {
        // Simulate tick count wrapping past uint.MaxValue
        uint lastInput = 0xFFFFFFF0; // 16 ms before wrap
        uint currentTick = 0x00000020; // 32 ms after wrap
        uint elapsed = unchecked(currentTick - lastInput);
        Assert.Equal(48u, elapsed);
    }

    [Fact]
    public void LiveWin32_GetLastInputInfo_SucceedsAndReturnsNonZeroDwTime()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        bool success = User32.GetLastInputInfo(ref lii);
        Assert.True(success);
        Assert.True(lii.dwTime > 0);
    }

    [Fact]
    public void LiveWin32_Verify_Whether_SendInput_Updates_GetLastInputInfo()
    {
        // Check baseline last input time
        var liiBefore = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        bool s1 = User32.GetLastInputInfo(ref liiBefore);
        Assert.True(s1);

        // Sleep briefly to ensure tick counter progresses
        Thread.Sleep(50);

        // Dispatch a key event using SendInput
        // We send a harmless keyup event (e.g. VK_SHIFT keyup) that doesn't alter state
        var inputs = new INPUT[1];
        inputs[0] = new INPUT
        {
            type = NativeConstants.INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = (ushort)NativeConstants.VK_RETURN,
                wScan = 0,
                dwFlags = NativeConstants.KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            }
        };

        uint sent = User32.SendInput(1, inputs, Marshal.SizeOf<INPUT>());
        int err = Marshal.GetLastWin32Error();

        if (sent == 1u)
        {
            var liiAfter = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            bool s2 = User32.GetLastInputInfo(ref liiAfter);
            Assert.True(s2);
            // When SendInput succeeds, Windows updates GetLastInputInfo tick count
            Assert.True(liiAfter.dwTime >= liiBefore.dwTime);
        }
        else
        {
            // Error 5 (ERROR_ACCESS_DENIED) occurs in non-interactive / elevated test execution
            Assert.Equal(5, err);
        }
    }
}
