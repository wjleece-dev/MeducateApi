using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Meducate.Infrastructure.Persistence.Repositories;

internal sealed class TopicWriteRepository(MeducateDbContext db) : ITopicWriteRepository, ITopicQueryRepository
{
    private readonly MeducateDbContext _db = db;

    public async Task<List<string>> GetAllTopicNamesAsync(CancellationToken ct) =>
        await _db.HealthTopics.AsNoTracking().Select(c => c.Name).ToListAsync(ct);

    public async Task<List<HealthTopic>> GetTopicsNeedingRefreshAsync(DateTime cutoff, CancellationToken ct) =>
        await _db.HealthTopics
            .Where(c => c.LastSourceRefresh == null || c.LastSourceRefresh < cutoff)
            .OrderBy(c => c.LastSourceRefresh)
            .ToListAsync(ct);

    public async Task<List<HealthTopic>> GetTopicsNeedingReprocessingAsync(CancellationToken ct)
    {
        // Only reprocess topics whose source was refreshed recently (within last 2 days)
        // This prevents endlessly reprocessing low-quality topics whose source hasn't changed
        var cutoff = DateTime.UtcNow.AddDays(-2);
        return await _db.HealthTopics
            .Where(c => c.NeedsLlmReprocessing &&
                       (c.LastSourceRefresh == null || c.LastSourceRefresh >= cutoff))
            .ToListAsync(ct);
    }

    public async Task<int> RetypeAsync(string name, string newType, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // Category and ICD-11 data belong to the old type, so clear them for the next refresh to redo;
        // reprocessing re-reads the lists under the new type's instructions.
        return await _db.HealthTopics
            .Where(c => c.Name == name)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.TopicType, newType)
                .SetProperty(c => c.Category, (string?)null)
                .SetProperty(c => c.Icd11Code, (string?)null)
                .SetProperty(c => c.Icd11Title, (string?)null)
                .SetProperty(c => c.NeedsLlmReprocessing, true)
                .SetProperty(c => c.ReprocessAttempts, 0)
                .SetProperty(c => c.LastSourceRefresh, now), ct);
    }

    // Makes the next refresh run re-fetch every topic's source, re-extracting any whose text changed
    // (e.g. after the provider's raw-text format changes).
    public async Task<int> ForceSourceRefreshAsync(CancellationToken ct) =>
        await _db.HealthTopics.ExecuteUpdateAsync(s => s.SetProperty(c => c.LastSourceRefresh, (DateTime?)null), ct);

    public async Task<int> FlagForReprocessingAsync(IReadOnlyCollection<string> topicTypes, IReadOnlyCollection<string> names, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        // LastSourceRefresh is touched so GetTopicsNeedingReprocessingAsync (2-day cutoff) picks these up
        return await _db.HealthTopics
            .Where(c => (c.TopicType != null && topicTypes.Contains(c.TopicType)) || names.Contains(c.Name))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.NeedsLlmReprocessing, true)
                .SetProperty(c => c.LastSourceRefresh, now), ct);
    }

    public async Task<List<HealthTopic>> GetUncategorizedTopicsAsync(CancellationToken ct) =>
        await _db.HealthTopics.Where(c => c.Category == null).ToListAsync(ct);

    public async Task<List<HealthTopic>> GetTopicsNeedingCategoryAsync(IReadOnlyCollection<string> validCategories, CancellationToken ct) =>
        await _db.HealthTopics
            .Where(c => c.Category == null || !validCategories.Contains(c.Category))
            .ToListAsync(ct);

    public async Task<List<HealthTopic>> GetTopicsNeedingIcd11Async(IReadOnlyCollection<string> codeableTypes, CancellationToken ct) =>
        await _db.HealthTopics
            .Where(c => c.Category != null && c.TopicType != null && codeableTypes.Contains(c.TopicType) && c.Icd11Code == null)
            .ToListAsync(ct);

    public async Task<int> GetServedTopicCountAsync(CancellationToken ct) =>
        await _db.HealthTopics.CountAsync(c => c.Category != null, ct);

    public async Task<List<HealthTopic>> GetServedTopicBatchAsync(int skip, int take, CancellationToken ct) =>
        await _db.HealthTopics
            .Where(c => c.Category != null)
            .OrderBy(c => c.Name)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    public async Task<List<HealthTopic>> GetUnclassifiedTopicsAsync(CancellationToken ct) =>
        await _db.HealthTopics
            .Where(c => c.TopicType == null || c.TopicType == "Other")
            .ToListAsync(ct);

    public Task<HealthTopic?> GetByNameTrackedAsync(string name, CancellationToken ct) =>
        _db.HealthTopics.FirstOrDefaultAsync(c => c.Name.ToLower() == name.ToLower(), ct);

    public async Task<List<HealthTopic>> GetByNamesTrackedAsync(IEnumerable<string> names, CancellationToken ct) =>
        await _db.HealthTopics.Where(c => names.Contains(c.Name)).ToListAsync(ct);

    public Task AddAsync(HealthTopic topic, CancellationToken ct)
    {
        _db.HealthTopics.Add(topic);
        return Task.CompletedTask;
    }

    public Task RemoveRangeAsync(IEnumerable<HealthTopic> topics, CancellationToken ct)
    {
        _db.HealthTopics.RemoveRange(topics);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct) => _db.SaveChangesAsync(ct);

    public bool HasChanges() => _db.ChangeTracker.HasChanges();

    public void RevertChanges(HealthTopic topic)
    {
        var entry = _db.Entry(topic);
        if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
        {
            entry.CurrentValues.SetValues(entry.OriginalValues);
            entry.State = EntityState.Unchanged;
        }
    }

    public void RevertChanges(IEnumerable<HealthTopic> topics)
    {
        foreach (var topic in topics)
        {
            var entry = _db.Entry(topic);
            if (entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
            }
        }
    }

    public async Task<List<HealthTopic>> GetTopicsWithoutOriginalNameAsync(CancellationToken ct) =>
        await _db.HealthTopics.Where(c => c.OriginalName == null).ToListAsync(ct);

    public async Task<Dictionary<string, string>> GetOriginalNameMappingsAsync(CancellationToken ct)
    {
        var pairs = await _db.HealthTopics
            .AsNoTracking()
            .Where(c => c.OriginalName != null)
            .Select(c => new { c.Name, c.OriginalName })
            .ToListAsync(ct);

        return pairs.ToDictionary(p => p.Name, p => p.OriginalName!, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<HashSet<string>> GetAllSeenTopicNamesAsync(CancellationToken ct)
    {
        var names = await _db.SeenTopics.AsNoTracking().Select(s => s.Name).ToListAsync(ct);
        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    public async Task AddSeenTopicsAsync(IEnumerable<SeenTopic> topics, CancellationToken ct)
    {
        _db.SeenTopics.AddRange(topics);
        await _db.SaveChangesAsync(ct);
    }

    private const int MaxReprocessAttempts = 3;

    public async Task<List<HealthTopic>> GetTopicsWithEmptyStructuredFieldsAsync(CancellationToken ct) =>
        await _db.HealthTopics
            .Where(c => !c.NeedsLlmReprocessing
                && c.ReprocessAttempts < MaxReprocessAttempts
                && ((c.Observations == null || c.Observations.Count == 0)
                || (c.Factors == null || c.Factors.Count == 0)
                || (c.Actions == null || c.Actions.Count == 0)))
            .ToListAsync(ct);

}
