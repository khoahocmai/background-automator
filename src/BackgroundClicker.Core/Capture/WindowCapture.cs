using System.Drawing;

namespace BackgroundClicker.Core.Capture;

/// <summary>
/// Encapsulates a captured snapshot of a target window's client area.
/// Coordinates are strictly aligned with the target window's client coordinate space,
/// where (0, 0) represents the top-left corner of the client area.
/// </summary>
public sealed class WindowCapture : IDisposable
{
    private bool _disposed;

    /// <summary>
    /// HWND of the captured window.
    /// </summary>
    public IntPtr Hwnd { get; }

    /// <summary>
    /// Width of the client area in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Height of the client area in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Origin of client coordinates, always (0, 0).
    /// </summary>
    public Point ClientOrigin => new(0, 0);

    /// <summary>
    /// The underlying managed bitmap representing the client area capture.
    /// </summary>
    public Bitmap Bitmap { get; }

    public WindowCapture(IntPtr hwnd, Bitmap bitmap)
    {
        Hwnd = hwnd;
        Bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        Width = bitmap.Width;
        Height = bitmap.Height;
    }

    /// <summary>
    /// Gets the color of a pixel at the specified client coordinates.
    /// </summary>
    /// <param name="clientX">Zero-based X coordinate in client space.</param>
    /// <param name="clientY">Zero-based Y coordinate in client space.</param>
    /// <returns>The color at (clientX, clientY).</returns>
    /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if coordinates fall outside the client area bounds.</exception>
    public Color GetPixel(int clientX, int clientY)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (clientX < 0 || clientX >= Width)
        {
            throw new ArgumentOutOfRangeException(nameof(clientX), clientX, $"Client X must be between 0 and {Width - 1}.");
        }

        if (clientY < 0 || clientY >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(clientY), clientY, $"Client Y must be between 0 and {Height - 1}.");
        }

        return Bitmap.GetPixel(clientX, clientY);
    }

    /// <summary>
    /// Evaluates whether the pixel at (clientX, clientY) matches the expected target color within tolerance.
    /// Tolerance is applied independently to R, G, and B color channels: |actual - target| &lt;= tolerance.
    /// </summary>
    public bool MatchesColor(int clientX, int clientY, Color targetColor, int tolerance = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (clientX < 0 || clientX >= Width || clientY < 0 || clientY >= Height)
        {
            return false;
        }

        Color actual = Bitmap.GetPixel(clientX, clientY);
        return Math.Abs(actual.R - targetColor.R) <= tolerance
            && Math.Abs(actual.G - targetColor.G) <= tolerance
            && Math.Abs(actual.B - targetColor.B) <= tolerance;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Bitmap.Dispose();
            _disposed = true;
        }
    }
}
