using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.Targeting;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.Core.Keyboard;

/// <summary>
/// Production engine delivering background keyboard messages to target HWNDs via Win32 PostMessage.
/// Guarantees that neither the hardware cursor position nor the foreground active window focus
/// is modified or hijacked.
/// </summary>
public sealed class BackgroundKeyboardEngine : IBackgroundKeyboard
{
    private readonly IAppLogger? _logger;

    /// <summary>
    /// Milliseconds to delay between discrete posted Win32 keyboard messages (between WM_KEYDOWN and WM_KEYUP).
    /// Default is 0 (no delay).
    /// </summary>
    public int KeyPacingMilliseconds { get; set; }

    public BackgroundKeyboardEngine(IAppLogger? logger = null, int keyPacingMilliseconds = 0)
    {
        _logger = logger;
        KeyPacingMilliseconds = keyPacingMilliseconds;
    }

    /// <inheritdoc />
    public KeyPressResult PressKey(IntPtr hwnd, BackgroundKey key)
    {
        uint vk = key.ToVirtualKey();
        return PressKey(hwnd, vk);
    }

    /// <inheritdoc />
    public KeyPressResult PressKey(IntPtr hwnd, uint virtualKey)
    {
        if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd))
        {
            _logger?.Warning($"Cannot press key: target HWND {HwndFormatter.Format(hwnd)} is invalid or closed");
            return KeyPressResult.InvalidTarget;
        }

        IntPtr downLParam = KeyboardMessageHelper.MakeKeyDownLParam(virtualKey);
        IntPtr upLParam = KeyboardMessageHelper.MakeKeyUpLParam(virtualKey);

        // 1. Post WM_KEYDOWN
        if (!User32.PostMessage(hwnd, NativeConstants.WM_KEYDOWN, (IntPtr)virtualKey, downLParam))
        {
            _logger?.Warning($"PostMessage WM_KEYDOWN failed for HWND {HwndFormatter.Format(hwnd)} (VK: 0x{virtualKey:X2})");
            return KeyPressResult.PostFailed;
        }

        ApplyPacing();

        // 2. Post WM_KEYUP
        if (!User32.PostMessage(hwnd, NativeConstants.WM_KEYUP, (IntPtr)virtualKey, upLParam))
        {
            _logger?.Warning($"PostMessage WM_KEYUP failed for HWND {HwndFormatter.Format(hwnd)} (VK: 0x{virtualKey:X2})");
            return KeyPressResult.PostFailed;
        }

        _logger?.Debug($"Posted WM_KEYDOWN and WM_KEYUP for VK 0x{virtualKey:X2} to HWND {HwndFormatter.Format(hwnd)}");
        return KeyPressResult.Success;
    }

    private void ApplyPacing()
    {
        if (KeyPacingMilliseconds > 0)
        {
            Thread.Sleep(KeyPacingMilliseconds);
        }
    }
}
