using System.Runtime.InteropServices;

namespace BackgroundAutomator.Win32;

/// <summary>
/// Helper for generating low-level keyboard input using Win32 SendInput.
/// </summary>
public static class SendInputHelper
{
    /// <summary>
    /// Dispatches a key down followed by key up event for the specified virtual key.
    /// </summary>
    /// <param name="virtualKey">Virtual key code (e.g. VK_RETURN).</param>
    /// <returns>True if both events were successfully inserted into the input stream.</returns>
    public static bool SendKeyDownUp(ushort virtualKey)
    {
        ushort scanCode = (ushort)User32.MapVirtualKey(virtualKey, NativeConstants.MAPVK_VK_TO_VSC);

        var inputs = new INPUT[2];

        // Key Down
        inputs[0] = new INPUT
        {
            type = NativeConstants.INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = virtualKey,
                wScan = scanCode,
                dwFlags = NativeConstants.KEYEVENTF_KEYDOWN,
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            }
        };

        // Key Up
        inputs[1] = new INPUT
        {
            type = NativeConstants.INPUT_KEYBOARD,
            ki = new KEYBDINPUT
            {
                wVk = virtualKey,
                wScan = scanCode,
                dwFlags = NativeConstants.KEYEVENTF_KEYUP,
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            }
        };

        uint sent = User32.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    /// <summary>
    /// Dispatches an Enter key press (down then up).
    /// </summary>
    public static bool SendEnter() => SendKeyDownUp((ushort)NativeConstants.VK_RETURN);
}
