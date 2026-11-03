using Meducate.Domain.Entities;

namespace Meducate.Domain.Repositories;

internal interface IWaitlistRepository
{
    /// <summary>Adds the (already normalised) email. Returns false if it was already on the list.</summary>
    Task<bool> TryAddAsync(string normalizedEmail, CancellationToken ct = default);

    Task<int> CountSinceAsync(DateTime sinceUtc, CancellationToken ct = default);

    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<WaitlistEntry>> ListAsync(int skip, int take, CancellationToken ct = default);
}
