using System.Drawing;
using BackgroundClicker.Core.Logging;
using BackgroundClicker.Core.Targeting;
using BackgroundClicker.Win32;

namespace BackgroundClicker.Core.Capture;

/// <summary>
/// Implements window client-area capture using Win32 GDI and PrintWindow with PW_CLIENTONLY.
/// Strictly enforces the GDI lifecycle to ensure zero unmanaged resource leaks:
/// GetDC -> CreateCompatibleDC -> CreateCompatibleBitmap -> SelectObject -> PrintWindow
/// -> Restore SelectObject -> DeleteObject -> DeleteDC -> ReleaseDC.
/// </summary>
public sealed class GdiWindowCaptureService : IWindowCaptureService
{
    private readonly IAppLogger? _logger;

    public GdiWindowCaptureService(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public WindowCapture? CaptureClientArea(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !User32.IsWindow(hWnd))
        {
            _logger?.Warning($"Capture failed: Invalid or destroyed HWND {HwndFormatter.Format(hWnd)}");
            return null;
        }

        if (!User32.GetClientRect(hWnd, out RECT clientRect))
        {
            _logger?.Warning($"Capture failed: GetClientRect returned false for HWND {HwndFormatter.Format(hWnd)}");
            return null;
        }

        int width = clientRect.Right - clientRect.Left;
        int height = clientRect.Bottom - clientRect.Top;

        if (width <= 0 || height <= 0)
        {
            _logger?.Warning($"Capture failed: Window has non-positive client area ({width}x{height}) for HWND {HwndFormatter.Format(hWnd)}");
            return null;
        }

        IntPtr hdcWindow = User32.GetDC(hWnd);
        if (hdcWindow == IntPtr.Zero)
        {
            _logger?.Warning($"Capture failed: GetDC returned IntPtr.Zero for HWND {HwndFormatter.Format(hWnd)}");
            return null;
        }

        try
        {
            IntPtr hdcMem = Gdi32.CreateCompatibleDC(hdcWindow);
            if (hdcMem == IntPtr.Zero)
            {
                _logger?.Warning($"Capture failed: CreateCompatibleDC returned IntPtr.Zero for HWND {HwndFormatter.Format(hWnd)}");
                return null;
            }

            try
            {
                IntPtr hBitmap = Gdi32.CreateCompatibleBitmap(hdcWindow, width, height);
                if (hBitmap == IntPtr.Zero)
                {
                    _logger?.Warning($"Capture failed: CreateCompatibleBitmap returned IntPtr.Zero for HWND {HwndFormatter.Format(hWnd)}");
                    return null;
                }

                try
                {
                    IntPtr hOldBitmap = Gdi32.SelectObject(hdcMem, hBitmap);
                    try
                    {
                        bool printed = User32.PrintWindow(hWnd, hdcMem, NativeConstants.PW_CLIENTONLY);
                        if (!printed)
                        {
                            // Fallback retry with default flags if target doesn't support PW_CLIENTONLY
                            printed = User32.PrintWindow(hWnd, hdcMem, 0);
                        }

                        if (!printed)
                        {
                            _logger?.Warning($"Capture failed: PrintWindow returned false for HWND {HwndFormatter.Format(hWnd)}");
                            return null;
                        }

                        // Convert unmanaged GDI bitmap to managed GDI+ Bitmap
                        using var tempBmp = Image.FromHbitmap(hBitmap);
                        var managedBitmap = new Bitmap(tempBmp);

                        return new WindowCapture(hWnd, managedBitmap);
                    }
                    finally
                    {
                        // Invariant: Always restore original selected object before deleting bitmap
                        Gdi32.SelectObject(hdcMem, hOldBitmap);
                    }
                }
                finally
                {
                    Gdi32.DeleteObject(hBitmap);
                }
            }
            finally
            {
                Gdi32.DeleteDC(hdcMem);
            }
        }
        finally
        {
            User32.ReleaseDC(hWnd, hdcWindow);
        }
    }
}
