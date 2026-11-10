using Meducate.Domain.Entities;

namespace Meducate.Tests;

public class UserTests
{
    [Fact]
    public void RotateVerificationToken_SetsNewToken()
    {
        var user = new User { Email = "test@example.com" };
        var oldToken = user.VerificationToken;

        user.RotateVerificationToken();

        Assert.NotNull(user.VerificationToken);
        Assert.NotEqual(oldToken, user.VerificationToken);
    }

    [Fact]
    public void RotateVerificationToken_SetsExpiryInFuture()
    {
        var user = new User { Email = "test@example.com" };

        user.RotateVerificationToken();

        Assert.NotNull(user.VerificationTokenExpiresAt);
        Assert.True(user.VerificationTokenExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public void RotateVerificationToken_ProducesUniqueTokens()
    {
        var user = new User { Email = "test@example.com" };

        user.RotateVerificationToken();
        var token1 = user.VerificationToken;

        user.RotateVerificationToken();
        var token2 = user.VerificationToken;

        Assert.NotEqual(token1, token2);
    }

    [Fact]
    public void RotateVerificationToken_Produces64CharHexToken()
    {
        var user = new User { Email = "test@example.com" };

        user.RotateVerificationToken();

        Assert.NotNull(user.VerificationToken);
        Assert.Equal(64, user.VerificationToken.Length);
        Assert.True(user.VerificationToken.All(c => "0123456789abcdef".Contains(c)));
    }

    [Fact]
    public void SecurityStamp_Is64CharHexByDefault()
    {
        var user = new User { Email = "test@example.com" };

        Assert.Equal(64, user.SecurityStamp.Length);
        Assert.True(user.SecurityStamp.All(c => "0123456789abcdef".Contains(c)));
    }
}
