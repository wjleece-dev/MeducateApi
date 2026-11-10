using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Meducate.Infrastructure.Persistence.Repositories;

internal sealed class OrganisationRepository(MeducateDbContext db) : IOrganisationRepository
{
    private readonly MeducateDbContext _db = db;

    public Task<Organisation?> GetByUserIdAsync(Guid userId, CancellationToken ct = default) =>
        _db.Organisations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.UserId == userId, ct);

    public Task<Organisation?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Organisations
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<Organisation> CreateAsync(string name, Guid userId, CancellationToken ct = default)
    {
        var org = new Organisation
        {
            Name = name,
            UserId = userId
        };

        _db.Organisations.Add(org);
        await _db.SaveChangesAsync(ct);
        return org;
    }
}
