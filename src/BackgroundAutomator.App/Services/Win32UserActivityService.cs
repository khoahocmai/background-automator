using System.Diagnostics;
using System.Runtime.InteropServices;
using BackgroundAutomator.Core.Logging;
using BackgroundAutomator.Core.UserActivity;
using BackgroundAutomator.Win32;

namespace BackgroundAutomator.App.Services;

/// <summary>
/// Production Win32 implementation of <see cref="IUserActivityService"/> using <see cref="User32.GetLastInputInfo"/>.
/// Tracks and adjusts for BackgroundAutomator's own injected input to prevent false user-activity pauses.
/// </summary>
public sealed class Win32UserActivityService : IUserActivityService
{
    public delegate bool GetLastInputInfoDelegate(ref LASTINPUTINFO plii);
    public delegate uint GetTickCountDelegate();

    private readonly IAppLogger? _logger;
    private readonly GetLastInputInfoDelegate _getLastInputInfo;
    private readonly GetTickCountDelegate _getTickCount;
    private bool _hasLoggedFailure;

    private uint? _lastInjectedTick;
    private TimeSpan _baseIdleDurationBeforeInjection = TimeSpan.Zero;
    private readonly Stopwatch _injectionStopwatch = new();

    public Win32UserActivityService(IAppLogger? logger = null)
        : this(User32.GetLastInputInfo, () => unchecked((uint)Environment.TickCount), logger)
    {
    }

    public Win32UserActivityService(
        GetLastInputInfoDelegate getLastInputInfo,
        GetTickCountDelegate getTickCount,
        IAppLogger? logger = null)
    {
        _getLastInputInfo = getLastInputInfo;
        _getTickCount = getTickCount;
        _logger = logger;
    }

    public bool TryGetIdleDuration(out TimeSpan idleDuration)
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!_getLastInputInfo(ref lii))
        {
            if (!_hasLoggedFailure)
            {
                _hasLoggedFailure = true;
                int err = Marshal.GetLastWin32Error();
                _logger?.Warning($"[UserActivity] GetLastInputInfo failed with Win32 error {err}. Falling back to FastPulse.");
            }
            idleDuration = TimeSpan.Zero;
            return false;
        }

        _hasLoggedFailure = false;

        // Check if the most recent system input event matches our recorded SendInput injection tick exactly.
        // NOTE ON WIN32 LIMITATION: If a physical user input occurs at the exact same millisecond tick value
        // as the injected SendInput, Windows GetLastInputInfo returns that tick value and cannot distinguish
        // between the two. In that rare same-tick event, the synthetic correction applies until the next input.
        if (_lastInjectedTick.HasValue)
        {
            if (lii.dwTime == _lastInjectedTick.Value)
            {
                // The most recent input in the system is still our own injected input.
                // Derive idle duration from the pre-injection idle duration + time elapsed since injection.
                idleDuration = _baseIdleDurationBeforeInjection + _injectionStopwatch.Elapsed;
                return true;
            }

            // A newer input event has occurred (different tick). Clear synthetic state immediately.
            _lastInjectedTick = null;
            _injectionStopwatch.Reset();
        }

        uint currentTick = _getTickCount();
        uint elapsedMs = unchecked(currentTick - lii.dwTime);
        idleDuration = TimeSpan.FromMilliseconds(elapsedMs);
        return true;
    }

    public void NotifyInputInjected(TimeSpan idleDurationBeforeInjection)
    {
        _baseIdleDurationBeforeInjection = idleDurationBeforeInjection;
        _injectionStopwatch.Restart();

        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (_getLastInputInfo(ref lii))
        {
            _lastInjectedTick = lii.dwTime;
        }
        else
        {
            _lastInjectedTick = _getTickCount();
        }
    }

    public void NotifyInputInjected()
    {
        NotifyInputInjected(TimeSpan.Zero);
    }
}
