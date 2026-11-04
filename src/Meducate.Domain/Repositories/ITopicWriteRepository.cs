using Meducate.Domain.Entities;

namespace Meducate.Domain.Repositories;

internal interface ITopicWriteRepository
{
    Task AddAsync(HealthTopic topic, CancellationToken ct);
    Task RemoveRangeAsync(IEnumerable<HealthTopic> topics, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
    bool HasChanges();
    void RevertChanges(HealthTopic topic);
    void RevertChanges(IEnumerable<HealthTopic> topics);
    Task AddSeenTopicsAsync(IEnumerable<SeenTopic> topics, CancellationToken ct);
    Task<int> RetypeAsync(string name, string newType, CancellationToken ct);
    Task<int> FlagForReprocessingAsync(IReadOnlyCollection<string> topicTypes, IReadOnlyCollection<string> names, CancellationToken ct);
}
