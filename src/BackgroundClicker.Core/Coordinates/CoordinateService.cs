using System.Drawing;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;

namespace BackgroundClicker.Core.Coordinates;

/// <summary>
/// Service providing Win32 coordinate conversions and round-trip verification.
/// </summary>
public class CoordinateService
{
    private readonly IAppLogger? _logger;

    public CoordinateService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Converts a screen coordinate into a client coordinate relative to the specified HWND.
    /// </summary>
    public TargetPoint ScreenToClient(IntPtr hwnd, Point screenPoint)
    {
        if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd))
        {
            _logger?.Warning($"ScreenToClient failed: Invalid HWND {HwndFormatter.Format(hwnd)}");
            return new TargetPoint(hwnd, 0, 0);
        }

        POINT pt = new(screenPoint.X, screenPoint.Y);
        if (!User32.ScreenToClient(hwnd, ref pt))
        {
            _logger?.Warning($"ScreenToClient Win32 call failed for HWND {HwndFormatter.Format(hwnd)} at ({screenPoint.X}, {screenPoint.Y})");
            return new TargetPoint(hwnd, 0, 0);
        }

        return new TargetPoint(hwnd, pt.X, pt.Y);
    }

    /// <summary>
    /// Converts an HWND-bound client coordinate to global screen coordinates.
    /// </summary>
    public Point ClientToScreen(TargetPoint clientPoint)
    {
        return ClientToScreen(clientPoint.Hwnd, clientPoint.ClientX, clientPoint.ClientY);
    }

    /// <summary>
    /// Converts a client coordinate of a specific HWND to global screen coordinates.
    /// </summary>
    public Point ClientToScreen(IntPtr hwnd, int clientX, int clientY)
    {
        if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd))
        {
            _logger?.Warning($"ClientToScreen failed: Invalid HWND {HwndFormatter.Format(hwnd)}");
            return Point.Empty;
        }

        POINT pt = new(clientX, clientY);
        if (!User32.ClientToScreen(hwnd, ref pt))
        {
            _logger?.Warning($"ClientToScreen Win32 call failed for HWND {HwndFormatter.Format(hwnd)} at ({clientX}, {clientY})");
            return Point.Empty;
        }

        return new Point(pt.X, pt.Y);
    }

    /// <summary>
    /// Verifies round-trip conversion: screen -> client -> screen.
    /// Returns true if round-trip screen point exactly matches the input screen point.
    /// </summary>
    public bool VerifyRoundTrip(IntPtr hwnd, Point screenPoint, out TargetPoint clientPoint, out Point roundTripScreenPoint)
    {
        clientPoint = ScreenToClient(hwnd, screenPoint);
        roundTripScreenPoint = ClientToScreen(clientPoint);

        bool matches = roundTripScreenPoint == screenPoint;
        if (!matches)
        {
            _logger?.Warning($"Coordinate round-trip mismatch for HWND {HwndFormatter.Format(hwnd)}: screen {screenPoint} -> client ({clientPoint.ClientX}, {clientPoint.ClientY}) -> screen {roundTripScreenPoint}");
        }

        return matches;
    }

    /// <summary>
    /// Verifies round-trip conversion: client -> screen -> client.
    /// Returns true if round-trip client point exactly matches the input client point.
    /// </summary>
    public bool VerifyRoundTrip(TargetPoint clientPoint, out Point screenPoint, out TargetPoint roundTripClientPoint)
    {
        screenPoint = ClientToScreen(clientPoint);
        roundTripClientPoint = ScreenToClient(clientPoint.Hwnd, screenPoint);

        bool matches = roundTripClientPoint.ClientX == clientPoint.ClientX &&
                       roundTripClientPoint.ClientY == clientPoint.ClientY;
        if (!matches)
        {
            _logger?.Warning($"Coordinate round-trip mismatch for HWND {HwndFormatter.Format(clientPoint.Hwnd)}: client ({clientPoint.ClientX}, {clientPoint.ClientY}) -> screen {screenPoint} -> client ({roundTripClientPoint.ClientX}, {roundTripClientPoint.ClientY})");
        }

        return matches;
    }
}
