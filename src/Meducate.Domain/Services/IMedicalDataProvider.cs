using Meducate.Domain.Entities;

namespace Meducate.Domain.Services;

internal sealed record RawTopicData(string TopicName, string RawText, string SourceName, IReadOnlyList<string>? Groups = null, string? ContentHash = null, IReadOnlyList<SourceLink>? Links = null);

internal interface IMedicalDataProvider
{
    string SourceName { get; }
    Task<RawTopicData?> FetchTopicDataAsync(string topicName, CancellationToken ct = default);

    Task<IReadOnlyList<RawTopicData>> DiscoverTopicsAsync(
        IReadOnlySet<string> existingNames, CancellationToken ct = default);

    Task<IReadOnlySet<string>> GetKnownTopicNamesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());

    // Pages this provider's text for the topic comes from, without fetching them (used to backfill stored topics).
    Task<IReadOnlyList<SourceLink>> GetSourceLinksAsync(string topicName, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<SourceLink>>([]);
}
