using System.Security.Cryptography;

namespace Meducate.Domain.Entities;

internal sealed class User
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(24);

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Email { get; set; } = null!;
    public bool IsEmailVerified { get; set; }

    public string? VerificationToken { get; set; }
    public DateTime? VerificationTokenExpiresAt { get; set; }

    public void RotateVerificationToken()
    {
        VerificationToken = GenerateSecureToken();
        VerificationTokenExpiresAt = DateTime.UtcNow.Add(TokenLifetime);
    }

    public string SecurityStamp { get; set; } = GenerateSecureToken();

    public DateTime? AcceptedTermsAt { get; set; }
    public string? AcceptedTermsVersion { get; set; }

    private static string GenerateSecureToken()
        => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    public Organisation? Organisation { get; private set; }
}
