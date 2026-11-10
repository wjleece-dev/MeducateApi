using Meducate.Infrastructure.ApiKeys;

namespace Meducate.Tests;

public class ApiKeyHasherTests
{
    [Fact]
    public void HashSecret_ProducesUniqueHashAndSalt()
    {
        var (hash1, salt1) = ApiKeyHasher.HashSecret("test-secret");
        var (hash2, salt2) = ApiKeyHasher.HashSecret("test-secret");

        // Same input should produce different salts (and thus different hashes)
        Assert.NotEqual(salt1, salt2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Verify_ReturnsTrueForCorrectSecret()
    {
        var secret = "my-secret-key";
        var (hash, salt) = ApiKeyHasher.HashSecret(secret);

        Assert.True(ApiKeyHasher.Verify(secret, hash, salt));
    }

    [Fact]
    public void Verify_ReturnsFalseForWrongSecret()
    {
        var (hash, salt) = ApiKeyHasher.HashSecret("correct-secret");

        Assert.False(ApiKeyHasher.Verify("wrong-secret", hash, salt));
    }

    [Fact]
    public void GenerateSecret_ProducesBase64String()
    {
        var secret = ApiKeyHasher.GenerateSecret();

        // Should be valid base64
        var bytes = Convert.FromBase64String(secret);
        Assert.Equal(32, bytes.Length);
    }

    [Fact]
    public void GenerateSecret_ProducesUniqueValues()
    {
        var secret1 = ApiKeyHasher.GenerateSecret();
        var secret2 = ApiKeyHasher.GenerateSecret();

        Assert.NotEqual(secret1, secret2);
    }

    [Fact]
    public void GenerateKeyId_ProducesValidGuidFormat()
    {
        var keyId = ApiKeyHasher.GenerateKeyId();

        // Should be a 32-char hex string (Guid without dashes)
        Assert.Equal(32, keyId.Length);
        Assert.True(Guid.TryParse(keyId, out _));
    }
}
