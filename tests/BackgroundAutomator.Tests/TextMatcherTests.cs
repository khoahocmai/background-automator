using BackgroundAutomator.Core.TextDetection;
using Xunit;

namespace BackgroundAutomator.Tests;

public class TextMatcherTests
{
    [Theory]
    [InlineData("hello world", "hello world")]
    [InlineData("  hello   world  ", "hello world")]
    [InlineData("hello\r\nworld", "hello world")]
    [InlineData("hello\t\tworld\n\nagain", "hello world again")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void NormalizeWhitespace_Normalizes_Sequences_And_Trims(string? input, string expected)
    {
        string actual = TextMatcher.NormalizeWhitespace(input);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsMatch_Contains_Matches_CaseInsensitive_And_WhitespaceNormalized()
    {
        string observed = "  Run   this \r\n command? \n > 1. Yes, run command ";
        string expected = "Run this command?";

        bool matched = TextMatcher.IsMatch(observed, expected, TextMatchMode.Contains);

        Assert.True(matched);
    }

    [Fact]
    public void IsMatch_Contains_CaseInsensitive()
    {
        string observed = "run this COMMAND?";
        string expected = "RUN THIS command?";

        bool matched = TextMatcher.IsMatch(observed, expected, TextMatchMode.Contains);

        Assert.True(matched);
    }

    [Fact]
    public void IsMatch_Exact_Succeeds_When_Identical_IgnoringCase_And_Whitespace()
    {
        string observed = "  Run\r\nthis\tcommand?  ";
        string expected = "run this command?";

        bool matched = TextMatcher.IsMatch(observed, expected, TextMatchMode.Exact);

        Assert.True(matched);
    }

    [Fact]
    public void IsMatch_Exact_Fails_When_Extra_Content_Present()
    {
        string observed = "Run this command? > 1. Yes, run command";
        string expected = "Run this command?";

        bool matched = TextMatcher.IsMatch(observed, expected, TextMatchMode.Exact);

        Assert.False(matched);
    }

    [Fact]
    public void IsMatch_NegativeCase_RunningCommand_DoesNotMatch_RunThisCommand()
    {
        // Negative test specified in Section 15:
        // Observed: "Running command..."
        // Expected: "Run this command?"
        // Result: NOT MATCHED
        string observed = "Running command...";
        string expected = "Run this command?";

        bool containsMatch = TextMatcher.IsMatch(observed, expected, TextMatchMode.Contains);
        bool exactMatch = TextMatcher.IsMatch(observed, expected, TextMatchMode.Exact);

        Assert.False(containsMatch, "Running command... must NOT match 'Run this command?' in Contains mode");
        Assert.False(exactMatch, "Running command... must NOT match 'Run this command?' in Exact mode");
    }

    [Fact]
    public void IsMatch_ReturnsFalse_When_Expected_Is_Null_Or_Whitespace()
    {
        Assert.False(TextMatcher.IsMatch("Some text", null, TextMatchMode.Contains));
        Assert.False(TextMatcher.IsMatch("Some text", "", TextMatchMode.Contains));
        Assert.False(TextMatcher.IsMatch("Some text", "   ", TextMatchMode.Contains));
    }

    [Fact]
    public void TruncatePreview_Truncates_Long_Strings()
    {
        string longText = new string('A', 200);
        string preview = TextMatcher.TruncatePreview(longText, maxLength: 50);

        Assert.Equal(53, preview.Length); // 50 chars + "..."
        Assert.EndsWith("...", preview);
    }

    [Fact]
    public void TruncatePreview_Leaves_Short_Strings_Intact()
    {
        string shortText = "Short text";
        string preview = TextMatcher.TruncatePreview(shortText, maxLength: 50);

        Assert.Equal("Short text", preview);
    }
}
