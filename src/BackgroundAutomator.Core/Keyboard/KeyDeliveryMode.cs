namespace BackgroundAutomator.Core.Keyboard;

/// <summary>
/// Delivery strategy for dispatching keyboard input to target windows.
/// </summary>
public enum KeyDeliveryMode
{
    /// <summary>
    /// Standard Win32 background PostMessage (WM_KEYDOWN / WM_KEYUP).
    /// Default for standard Win32 / WinForms controls without taking focus.
    /// </summary>
    BackgroundPostMessage,

    /// <summary>
    /// Controlled brief foreground pulse via SetForegroundWindow + SendInput.
    /// Used for modern terminal surfaces (e.g. Windows Terminal / ConPTY) that do not accept background PostMessage.
    /// Restores previous foreground window after delivery.
    /// </summary>
    ForegroundPulse
}
