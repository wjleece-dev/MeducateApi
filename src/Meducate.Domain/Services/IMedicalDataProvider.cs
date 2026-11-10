namespace Meducate.Domain.Services;

internal sealed record RawTopicData(string TopicName, string RawText, string SourceName, IReadOnlyList<string>? Groups = null, string? ContentHash = null);

internal interface IMedicalDataProvider
{
    string SourceName { get; }
    Task<RawTopicData?> FetchTopicDataAsync(string topicName, CancellationToken ct = default);

    Task<IReadOnlyList<RawTopicData>> DiscoverTopicsAsync(
        IReadOnlySet<string> existingNames, CancellationToken ct = default);

    Task<IReadOnlySet<string>> GetKnownTopicNamesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<string>>(new HashSet<string>());
}
