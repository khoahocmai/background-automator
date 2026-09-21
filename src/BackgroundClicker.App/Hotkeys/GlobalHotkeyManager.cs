using System.Runtime.InteropServices;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Win32;

namespace BackgroundClicker.App.Hotkeys;

/// <summary>
/// Manages system-wide global hotkeys (F6 for Start/Stop, F7 for Emergency Stop)
/// via Win32 RegisterHotKey and UnregisterHotKey.
/// </summary>
public sealed class GlobalHotkeyManager : IDisposable
{
    public const int HotkeyIdStartStop = 1001;
    public const int HotkeyIdEmergencyStop = 1002;

    private readonly IntPtr _hWnd;
    private readonly Action _onStartStop;
    private readonly Action _onEmergencyStop;
    private readonly IAppLogger? _logger;

    private bool _f6Registered;
    private bool _f7Registered;
    private bool _disposed;

    public GlobalHotkeyManager(
        IntPtr hWnd,
        Action onStartStop,
        Action onEmergencyStop,
        IAppLogger? logger = null)
    {
        _hWnd = hWnd;
        _onStartStop = onStartStop ?? throw new ArgumentNullException(nameof(onStartStop));
        _onEmergencyStop = onEmergencyStop ?? throw new ArgumentNullException(nameof(onEmergencyStop));
        _logger = logger;
    }

    /// <summary>
    /// Registers F6 (Start/Stop) and F7 (Emergency Stop) system-wide.
    /// Fails gracefully if another application owns either hotkey.
    /// </summary>
    public void RegisterHotkeys()
    {
        if (_disposed || _hWnd == IntPtr.Zero)
            return;

        // Register F6 (VK_F6 = 0x75)
        _f6Registered = User32.RegisterHotKey(_hWnd, HotkeyIdStartStop, NativeConstants.MOD_NOREPEAT, NativeConstants.VK_F6);
        if (_f6Registered)
        {
            _logger?.Info("Global hotkey registered: F6 (Start / Stop)");
        }
        else
        {
            int err = Marshal.GetLastWin32Error();
            _logger?.Warning($"Failed to register global hotkey F6 (Win32 Error: {err}). Key may be claimed by another application.");
        }

        // Register F7 (VK_F7 = 0x76)
        _f7Registered = User32.RegisterHotKey(_hWnd, HotkeyIdEmergencyStop, NativeConstants.MOD_NOREPEAT, NativeConstants.VK_F7);
        if (_f7Registered)
        {
            _logger?.Info("Global hotkey registered: F7 (Emergency Stop)");
        }
        else
        {
            int err = Marshal.GetLastWin32Error();
            _logger?.Warning($"Failed to register global hotkey F7 (Win32 Error: {err}). Key may be claimed by another application.");
        }
    }

    /// <summary>
    /// Processes a WM_HOTKEY message.
    /// </summary>
    /// <param name="hotkeyId">The hotkey identifier from wParam.</param>
    /// <returns>True if the hotkey was handled, false otherwise.</returns>
    public bool ProcessHotkey(int hotkeyId)
    {
        if (hotkeyId == HotkeyIdStartStop)
        {
            _logger?.Info("Global hotkey triggered: F6 (Start / Stop)");
            _onStartStop();
            return true;
        }

        if (hotkeyId == HotkeyIdEmergencyStop)
        {
            _logger?.Info("Global hotkey triggered: F7 (Emergency Stop)");
            _onEmergencyStop();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Unregisters all registered global hotkeys.
    /// </summary>
    public void UnregisterHotkeys()
    {
        if (_hWnd == IntPtr.Zero)
            return;

        if (_f6Registered)
        {
            User32.UnregisterHotKey(_hWnd, HotkeyIdStartStop);
            _f6Registered = false;
        }

        if (_f7Registered)
        {
            User32.UnregisterHotKey(_hWnd, HotkeyIdEmergencyStop);
            _f7Registered = false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            UnregisterHotkeys();
            _disposed = true;
        }
    }
}
