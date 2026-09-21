using BackgroundClicker.Core.Targeting;
using Xunit;

namespace BackgroundClicker.Tests;

public class TargetPointTests
{
    [Fact]
    public void TargetPoint_WithSameHwndAndCoords_AreEqual()
    {
        var p1 = new TargetPoint(new IntPtr(0x100), 25, 50);
        var p2 = new TargetPoint(new IntPtr(0x100), 25, 50);

        Assert.Equal(p1, p2);
        Assert.True(p1 == p2);
    }

    [Fact]
    public void TargetPoint_WithDifferentHwndSameCoords_AreNotEqual()
    {
        // Enforces core invariant: A client coordinate must always be bound to its specific HWND.
        var p1 = new TargetPoint(new IntPtr(0x100), 25, 50);
        var p2 = new TargetPoint(new IntPtr(0x200), 25, 50);

        Assert.NotEqual(p1, p2);
        Assert.True(p1 != p2);
    }

    [Fact]
    public void TargetPoint_Empty_HasZeroHwndAndZeroCoords()
    {
        var empty = TargetPoint.Empty;
        Assert.True(empty.IsEmpty);
        Assert.Equal(IntPtr.Zero, empty.Hwnd);
        Assert.Equal(0, empty.ClientX);
        Assert.Equal(0, empty.ClientY);
    }

    [Fact]
    public void TargetPoint_ToString_FormatsCorrectlyWithHwnd()
    {
        var pt = new TargetPoint(new IntPtr(0x1203AA), 17, 14);
        string str = pt.ToString();

        Assert.Contains("0x00000000001203AA", str);
        Assert.Contains("17", str);
        Assert.Contains("14", str);
    }
}
