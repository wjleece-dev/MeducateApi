using System.Security.Cryptography;

namespace Meducate.Infrastructure.ApiKeys;

internal static class ApiKeyHasher
{
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static (string hashed, string salt) HashSecret(string secret)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(secret, saltBytes, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(saltBytes));
    }

    public static bool Verify(string secret, string storedHashBase64, string saltBase64)
    {
        var salt = Convert.FromBase64String(saltBase64);
        var computed = Rfc2898DeriveBytes.Pbkdf2(secret, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        var stored = Convert.FromBase64String(storedHashBase64);
        return CryptographicOperations.FixedTimeEquals(computed, stored);
    }

    public static string GenerateSecret(int bytes = 32) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
    public static string GenerateKeyId() => Guid.NewGuid().ToString("N");
}
