using Meducate.Domain.Entities;

namespace Meducate.Domain.Repositories;

internal interface ITopicRepository
{
    Task<IEnumerable<TopicListItem>> GetAllAsync(int skip = 0, int take = 50, string? topicType = null, string? category = null, CancellationToken ct = default);
    Task<int> GetCountAsync(string? topicType = null, string? category = null, CancellationToken ct = default);
    Task<HealthTopic?> GetByNameAsync(string name, CancellationToken ct = default);
    Task<HealthTopic?> GetByIcd11CodeAsync(string icd11Code, CancellationToken ct = default);
    Task<IEnumerable<TopicListItem>> SearchAsync(string query, int skip = 0, int take = 50, string? topicType = null, string? category = null, CancellationToken ct = default);
    Task<int> SearchCountAsync(string query, string? topicType = null, string? category = null, CancellationToken ct = default);
    Task<IReadOnlyList<TopicTypeSummary>> GetDistinctTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TopicCategorySummary>> GetDistinctCategoriesAsync(CancellationToken ct = default);
    Task<IEnumerable<TopicChangeItem>> GetChangedSinceAsync(DateTime since, int skip = 0, int take = 50, CancellationToken ct = default);
    Task<int> GetChangedSinceCountAsync(DateTime since, CancellationToken ct = default);
    void InvalidateCache();
}

/// <summary>
/// Lightweight projection for topic listings — avoids loading JSONB columns.
/// </summary>
public sealed record TopicListItem(
    Guid Id,
    string Name,
    string? Summary,
    string? TopicType,
    string? Category,
    string? Icd11Code,
    DateTime LastUpdated);

internal sealed record TopicTypeSummary(string Type, int Count)
{
    public string Href => $"/api/v1/topics?type={Uri.EscapeDataString(Type)}";
}

internal sealed record TopicCategorySummary(string Category, int Count)
{
    public string Href => $"/api/v1/topics?category={Uri.EscapeDataString(Category)}";
}

/// <summary>
/// A topic added or updated since a given point in time. "Added" means the topic has
/// never been reprocessed since creation (Version == 1) — an approximation, not a
/// precise add/update distinction. Removals are not currently tracked.
/// </summary>
internal sealed record TopicChangeItem(
    string Name,
    string? TopicType,
    string? Category,
    string ChangeType,
    int Version,
    DateTime LastUpdated);
