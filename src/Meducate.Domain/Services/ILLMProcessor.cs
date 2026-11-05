using Meducate.Domain.Entities;

namespace Meducate.Domain.Services;

internal interface ILLMProcessor
{
    Task<HealthTopic?> ParseHealthTopicAsync(string rawText, string? topicType = null, string? discoveredName = null, CancellationToken ct = default, bool escalate = false);
    Task<HealthTopic?> VerifyHealthTopicAsync(string rawText, HealthTopic extracted, CancellationToken ct = default, bool escalate = false);
    // Asks the model whether the source backs each observation/factor/action. Null when the reply is unusable
    // (callers then keep everything); budget and transport errors propagate.
    Task<IReadOnlyList<ItemJudgement>?> JudgeListItemsAsync(string rawText, HealthTopic topic, CancellationToken ct = default, bool escalate = false);
    Task<Dictionary<string, string>> ClassifyTopicNamesAsync(IReadOnlyList<TopicClassifyInput> topics, CancellationToken ct = default);
    Task<Dictionary<string, string>> ClassifyTopicCategoriesAsync(IReadOnlyList<TopicCategoryInput> topics, CancellationToken ct = default);
    Task<BroaderNameResult> CompareBroaderNameAsync(string candidate, string existing, CancellationToken ct = default);
    Task<Dictionary<string, string>> MatchOriginalNamesAsync(IReadOnlyList<string> normalizedNames, IReadOnlyList<string> candidateNames, CancellationToken ct = default);
    bool ShouldProcessTopicType(string? topicType);
    IReadOnlySet<string> GetValidCategories();
}

// Field is "observations", "factors" or "actions". Quote is the passage of the source the model says backs the item.
internal sealed record ItemJudgement(string Field, string Item, bool Supported, string? Quote);

internal sealed record BroaderNameResult(string PreferredName, bool ShouldReplace);

internal sealed record TopicClassifyInput(string Name, string? SummarySnippet = null);

internal sealed record TopicCategoryInput(string Name, string TopicType, string? SummarySnippet = null);

