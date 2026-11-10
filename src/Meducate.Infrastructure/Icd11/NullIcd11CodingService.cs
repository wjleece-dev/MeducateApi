using Meducate.Domain.Services;

namespace Meducate.Infrastructure.Icd11;

// Used when Icd11:ClientId/Icd11:ClientSecret aren't configured (local dev, CI,
// integration tests) so the app runs without WHO credentials instead of failing startup.
internal sealed class NullIcd11CodingService : IIcd11CodingService
{
    public Task<Icd11Match?> LookupAsync(string topicName, CancellationToken ct = default) =>
        Task.FromResult<Icd11Match?>(null);
}
