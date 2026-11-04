using Meducate.API.Infrastructure;

namespace Meducate.Tests;

public class HangfireDashboardAuthFilterTests
{
    [Fact]
    public void Matches_AcceptsOnlyTheExactPassword()
    {
        Assert.True(HangfireDashboardAuthFilter.Matches("correct horse", "correct horse"));
        Assert.False(HangfireDashboardAuthFilter.Matches("correct horse ", "correct horse"));
        Assert.False(HangfireDashboardAuthFilter.Matches(null, "correct horse"));
        Assert.False(HangfireDashboardAuthFilter.Matches("", "correct horse"));
    }

    [Fact]
    public void SessionToken_IsStableButNeverTheRawPassword()
    {
        var token = HangfireDashboardAuthFilter.SessionToken("correct horse");

        Assert.Equal(token, HangfireDashboardAuthFilter.SessionToken("correct horse"));
        Assert.NotEqual(token, HangfireDashboardAuthFilter.SessionToken("correct horsf"));
        Assert.DoesNotContain("correct horse", token);
    }
}
