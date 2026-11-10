namespace Meducate.Domain.Entities;

internal sealed class ApiClient
{
    public Guid OrganisationId { get; set; }
    public Organisation Organisation { get; set; } = null!;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string KeyId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int DailyLimit { get; set; }
    public bool IsActive { get; set; }
    public string HashedSecret { get; set; } = null!;
    public string Salt { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTime.UtcNow;
}
