using BackgroundAutomator.Win32;
using Xunit;

namespace BackgroundAutomator.Tests;

public class MouseMessageHelperTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 200)]
    [InlineData(-1, -1)]
    [InlineData(-50, 100)]
    [InlineData(100, -50)]
    [InlineData(-1234, -5678)]
    [InlineData(short.MaxValue, short.MinValue)]
    [InlineData(short.MinValue, short.MaxValue)]
    [InlineData(32767, -32768)]
    public void MakeMouseLParam_And_GetMouseX_GetMouseY_RoundTripSignedSemantics(int x, int y)
    {
        IntPtr lParam = MouseMessageHelper.MakeMouseLParam(x, y);

        int decodedX = MouseMessageHelper.GetMouseX(lParam);
        int decodedY = MouseMessageHelper.GetMouseY(lParam);

        Assert.Equal(x, decodedX);
        Assert.Equal(y, decodedY);
    }

    [Fact]
    public void MakeMouseLParam_WithZero_ReturnsIntPtrZero()
    {
        IntPtr lParam = MouseMessageHelper.MakeMouseLParam(0, 0);
        Assert.Equal(IntPtr.Zero, lParam);
    }

    [Fact]
    public void MakeMouseLParam_MinusOneMinusOne_HasExpectedAllBitsSetInLower32Bits()
    {
        IntPtr lParam = MouseMessageHelper.MakeMouseLParam(-1, -1);
        Assert.Equal(-1, MouseMessageHelper.GetMouseX(lParam));
        Assert.Equal(-1, MouseMessageHelper.GetMouseY(lParam));
        Assert.Equal(0xFFFFFFFF, unchecked((uint)lParam.ToInt64()));
    }
}
