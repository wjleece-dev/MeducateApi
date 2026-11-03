using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Meducate.Infrastructure.Persistence.Repositories;

internal sealed class WaitlistRepository(MeducateDbContext db) : IWaitlistRepository
{
    private readonly MeducateDbContext _db = db;

    public async Task<bool> TryAddAsync(string normalizedEmail, CancellationToken ct = default)
    {
        if (await _db.WaitlistEntries.AnyAsync(w => w.Email == normalizedEmail, ct))
            return false;

        _db.WaitlistEntries.Add(new WaitlistEntry { Email = normalizedEmail });

        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two concurrent requests for the same email: the other one won.
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public Task<int> CountSinceAsync(DateTime sinceUtc, CancellationToken ct = default) =>
        _db.WaitlistEntries.CountAsync(w => w.CreatedAt >= sinceUtc, ct);

    public Task<int> CountAsync(CancellationToken ct = default) => _db.WaitlistEntries.CountAsync(ct);

    public async Task<IReadOnlyList<WaitlistEntry>> ListAsync(int skip, int take, CancellationToken ct = default) =>
        await _db.WaitlistEntries.AsNoTracking()
            .OrderByDescending(w => w.CreatedAt)
            .Skip(skip).Take(take)
            .ToListAsync(ct);
}
