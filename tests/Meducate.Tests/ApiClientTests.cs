using Meducate.Domain.Entities;

namespace Meducate.Tests;

public class ApiClientTests
{
    [Fact]
    public void IsExpired_ReturnsFalseWhenNoExpiry()
    {
        var client = new ApiClient { ExpiresAt = null };

        Assert.False(client.IsExpired);
    }

    [Fact]
    public void IsExpired_ReturnsFalseWhenExpiryInFuture()
    {
        var client = new ApiClient { ExpiresAt = DateTime.UtcNow.AddDays(30) };

        Assert.False(client.IsExpired);
    }

    [Fact]
    public void IsExpired_ReturnsTrueWhenExpiryInPast()
    {
        var client = new ApiClient { ExpiresAt = DateTime.UtcNow.AddMinutes(-1) };

        Assert.True(client.IsExpired);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var client = new ApiClient();

        Assert.NotEqual(Guid.Empty, client.Id);
        Assert.Null(client.LastUsedAt);
        Assert.Null(client.ExpiresAt);
        Assert.False(client.IsExpired);
    }
}
