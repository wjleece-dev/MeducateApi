using Meducate.Application.Helpers;
using Meducate.Domain.Services;

namespace Meducate.Tests;

public class ContentHasherTests
{
    [Fact]
    public void ComputeHash_ReturnsDeterministicHexString()
    {
        var hash1 = ContentHasher.ComputeHash("hello world");
        var hash2 = ContentHasher.ComputeHash("hello world");

        Assert.Equal(hash1, hash2);
        Assert.Equal(16, hash1.Length);
        Assert.True(hash1.All(c => "0123456789ABCDEF".Contains(c)));
    }

    [Fact]
    public void ComputeHash_DifferentInputsProduceDifferentHashes()
    {
        var hash1 = ContentHasher.ComputeHash("input one");
        var hash2 = ContentHasher.ComputeHash("input two");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void GetSourceHash_PrefersProviderContentHash()
    {
        var results = new[]
        {
            new RawTopicData("Topic", "text", "Source", ContentHash: "provider-hash-123")
        };

        var hash = ContentHasher.GetSourceHash(results, "merged raw source");

        Assert.Equal("provider-hash-123", hash);
    }

    [Fact]
    public void GetSourceHash_FallsBackToComputedHash_WhenNoProviderHash()
    {
        var results = new[]
        {
            new RawTopicData("Topic", "text", "Source")
        };

        var hash = ContentHasher.GetSourceHash(results, "merged raw source");

        Assert.Equal(ContentHasher.ComputeHash("merged raw source"), hash);
    }
}
