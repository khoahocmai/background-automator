using System.Drawing;
using BackgroundClicker.Core.Capture;
using Xunit;

namespace BackgroundClicker.Tests;

public class WindowCaptureTests
{
    [Fact]
    public void WindowCapture_PropertiesAndOrigin_AreCorrect()
    {
        using var bmp = new Bitmap(120, 80);
        using var capture = new WindowCapture((IntPtr)0x1234, bmp);

        Assert.Equal((IntPtr)0x1234, capture.Hwnd);
        Assert.Equal(120, capture.Width);
        Assert.Equal(80, capture.Height);
        Assert.Equal(new Point(0, 0), capture.ClientOrigin);
        Assert.Same(bmp, capture.Bitmap);
    }

    [Fact]
    public void WindowCapture_GetPixel_ReturnsCorrectColorWithinBounds()
    {
        using var bmp = new Bitmap(50, 50);
        bmp.SetPixel(15, 25, Color.FromArgb(255, 10, 20, 30));

        using var capture = new WindowCapture((IntPtr)0x5555, bmp);
        Color pixel = capture.GetPixel(15, 25);

        Assert.Equal(255, pixel.A);
        Assert.Equal(10, pixel.R);
        Assert.Equal(20, pixel.G);
        Assert.Equal(30, pixel.B);
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(50, 10)]
    [InlineData(10, -1)]
    [InlineData(10, 50)]
    public void WindowCapture_GetPixel_OutOfBounds_ThrowsArgumentOutOfRangeException(int x, int y)
    {
        using var bmp = new Bitmap(50, 50);
        using var capture = new WindowCapture((IntPtr)0x5555, bmp);

        Assert.Throws<ArgumentOutOfRangeException>(() => capture.GetPixel(x, y));
    }

    [Fact]
    public void WindowCapture_MatchesColor_ExactAndWithTolerance()
    {
        using var bmp = new Bitmap(30, 30);
        bmp.SetPixel(10, 10, Color.FromArgb(100, 150, 200));

        using var capture = new WindowCapture((IntPtr)0x1000, bmp);

        // Exact match
        Assert.True(capture.MatchesColor(10, 10, Color.FromArgb(100, 150, 200), tolerance: 0));

        // Within tolerance (5)
        Assert.True(capture.MatchesColor(10, 10, Color.FromArgb(104, 146, 203), tolerance: 5));

        // Outside tolerance (5)
        Assert.False(capture.MatchesColor(10, 10, Color.FromArgb(106, 150, 200), tolerance: 5));

        // Out of bounds returns false
        Assert.False(capture.MatchesColor(-1, 10, Color.FromArgb(100, 150, 200), tolerance: 100));
        Assert.False(capture.MatchesColor(30, 10, Color.FromArgb(100, 150, 200), tolerance: 100));
    }

    [Fact]
    public void WindowCapture_Dispose_DisposesBitmapAndThrowsOnSubsequentCalls()
    {
        var bmp = new Bitmap(20, 20);
        var capture = new WindowCapture((IntPtr)0x2000, bmp);

        capture.Dispose();

        Assert.Throws<ObjectDisposedException>(() => capture.GetPixel(5, 5));
        Assert.Throws<ObjectDisposedException>(() => capture.MatchesColor(5, 5, Color.Red));
    }
}
