using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace Meducate.Infrastructure.Persistence.Repositories;

internal sealed class TopicRepository(MeducateDbContext context, IMemoryCache cache) : ITopicRepository
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(2);
    private static CancellationTokenSource _cacheTokenSource = new();

    private static MemoryCacheEntryOptions CreateEntryOptions(TimeSpan duration)
    {
        return new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(duration)
            .AddExpirationToken(new CancellationChangeToken(_cacheTokenSource.Token));
    }

    private void CacheSet<T>(string key, T value, TimeSpan? duration = null) =>
        cache.Set(key, value, CreateEntryOptions(duration ?? CacheDuration));

    // Only expose topics that have been assigned a category.
    // Topics pending or failing categorisation are not served via the API.
    private static IQueryable<HealthTopic> CategorizedQuery(DbSet<HealthTopic> set) =>
        set.AsNoTracking().Where(c => c.Category != null);

    private static IQueryable<HealthTopic> ApplyTypeFilter(IQueryable<HealthTopic> query, string? topicType)
    {
        return string.IsNullOrWhiteSpace(topicType)
            ? query
            : query.Where(c => c.TopicType != null && EF.Functions.ILike(c.TopicType, topicType));
    }

    private static IQueryable<HealthTopic> ApplyCategoryFilter(IQueryable<HealthTopic> query, string? category)
    {
        return string.IsNullOrWhiteSpace(category)
            ? query
            : query.Where(c => c.Category != null && EF.Functions.ILike(c.Category, category));
    }

    private static IQueryable<TopicListItem> ProjectToListItem(IQueryable<HealthTopic> query)
    {
        return query.Select(c => new TopicListItem(
            c.Id,
            c.Name,
            c.Summary,
            c.TopicType,
            c.Category,
            c.Icd11Code,
            c.LastUpdated));
    }

    private static class CacheKeys
    {
        public static string All(int skip, int take, string? type, string? category) =>
            $"topics:all:{skip}:{take}:{type?.ToLowerInvariant()}:{category?.ToLowerInvariant()}";

        public static string Count(string? type, string? category) =>
            $"topics:count:{type?.ToLowerInvariant()}:{category?.ToLowerInvariant()}";

        public static string ByName(string name) =>
            $"topics:name:{name.ToLowerInvariant()}";

        public static string ByIcd11Code(string code) =>
            $"topics:icd11:{code.ToLowerInvariant()}";

        public static string Search(string query, int skip, int take, string? type, string? category) =>
            $"topics:search:{query.ToLowerInvariant()}:{skip}:{take}:{type?.ToLowerInvariant()}:{category?.ToLowerInvariant()}";

        public static string SearchCount(string query, string? type, string? category) =>
            $"topics:searchcount:{query.ToLowerInvariant()}:{type?.ToLowerInvariant()}:{category?.ToLowerInvariant()}";

        public const string Types = "topics:types";
        public const string Categories = "topics:categories";
    }

    public async Task<IEnumerable<TopicListItem>> GetAllAsync(int skip = 0, int take = 50, string? topicType = null, string? category = null, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.All(skip, take, topicType, category);
        if (cache.TryGetValue(cacheKey, out IEnumerable<TopicListItem>? cached) && cached is not null)
            return cached;

        var query = ApplyCategoryFilter(ApplyTypeFilter(CategorizedQuery(context.HealthTopics), topicType), category);

        var results = await ProjectToListItem(
                query.OrderBy(c => c.Name).Skip(skip).Take(take))
            .ToListAsync(ct);

        CacheSet(cacheKey, results);
        return results;
    }

    public async Task<int> GetCountAsync(string? topicType = null, string? category = null, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.Count(topicType, category);
        if (cache.TryGetValue(cacheKey, out int cached))
            return cached;

        var query = ApplyCategoryFilter(ApplyTypeFilter(CategorizedQuery(context.HealthTopics), topicType), category);

        var count = await query.CountAsync(ct);
        CacheSet(cacheKey, count);
        return count;
    }

    private static readonly object NegativeCacheSentinel = new();

    public async Task<HealthTopic?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.ByName(name);
        if (cache.TryGetValue(cacheKey, out object? cached))
            return ReferenceEquals(cached, NegativeCacheSentinel) ? null : cached as HealthTopic;

        var result = await CategorizedQuery(context.HealthTopics)
            .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Name, name), ct);

        // Cache hits for 10 min, misses for 2 min to avoid repeated DB lookups
        var duration = result is not null ? CacheDuration : NegativeCacheDuration;
        CacheSet(cacheKey, (object?)result ?? NegativeCacheSentinel, duration);

        return result;
    }

    public async Task<HealthTopic?> GetByIcd11CodeAsync(string icd11Code, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.ByIcd11Code(icd11Code);
        if (cache.TryGetValue(cacheKey, out object? cached))
            return ReferenceEquals(cached, NegativeCacheSentinel) ? null : cached as HealthTopic;

        // Codes are case-sensitive per the WHO ICD-11 spec (e.g. "5A11"), but ILike keeps
        // this forgiving for callers who don't get the casing exactly right.
        var result = await CategorizedQuery(context.HealthTopics)
            .FirstOrDefaultAsync(c => c.Icd11Code != null && EF.Functions.ILike(c.Icd11Code, icd11Code), ct);

        var duration = result is not null ? CacheDuration : NegativeCacheDuration;
        CacheSet(cacheKey, (object?)result ?? NegativeCacheSentinel, duration);

        return result;
    }

    private static string EscapeLikeQuery(string query) => query
        .Replace("\\", "\\\\")
        .Replace("%", "\\%")
        .Replace("_", "\\_");

    public async Task<IEnumerable<TopicListItem>> SearchAsync(string query, int skip = 0, int take = 50, string? topicType = null, string? category = null, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.Search(query, skip, take, topicType, category);
        if (cache.TryGetValue(cacheKey, out IEnumerable<TopicListItem>? cached) && cached is not null)
            return cached;

        var escaped = EscapeLikeQuery(query);

        var dbQuery = ApplyCategoryFilter(
            ApplyTypeFilter(
                CategorizedQuery(context.HealthTopics)
                    .Where(c => EF.Functions.ILike(c.Name, $"%{escaped}%", "\\")),
                topicType),
            category);

        var results = await ProjectToListItem(
                dbQuery.OrderBy(c => c.Name).Skip(skip).Take(take))
            .ToListAsync(ct);

        CacheSet(cacheKey, results);
        return results;
    }

    public async Task<int> SearchCountAsync(string query, string? topicType = null, string? category = null, CancellationToken ct = default)
    {
        var cacheKey = CacheKeys.SearchCount(query, topicType, category);
        if (cache.TryGetValue(cacheKey, out int cached))
            return cached;

        var escaped = EscapeLikeQuery(query);

        var dbQuery = ApplyCategoryFilter(
            ApplyTypeFilter(
                CategorizedQuery(context.HealthTopics)
                    .Where(c => EF.Functions.ILike(c.Name, $"%{escaped}%", "\\")),
                topicType),
            category);

        var count = await dbQuery.CountAsync(ct);
        CacheSet(cacheKey, count);
        return count;
    }

    public async Task<IReadOnlyList<TopicTypeSummary>> GetDistinctTypesAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKeys.Types, out IReadOnlyList<TopicTypeSummary>? cached) && cached is not null)
            return cached;

        var types = (await CategorizedQuery(context.HealthTopics)
            .Where(c => c.TopicType != null)
            .GroupBy(c => c.TopicType!)
            .Select(g => new { Type = g.Key, Count = g.Count() })
            .OrderBy(t => t.Type)
            .ToListAsync(ct))
            .Select(t => new TopicTypeSummary(t.Type, t.Count))
            .ToList();

        CacheSet(CacheKeys.Types, (IReadOnlyList<TopicTypeSummary>)types);
        return types;
    }

    public async Task<IReadOnlyList<TopicCategorySummary>> GetDistinctCategoriesAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKeys.Categories, out IReadOnlyList<TopicCategorySummary>? cached) && cached is not null)
            return cached;

        var categories = (await CategorizedQuery(context.HealthTopics)
            .GroupBy(c => c.Category!)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderBy(c => c.Category)
            .ToListAsync(ct))
            .Select(c => new TopicCategorySummary(c.Category, c.Count))
            .ToList();

        CacheSet(CacheKeys.Categories, (IReadOnlyList<TopicCategorySummary>)categories);
        return categories;
    }

    // Deliberately uncached: `since` varies per-request with no realistic reuse across
    // callers, so caching this would just add memory pressure for near-zero hit rate.
    public async Task<IEnumerable<TopicChangeItem>> GetChangedSinceAsync(DateTime since, int skip = 0, int take = 50, CancellationToken ct = default)
    {
        return await CategorizedQuery(context.HealthTopics)
            .Where(c => c.LastUpdated > since)
            .OrderByDescending(c => c.LastUpdated)
            .Skip(skip)
            .Take(take)
            .Select(c => new TopicChangeItem(
                c.Name,
                c.TopicType,
                c.Category,
                c.Version == 1 ? "Added" : "Updated",
                c.Version,
                c.LastUpdated))
            .ToListAsync(ct);
    }

    public async Task<int> GetChangedSinceCountAsync(DateTime since, CancellationToken ct = default)
    {
        return await CategorizedQuery(context.HealthTopics)
            .Where(c => c.LastUpdated > since)
            .CountAsync(ct);
    }

    public void InvalidateCache()
    {
        var old = Interlocked.Exchange(ref _cacheTokenSource, new CancellationTokenSource());
        old.Cancel();
        // Do not dispose — outstanding CancellationChangeTokens still reference old.Token.
        // The GC will collect it once all references are released.
    }
}
