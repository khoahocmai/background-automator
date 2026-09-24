namespace BackgroundAutomator.Core.Keyboard;

/// <summary>
/// Abstraction for delivering background keyboard events directly to target HWNDs
/// via Win32 message posting without moving the mouse or stealing window focus.
/// </summary>
public interface IBackgroundKeyboard
{
    /// <summary>
    /// Delivers a background key-press message sequence (WM_KEYDOWN -> WM_KEYUP)
    /// to the specified target HWND.
    /// </summary>
    /// <param name="hwnd">The window handle to receive the key press.</param>
    /// <param name="key">The background key to press.</param>
    /// <returns>A <see cref="KeyPressResult"/> indicating the delivery outcome.</returns>
    KeyPressResult PressKey(IntPtr hwnd, BackgroundKey key);

    /// <summary>
    /// Delivers a background key-press message sequence (WM_KEYDOWN -> WM_KEYUP)
    /// to the specified target HWND using an explicit Win32 virtual-key code.
    /// </summary>
    /// <param name="hwnd">The window handle to receive the key press.</param>
    /// <param name="virtualKey">The virtual key code to press.</param>
    /// <returns>A <see cref="KeyPressResult"/> indicating the delivery outcome.</returns>
    KeyPressResult PressKey(IntPtr hwnd, uint virtualKey);
}
