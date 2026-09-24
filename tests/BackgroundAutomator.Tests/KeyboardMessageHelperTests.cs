using BackgroundAutomator.Core.Keyboard;
using BackgroundAutomator.Win32;
using Xunit;

namespace BackgroundAutomator.Tests;

public class KeyboardMessageHelperTests
{
    [Fact]
    public void EnterKey_KeyDown_LParam_HasCorrectBits()
    {
        // VK_RETURN = 0x0D, Scan code = 0x1C (28)
        IntPtr lParam = KeyboardMessageHelper.MakeKeyDownLParam(NativeConstants.VK_RETURN);

        // Repeat count should be 1
        Assert.Equal(1, KeyboardMessageHelper.GetRepeatCount(lParam));

        // Scan code should be 0x1C
        Assert.Equal(0x1C, KeyboardMessageHelper.GetScanCodeFromLParam(lParam));

        // Enter is not an extended key
        Assert.False(KeyboardMessageHelper.IsExtendedKeyLParam(lParam));

        // Context code should be 0
        Assert.False(KeyboardMessageHelper.GetContextCode(lParam));

        // Previous key state should be 0 (key was up before WM_KEYDOWN)
        Assert.False(KeyboardMessageHelper.GetPreviousKeyState(lParam));

        // Transition state should be 0 (key is being pressed)
        Assert.False(KeyboardMessageHelper.GetTransitionState(lParam));

        // Expected bit pattern: 1 | (0x1C << 16) = 0x001C0001
        uint rawBits = KeyboardMessageHelper.MakeKeyLParamBits(0x1C, isExtended: false, previousStateDown: false, transitionStateUp: false, repeatCount: 1);
        Assert.Equal(0x001C0001U, rawBits);
        Assert.Equal(unchecked((int)0x001C0001U), (int)lParam);
    }

    [Fact]
    public void EnterKey_KeyUp_LParam_HasCorrectBits()
    {
        IntPtr lParam = KeyboardMessageHelper.MakeKeyUpLParam(NativeConstants.VK_RETURN);

        // Repeat count should be 1
        Assert.Equal(1, KeyboardMessageHelper.GetRepeatCount(lParam));

        // Scan code should be 0x1C
        Assert.Equal(0x1C, KeyboardMessageHelper.GetScanCodeFromLParam(lParam));

        // Enter is not an extended key
        Assert.False(KeyboardMessageHelper.IsExtendedKeyLParam(lParam));

        // Context code should be 0
        Assert.False(KeyboardMessageHelper.GetContextCode(lParam));

        // Previous key state should be 1 (key was previously down before WM_KEYUP)
        Assert.True(KeyboardMessageHelper.GetPreviousKeyState(lParam));

        // Transition state should be 1 (key is being released)
        Assert.True(KeyboardMessageHelper.GetTransitionState(lParam));

        // Expected bit pattern: 1 | (0x1C << 16) | (1 << 30) | (1 << 31) = 0xC01C0001
        uint rawBits = KeyboardMessageHelper.MakeKeyLParamBits(0x1C, isExtended: false, previousStateDown: true, transitionStateUp: true, repeatCount: 1);
        Assert.Equal(0xC01C0001U, rawBits);
        Assert.Equal(unchecked((int)0xC01C0001U), (int)lParam);
    }

    [Theory]
    [InlineData(BackgroundKey.Enter, NativeConstants.VK_RETURN)]
    [InlineData(BackgroundKey.Tab, NativeConstants.VK_TAB)]
    [InlineData(BackgroundKey.Escape, NativeConstants.VK_ESCAPE)]
    [InlineData(BackgroundKey.Space, NativeConstants.VK_SPACE)]
    [InlineData(BackgroundKey.ArrowUp, NativeConstants.VK_UP)]
    [InlineData(BackgroundKey.ArrowDown, NativeConstants.VK_DOWN)]
    [InlineData(BackgroundKey.ArrowLeft, NativeConstants.VK_LEFT)]
    [InlineData(BackgroundKey.ArrowRight, NativeConstants.VK_RIGHT)]
    [InlineData(BackgroundKey.Backspace, NativeConstants.VK_BACK)]
    [InlineData(BackgroundKey.Delete, NativeConstants.VK_DELETE)]
    [InlineData(BackgroundKey.Home, NativeConstants.VK_HOME)]
    [InlineData(BackgroundKey.End, NativeConstants.VK_END)]
    [InlineData(BackgroundKey.PageUp, NativeConstants.VK_PRIOR)]
    [InlineData(BackgroundKey.PageDown, NativeConstants.VK_NEXT)]
    public void BackgroundKey_MapsToCorrectVirtualKey(BackgroundKey key, uint expectedVk)
    {
        Assert.Equal(expectedVk, key.ToVirtualKey());
    }

    [Theory]
    [InlineData(NativeConstants.VK_UP, true)]
    [InlineData(NativeConstants.VK_DOWN, true)]
    [InlineData(NativeConstants.VK_LEFT, true)]
    [InlineData(NativeConstants.VK_RIGHT, true)]
    [InlineData(NativeConstants.VK_HOME, true)]
    [InlineData(NativeConstants.VK_END, true)]
    [InlineData(NativeConstants.VK_PRIOR, true)]
    [InlineData(NativeConstants.VK_NEXT, true)]
    [InlineData(NativeConstants.VK_DELETE, true)]
    [InlineData(NativeConstants.VK_RETURN, false)]
    [InlineData(NativeConstants.VK_TAB, false)]
    [InlineData(NativeConstants.VK_ESCAPE, false)]
    [InlineData(NativeConstants.VK_SPACE, false)]
    [InlineData(NativeConstants.VK_BACK, false)]
    public void ExtendedKey_Handling_MatchesExpectations(uint vk, bool expectedExtended)
    {
        Assert.Equal(expectedExtended, KeyboardMessageHelper.IsExtendedKey(vk));

        IntPtr downLParam = KeyboardMessageHelper.MakeKeyDownLParam(vk);
        Assert.Equal(expectedExtended, KeyboardMessageHelper.IsExtendedKeyLParam(downLParam));

        IntPtr upLParam = KeyboardMessageHelper.MakeKeyUpLParam(vk);
        Assert.Equal(expectedExtended, KeyboardMessageHelper.IsExtendedKeyLParam(upLParam));
    }

    [Fact]
    public void ArrowDown_KeyDown_LParam_HasExtendedBitSet()
    {
        IntPtr lParam = KeyboardMessageHelper.MakeKeyDownLParam(NativeConstants.VK_DOWN);

        Assert.True(KeyboardMessageHelper.IsExtendedKeyLParam(lParam));
        Assert.False(KeyboardMessageHelper.GetPreviousKeyState(lParam));
        Assert.False(KeyboardMessageHelper.GetTransitionState(lParam));

        // Scan code for Down Arrow is 0x50
        Assert.Equal(0x50, KeyboardMessageHelper.GetScanCodeFromLParam(lParam));
        Assert.Equal(1, KeyboardMessageHelper.GetRepeatCount(lParam));
    }

    [Fact]
    public void CustomRepeatCount_IsProperlyEncoded()
    {
        IntPtr lParam = KeyboardMessageHelper.MakeKeyDownLParam(NativeConstants.VK_RETURN, repeatCount: 42);
        Assert.Equal(42, KeyboardMessageHelper.GetRepeatCount(lParam));
    }
}
