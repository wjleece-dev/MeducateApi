using Meducate.Application.Helpers;
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

    public async Task<int> RenameAndRetypeAsync(string from, string to, string newType, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return await _db.HealthTopics
            .Where(c => c.Name == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Name, to)
                .SetProperty(c => c.TopicType, newType)
                .SetProperty(c => c.Category, (string?)null)
                .SetProperty(c => c.Icd11Code, (string?)null)
                .SetProperty(c => c.Icd11Title, (string?)null)
                .SetProperty(c => c.NeedsLlmReprocessing, true)
                .SetProperty(c => c.ReprocessAttempts, 0)
                .SetProperty(c => c.LastSourceRefresh, now), ct);
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

    public async Task<List<SeenTopic>> FindSeenTopicsAsync(string contains, CancellationToken ct) =>
        await _db.SeenTopics.AsNoTracking().Where(s => EF.Functions.ILike(s.Name, "%" + contains + "%")).OrderBy(s => s.Name).Take(50).ToListAsync(ct);

    // Removes or replaces single list entries by exact (case-insensitive) text, e.g. items a grounding review found
    // the source does not support. Changed topics get a new version so /topics/changes reports them.
    public async Task<(int Applied, int NotFound)> EditListItemsAsync(IReadOnlyList<Meducate.Domain.Services.ItemEdit> edits, CancellationToken ct)
    {
        int applied = 0, notFound = 0;

        foreach (var group in edits.GroupBy(e => e.Name))
        {
            var topic = await _db.HealthTopics.FirstOrDefaultAsync(t => t.Name == group.Key, ct);
            if (topic is null)
            {
                notFound += group.Count();
                continue;
            }

            var changed = false;
            foreach (var edit in group)
            {
                var list = edit.Field.ToLowerInvariant() switch
                {
                    "observations" => topic.Observations,
                    "factors" => topic.Factors,
                    "actions" => topic.Actions,
                    _ => null,
                };

                // An empty Item with ReplaceWith set adds ReplaceWith (used to restore items a bad re-extraction lost).
                if (string.IsNullOrEmpty(edit.Item) && !string.IsNullOrWhiteSpace(edit.ReplaceWith))
                {
                    var target = edit.Field.ToLowerInvariant();
                    if (target is not ("observations" or "factors" or "actions"))
                    {
                        notFound++;
                        continue;
                    }

                    list ??= target switch
                    {
                        "observations" => topic.Observations = [],
                        "factors" => topic.Factors = [],
                        _ => topic.Actions = [],
                    };
                    if (!list.Any(i => string.Equals(i, edit.ReplaceWith, StringComparison.OrdinalIgnoreCase)))
                    {
                        list.Add(edit.ReplaceWith);
                        applied++;
                        changed = true;
                    }
                    continue;
                }

                var index = list?.FindIndex(i => string.Equals(i, edit.Item, StringComparison.OrdinalIgnoreCase)) ?? -1;
                if (list is null || index < 0)
                {
                    notFound++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(edit.ReplaceWith))
                    list.RemoveAt(index);
                else
                    list[index] = edit.ReplaceWith;

                applied++;
                changed = true;
            }

            if (!changed)
                continue;

            // Reassign so EF notices the in-place change to the list column.
            topic.Observations = [.. topic.Observations ?? []];
            topic.Factors = [.. topic.Factors ?? []];
            topic.Actions = [.. topic.Actions ?? []];
            topic.LastUpdated = DateTime.UtcNow;
            topic.Version++;
        }

        await _db.SaveChangesAsync(ct);
        return (applied, notFound);
    }

    // Lets discovery pick these provider titles up again (the deduplication table otherwise blocks them).
    public async Task<int> ForgetSeenTopicsAsync(IReadOnlyCollection<string> names, CancellationToken ct) =>
        await _db.SeenTopics.Where(s => names.Contains(s.Name)).ExecuteDeleteAsync(ct);

    public async Task<int> SetCategoryAsync(string name, string category, CancellationToken ct) =>
        await _db.HealthTopics.Where(c => c.Name == name)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Category, category), ct);

    // Drops every stored ICD-11 code so the next refresh looks them all up again (after a WHO release bump or a matcher change).
    public async Task<int> ClearIcd11CodesAsync(CancellationToken ct) =>
        await _db.HealthTopics.Where(c => c.Icd11Code != null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Icd11Code, (string?)null).SetProperty(c => c.Icd11Title, (string?)null), ct);

    // Applies TextCleaner to every stored topic; changed topics get a new version so /topics/changes reports them.
    public async Task<int> SanitizeAllAsync(CancellationToken ct)
    {
        var changed = 0;
        foreach (var topic in await _db.HealthTopics.ToListAsync(ct))
        {
            if (!TextCleaner.SanitizeTopic(topic))
                continue;

            topic.LastUpdated = DateTime.UtcNow;
            topic.Version++;
            changed++;
        }

        await _db.SaveChangesAsync(ct);
        return changed;
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
