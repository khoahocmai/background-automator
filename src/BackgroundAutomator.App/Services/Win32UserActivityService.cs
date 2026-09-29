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
    private readonly IAppLogger? _logger;
    private bool _hasLoggedFailure;

    private uint? _lastInjectedTick;
    private TimeSpan _baseIdleDurationBeforeInjection = TimeSpan.Zero;
    private readonly Stopwatch _injectionStopwatch = new();

    public Win32UserActivityService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    public bool TryGetIdleDuration(out TimeSpan idleDuration)
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!User32.GetLastInputInfo(ref lii))
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

        // Check if the most recent system input event was our own SendInput injection
        if (_lastInjectedTick.HasValue)
        {
            uint diffFromInjected = unchecked(lii.dwTime - _lastInjectedTick.Value);
            // If the tick corresponds to our injected input (or within a 100ms tolerance),
            // no new physical user input has arrived since injection.
            if (diffFromInjected <= 100)
            {
                idleDuration = _baseIdleDurationBeforeInjection + _injectionStopwatch.Elapsed;
                return true;
            }

            // User has generated physical input after our injection
            _lastInjectedTick = null;
            _injectionStopwatch.Reset();
        }

        uint currentTick = unchecked((uint)Environment.TickCount);
        uint elapsedMs = unchecked(currentTick - lii.dwTime);
        idleDuration = TimeSpan.FromMilliseconds(elapsedMs);
        return true;
    }

    public void NotifyInputInjected()
    {
        // Capture user idle duration before injection as baseline
        if (TryGetIdleDuration(out var currentIdle))
        {
            _baseIdleDurationBeforeInjection = currentIdle;
        }
        else
        {
            _baseIdleDurationBeforeInjection = TimeSpan.FromSeconds(2);
        }

        _injectionStopwatch.Restart();

        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (User32.GetLastInputInfo(ref lii))
        {
            _lastInjectedTick = lii.dwTime;
        }
        else
        {
            _lastInjectedTick = unchecked((uint)Environment.TickCount);
        }
    }
}
