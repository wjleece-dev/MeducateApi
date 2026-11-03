using Meducate.Application.Helpers;

namespace Meducate.Tests;

public class WaitlistHelpersTests
{
    [Theory]
    [InlineData("a@b.co", "a@b.co")]
    [InlineData("  Jane.Doe@Example.COM  ", "jane.doe@example.com")]
    [InlineData("first+tag@sub.example.co.uk", "first+tag@sub.example.co.uk")]
    public void TryNormalizeEmail_AcceptsPlainAddresses_TrimmedAndLowercased(string raw, string expected)
    {
        Assert.True(WaitlistHelpers.TryNormalizeEmail(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("a@b")]                    // domain without a dot
    [InlineData("a@.com")]
    [InlineData("a@b.")]
    [InlineData("two words@example.com")]  // whitespace inside
    [InlineData("Jane <jane@example.com>")] // display name form
    [InlineData("a@b.com\r\nBcc: x@y.com")] // header injection attempt
    public void TryNormalizeEmail_RejectsAnythingElse(string? raw)
    {
        Assert.False(WaitlistHelpers.TryNormalizeEmail(raw, out var normalized));
        Assert.Equal("", normalized);
    }

    [Fact]
    public void TryNormalizeEmail_RejectsOverlongAddresses()
    {
        var email = new string('a', 250) + "@b.co";

        Assert.False(WaitlistHelpers.TryNormalizeEmail(email, out _));
    }

    private static string RenderedAgo(TimeSpan age, DateTimeOffset now) =>
        now.Add(-age).ToUnixTimeMilliseconds().ToString();

    [Fact]
    public void CheckTiming_AcceptsAFormRenderedSecondsAgo()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(SubmissionTiming.Ok, WaitlistHelpers.CheckTiming(RenderedAgo(TimeSpan.FromSeconds(10), now), now));
    }

    [Fact]
    public void CheckTiming_FlagsSubmissionsUnderTwoSecondsAndFromTheFuture_AsTooFast()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(SubmissionTiming.TooFast, WaitlistHelpers.CheckTiming(RenderedAgo(TimeSpan.FromMilliseconds(500), now), now));
        Assert.Equal(SubmissionTiming.TooFast, WaitlistHelpers.CheckTiming(RenderedAgo(TimeSpan.FromSeconds(-30), now), now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-number")]
    public void CheckTiming_FlagsMissingOrUnparseableTimestamps_AsInvalid(string? timestamp)
    {
        Assert.Equal(SubmissionTiming.Invalid, WaitlistHelpers.CheckTiming(timestamp, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void CheckTiming_FlagsFormsOlderThanSevenDays_AsInvalid()
    {
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(SubmissionTiming.Invalid, WaitlistHelpers.CheckTiming(RenderedAgo(TimeSpan.FromDays(8), now), now));
        Assert.Equal(SubmissionTiming.Ok, WaitlistHelpers.CheckTiming(RenderedAgo(TimeSpan.FromDays(6), now), now));
    }
}
