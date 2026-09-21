using BackgroundClicker.Core.Targeting;
using Xunit;

namespace BackgroundClicker.Tests;

public class HwndFormatterTests
{
    [Fact]
    public void Format_GivenZero_ReturnsFull16ZeroHex()
    {
        string formatted = HwndFormatter.Format(IntPtr.Zero);
        Assert.Equal("0x0000000000000000", formatted);
    }

    [Fact]
    public void Format_GivenKnown32BitHandle_FormatsWith16DigitsWithoutTruncation()
    {
        var handle = new IntPtr(0x001203AA);
        string formatted = HwndFormatter.Format(handle);
        Assert.Equal("0x00000000001203AA", formatted);
    }

    [Fact]
    public void Format_GivenHigh64BitPointer_FormatsCorrectly()
    {
        // Pointers above 2GB or 4GB in 64-bit address space
        long largeVal = 0x7FFF12345678ABCD;
        var handle = new IntPtr(largeVal);
        string formatted = HwndFormatter.Format(handle);
        Assert.Equal("0x7FFF12345678ABCD", formatted);
    }

    [Fact]
    public void FormatShort_FormatsWithAtLeast8Digits()
    {
        var handle = new IntPtr(0x1203AA);
        string formatted = HwndFormatter.FormatShort(handle);
        Assert.Equal("0x001203AA", formatted);
    }

    [Theory]
    [InlineData("0x00000000001203AA", 0x1203AA)]
    [InlineData("0x1203AA", 0x1203AA)]
    [InlineData("1203AA", 0x1203AA)]
    [InlineData("0x0", 0)]
    public void TryParse_ValidHexStrings_ParsesAccurately(string input, long expectedVal)
    {
        bool success = HwndFormatter.TryParse(input, out IntPtr parsed);
        Assert.True(success);
        Assert.Equal(new IntPtr(expectedVal), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotAHexValue")]
    public void TryParse_InvalidStrings_ReturnsFalse(string? input)
    {
        bool success = HwndFormatter.TryParse(input, out IntPtr parsed);
        Assert.False(success);
        Assert.Equal(IntPtr.Zero, parsed);
    }
}
