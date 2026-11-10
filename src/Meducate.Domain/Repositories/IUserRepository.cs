using Meducate.Domain.Entities;

namespace Meducate.Domain.Repositories;

internal interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<User> GetOrCreateAsync(string email, CancellationToken ct = default);

    Task<User?> GetByVerificationTokenAsync(string token, CancellationToken ct = default);
    Task<User?> GetByVerificationTokenIncludingExpiredAsync(string token, CancellationToken ct = default);

    Task VerifyAsync(User user, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
