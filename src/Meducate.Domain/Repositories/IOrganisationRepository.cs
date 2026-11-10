using Meducate.Domain.Entities;

namespace Meducate.Domain.Repositories;

internal interface IOrganisationRepository
{
    Task<Organisation?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<Organisation?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Organisation> CreateAsync(string name, Guid userId, CancellationToken ct = default);
}
