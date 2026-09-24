using System.Drawing;
using BackgroundAutomator.Win32;
using Xunit;

namespace BackgroundAutomator.Tests;

public class NativeTypesTests
{
    [Fact]
    public void Point_ImplicitConversions_WorkBidirectionally()
    {
        POINT win32Pt = new(100, 200);
        Point drawingPt = win32Pt;

        Assert.Equal(100, drawingPt.X);
        Assert.Equal(200, drawingPt.Y);

        POINT roundTrip = drawingPt;
        Assert.Equal(win32Pt, roundTrip);
    }

    [Fact]
    public void Rect_ImplicitConversions_AndCalculations_AreAccurate()
    {
        RECT win32Rect = new(50, 60, 250, 360);

        Assert.Equal(200, win32Rect.Width);
        Assert.Equal(300, win32Rect.Height);

        Rectangle drawingRect = win32Rect;
        Assert.Equal(50, drawingRect.X);
        Assert.Equal(60, drawingRect.Y);
        Assert.Equal(200, drawingRect.Width);
        Assert.Equal(300, drawingRect.Height);

        RECT roundTrip = drawingRect;
        Assert.Equal(win32Rect, roundTrip);
    }

    [Fact]
    public void Rect_Equality_WorksCorrectly()
    {
        var r1 = new RECT(10, 20, 30, 40);
        var r2 = new RECT(10, 20, 30, 40);
        var r3 = new RECT(10, 20, 31, 40);

        Assert.True(r1 == r2);
        Assert.False(r1 == r3);
    }
}
