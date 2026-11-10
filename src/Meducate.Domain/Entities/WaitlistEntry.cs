namespace Meducate.Domain.Entities;

internal sealed class WaitlistEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Stored trimmed + lowercased; unique, so the same person can't be listed twice.
    public string Email { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
