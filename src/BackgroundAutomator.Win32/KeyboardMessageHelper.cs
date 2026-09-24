namespace BackgroundAutomator.Win32;

/// <summary>
/// Helper for encoding and decoding Win32 keyboard message parameters (WM_KEYDOWN, WM_KEYUP).
/// Accurately constructs LPARAM bitfields including repeat count, scan code, extended-key flag,
/// previous key state, and transition state.
/// </summary>
public static class KeyboardMessageHelper
{
    /// <summary>
    /// Translates a virtual-key code to its corresponding OEM scan code using User32.MapVirtualKey
    /// with fallback to standard US/PC scancodes if unmapped.
    /// </summary>
    public static uint GetScanCode(uint virtualKey)
    {
        uint scanCode = User32.MapVirtualKey(virtualKey, NativeConstants.MAPVK_VK_TO_VSC);
        if (scanCode != 0)
        {
            return scanCode;
        }

        return virtualKey switch
        {
            NativeConstants.VK_BACK => 0x0E,
            NativeConstants.VK_TAB => 0x0F,
            NativeConstants.VK_RETURN => 0x1C,
            NativeConstants.VK_ESCAPE => 0x01,
            NativeConstants.VK_SPACE => 0x39,
            NativeConstants.VK_PRIOR => 0x49, // PageUp
            NativeConstants.VK_NEXT => 0x51,  // PageDown
            NativeConstants.VK_END => 0x4F,
            NativeConstants.VK_HOME => 0x47,
            NativeConstants.VK_LEFT => 0x4B,
            NativeConstants.VK_UP => 0x48,
            NativeConstants.VK_RIGHT => 0x4D,
            NativeConstants.VK_DOWN => 0x50,
            NativeConstants.VK_DELETE => 0x53,
            _ => 0
        };
    }

    /// <summary>
    /// Determines whether the specified virtual-key represents an extended key
    /// (such as navigation arrow keys, Delete, Home, End, PageUp, PageDown).
    /// </summary>
    public static bool IsExtendedKey(uint virtualKey)
    {
        return virtualKey switch
        {
            NativeConstants.VK_UP => true,
            NativeConstants.VK_DOWN => true,
            NativeConstants.VK_LEFT => true,
            NativeConstants.VK_RIGHT => true,
            NativeConstants.VK_HOME => true,
            NativeConstants.VK_END => true,
            NativeConstants.VK_PRIOR => true,
            NativeConstants.VK_NEXT => true,
            NativeConstants.VK_DELETE => true,
            _ => false
        };
    }

    /// <summary>
    /// Packs keyboard message parameters into a 32-bit unsigned bitfield:
    /// - Bits 00-15: Repeat count
    /// - Bits 16-23: OEM scan code
    /// - Bit 24:     Extended key flag
    /// - Bits 25-28: Reserved (0)
    /// - Bit 29:     Context code (0 for WM_KEYDOWN/WM_KEYUP)
    /// - Bit 30:     Previous key state (0 if up before message, 1 if down)
    /// - Bit 31:     Transition state (0 for down/pressed, 1 for up/released)
    /// </summary>
    public static uint MakeKeyLParamBits(
        uint scanCode,
        bool isExtended,
        bool previousStateDown,
        bool transitionStateUp,
        ushort repeatCount = 1)
    {
        return (uint)(repeatCount & 0xFFFF)
             | ((scanCode & 0xFF) << 16)
             | (isExtended ? (1U << 24) : 0U)
             | (previousStateDown ? (1U << 30) : 0U)
             | (transitionStateUp ? (1U << 31) : 0U);
    }

    /// <summary>
    /// Constructs a native IntPtr LPARAM for a keyboard message.
    /// </summary>
    public static IntPtr MakeKeyLParam(
        uint scanCode,
        bool isExtended,
        bool previousStateDown,
        bool transitionStateUp,
        ushort repeatCount = 1)
    {
        uint bits = MakeKeyLParamBits(scanCode, isExtended, previousStateDown, transitionStateUp, repeatCount);
        return unchecked((IntPtr)(int)bits);
    }

    /// <summary>
    /// Constructs the proper LPARAM for a WM_KEYDOWN message.
    /// Bit 30 (previous key state) is 0 (key was up).
    /// Bit 31 (transition state) is 0 (key is being pressed).
    /// </summary>
    public static IntPtr MakeKeyDownLParam(uint virtualKey, ushort repeatCount = 1)
    {
        uint scanCode = GetScanCode(virtualKey);
        bool isExtended = IsExtendedKey(virtualKey);
        return MakeKeyLParam(scanCode, isExtended, previousStateDown: false, transitionStateUp: false, repeatCount);
    }

    /// <summary>
    /// Constructs the proper LPARAM for a WM_KEYUP message.
    /// Bit 30 (previous key state) is 1 (key was previously down).
    /// Bit 31 (transition state) is 1 (key is being released).
    /// </summary>
    public static IntPtr MakeKeyUpLParam(uint virtualKey, ushort repeatCount = 1)
    {
        uint scanCode = GetScanCode(virtualKey);
        bool isExtended = IsExtendedKey(virtualKey);
        return MakeKeyLParam(scanCode, isExtended, previousStateDown: true, transitionStateUp: true, repeatCount);
    }

    /// <summary>
    /// Extracts the repeat count (bits 0-15) from a keyboard message LPARAM.
    /// </summary>
    public static ushort GetRepeatCount(IntPtr lParam)
    {
        return unchecked((ushort)((long)lParam & 0xFFFF));
    }

    /// <summary>
    /// Extracts the 8-bit scan code (bits 16-23) from a keyboard message LPARAM.
    /// </summary>
    public static byte GetScanCodeFromLParam(IntPtr lParam)
    {
        return unchecked((byte)(((long)lParam >> 16) & 0xFF));
    }

    /// <summary>
    /// Determines whether the extended key flag (bit 24) is set in a keyboard message LPARAM.
    /// </summary>
    public static bool IsExtendedKeyLParam(IntPtr lParam)
    {
        return (((long)lParam >> 24) & 1) != 0;
    }

    /// <summary>
    /// Determines whether the context code (bit 29) is set in a keyboard message LPARAM.
    /// </summary>
    public static bool GetContextCode(IntPtr lParam)
    {
        return (((long)lParam >> 29) & 1) != 0;
    }

    /// <summary>
    /// Extracts the previous key state (bit 30) from a keyboard message LPARAM.
    /// True if the key was down before the message, False if up.
    /// </summary>
    public static bool GetPreviousKeyState(IntPtr lParam)
    {
        return (((long)lParam >> 30) & 1) != 0;
    }

    /// <summary>
    /// Extracts the transition state (bit 31) from a keyboard message LPARAM.
    /// True if the key is being released (WM_KEYUP), False if being pressed (WM_KEYDOWN).
    /// </summary>
    public static bool GetTransitionState(IntPtr lParam)
    {
        return (((long)lParam >> 31) & 1) != 0;
    }
}
