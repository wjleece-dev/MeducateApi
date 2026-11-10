using Meducate.Domain.Entities;
using Meducate.Domain.Services;
using Meducate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meducate.Infrastructure.LLM;

// Singleton-safe: every operation opens its own scope, because the tracker outlives any request or job scope.
internal sealed class LlmUsageStore(IServiceScopeFactory scopes) : ILlmUsageStore
{
    public async Task<List<LlmUsage>> GetSinceAsync(DateTime sinceUtc, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeducateDbContext>();
        return await db.LlmUsages.AsNoTracking().Where(u => u.At >= sinceUtc).ToListAsync(ct);
    }

    public async Task AddAsync(LlmUsage usage, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeducateDbContext>();
        db.LlmUsages.Add(usage);
        await db.SaveChangesAsync(ct);
    }
}
