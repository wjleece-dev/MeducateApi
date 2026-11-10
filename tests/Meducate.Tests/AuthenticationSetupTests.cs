using Meducate.API.Infrastructure;

namespace Meducate.Tests;

public class AuthenticationSetupTests
{
    private const long Now = 1_700_000_000L;

    [Fact]
    public void IsAuthTimeValid_WithinMaxAge_ReturnsTrue()
    {
        Assert.True(AuthenticationSetup.IsAuthTimeValid((Now - 1000).ToString(), Now));
    }

    [Fact]
    public void IsAuthTimeValid_ExceedsMaxAge_ReturnsFalse()
    {
        var eightDaysAgo = Now - (8 * 24 * 60 * 60);
        Assert.False(AuthenticationSetup.IsAuthTimeValid(eightDaysAgo.ToString(), Now));
    }

    [Fact]
    public void IsAuthTimeValid_MissingClaim_ReturnsFalse()
    {
        Assert.False(AuthenticationSetup.IsAuthTimeValid(null, Now));
    }

    [Fact]
    public void IsAuthTimeValid_MalformedClaim_ReturnsFalse()
    {
        Assert.False(AuthenticationSetup.IsAuthTimeValid("not-a-number", Now));
    }

    [Fact]
    public void IsStampRecheckDue_NoPriorCheck_ReturnsTrue()
    {
        Assert.True(AuthenticationSetup.IsStampRecheckDue(null, Now));
    }

    [Fact]
    public void IsStampRecheckDue_WithinThrottleWindow_ReturnsFalse()
    {
        var thirtySecondsAgo = Now - 30;
        Assert.False(AuthenticationSetup.IsStampRecheckDue(thirtySecondsAgo.ToString(), Now));
    }

    [Fact]
    public void IsStampRecheckDue_PastThrottleWindow_ReturnsTrue()
    {
        var tenMinutesAgo = Now - 600;
        Assert.True(AuthenticationSetup.IsStampRecheckDue(tenMinutesAgo.ToString(), Now));
    }

    [Fact]
    public void StampsMatch_SameStamp_ReturnsTrue()
    {
        Assert.True(AuthenticationSetup.StampsMatch("abc123", "abc123"));
    }

    [Fact]
    public void StampsMatch_DifferentStamp_ReturnsFalse()
    {
        Assert.False(AuthenticationSetup.StampsMatch("abc123", "xyz789"));
    }
}
