using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Meducate.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(MeducateDbContext db) : IUserRepository
{
    private readonly MeducateDbContext _db = db;

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email.Trim().ToLowerInvariant(), ct);

    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    public async Task<User> GetOrCreateAsync(string email, CancellationToken ct = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

        if (user is null)
        {
            user = new User { Email = normalizedEmail, IsEmailVerified = false };
            user.RotateVerificationToken();

            _db.Users.Add(user);
        }
        else if (!user.IsEmailVerified)
        {
            user.RotateVerificationToken();
        }

        try
        {
            await _db.SaveChangesAsync(ct);
            return user;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Clear the failed entity from the change tracker to avoid
            // re-inserting it on a subsequent SaveChangesAsync call.
            _db.ChangeTracker.Clear();

            // Handles race condition if two requests insert the same email
            var existing = await _db.Users.FirstAsync(u => u.Email == normalizedEmail, ct);
            return existing;
        }
    }

    public Task<User?> GetByVerificationTokenAsync(string token, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u =>
            u.VerificationToken == token &&
            u.VerificationTokenExpiresAt != null &&
            u.VerificationTokenExpiresAt > DateTime.UtcNow, ct);

    public Task<User?> GetByVerificationTokenIncludingExpiredAsync(string token, CancellationToken ct = default) =>
        _db.Users.FirstOrDefaultAsync(u => u.VerificationToken == token, ct);

    public async Task VerifyAsync(User user, CancellationToken ct = default)
    {
        user.IsEmailVerified = true;
        user.VerificationToken = null;
        user.VerificationTokenExpiresAt = null;

        await _db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
