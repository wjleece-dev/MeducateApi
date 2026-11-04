using Meducate.Domain.Entities;

namespace Meducate.Domain.Services;

internal interface ILLMProcessor
{
    Task<HealthTopic?> ParseHealthTopicAsync(string rawText, string? topicType = null, string? discoveredName = null, CancellationToken ct = default, bool escalate = false);
    Task<HealthTopic?> VerifyHealthTopicAsync(string rawText, HealthTopic extracted, CancellationToken ct = default, bool escalate = false);
    Task<Dictionary<string, string>> ClassifyTopicNamesAsync(IReadOnlyList<TopicClassifyInput> topics, CancellationToken ct = default);
    Task<Dictionary<string, string>> ClassifyTopicCategoriesAsync(IReadOnlyList<TopicCategoryInput> topics, CancellationToken ct = default);
    Task<BroaderNameResult> CompareBroaderNameAsync(string candidate, string existing, CancellationToken ct = default);
    Task<Dictionary<string, string>> MatchOriginalNamesAsync(IReadOnlyList<string> normalizedNames, IReadOnlyList<string> candidateNames, CancellationToken ct = default);
    bool ShouldProcessTopicType(string? topicType);
    IReadOnlySet<string> GetValidCategories();
}

internal sealed record BroaderNameResult(string PreferredName, bool ShouldReplace);

internal sealed record TopicClassifyInput(string Name, string? SummarySnippet = null);

internal sealed record TopicCategoryInput(string Name, string TopicType, string? SummarySnippet = null);

