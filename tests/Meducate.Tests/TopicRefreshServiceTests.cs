using Meducate.Application.Services;
using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;
using Meducate.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meducate.Tests;

public class TopicRefreshServiceTests
{
    private static TopicRefreshService BuildService(
        IEnumerable<IMedicalDataProvider> providers,
        FakeQueryRepo queryRepo,
        FakeWriteRepo writeRepo,
        FakeLlmProcessor llmProcessor,
        FakeTopicRepo? topicRepo = null,
        FakeIcd11CodingService? icd11Service = null)
    {
        var backfill = new TopicBackfillService(queryRepo, writeRepo, llmProcessor, icd11Service ?? new FakeIcd11CodingService(), NullLogger<TopicBackfillService>.Instance);
        return new TopicRefreshService(providers, queryRepo, writeRepo, llmProcessor, topicRepo ?? new FakeTopicRepo(), backfill, NullLogger<TopicRefreshService>.Instance);
    }

    private static HealthTopic GoodQualityTopic(string name) => new()
    {
        Name = name,
        Summary = "This is a sufficiently detailed summary describing the condition in full clinical terms for testing.",
        Observations = ["Fatigue"],
        Factors = ["Genetics"],
        Actions = ["Rest"]
    };

    [Fact]
    public async Task RefreshAllAsync_FlagsReprocessing_WhenSourceHashChanges()
    {
        var topic = new HealthTopic { Name = "Topic A", SourceHash = "old-hash", ReprocessAttempts = 2 };
        var provider = new FakeProvider("TestSource", name => new RawTopicData(name, new string('x', 200), "TestSource"));
        // Intentionally not added to AllTopics — isolates Phase 1 from Phase 2, which would
        // otherwise immediately reprocess (and clear) the flag Phase 1 just set.
        var queryRepo = new FakeQueryRepo { NeedingRefresh = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor();
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.RefreshAllAsync();

        Assert.True(topic.NeedsLlmReprocessing);
        Assert.Equal(0, topic.ReprocessAttempts);
        Assert.NotEqual("old-hash", topic.SourceHash);
    }

    [Fact]
    public async Task RefreshAllAsync_Reprocess_SuccessClearsFlag_AndIncrementsVersion()
    {
        var topic = new HealthTopic { Name = "Topic A", RawSource = new string('x', 200), NeedsLlmReprocessing = true, Version = 1 };
        var queryRepo = new FakeQueryRepo { AllTopics = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { ParseResult = GoodQualityTopic("Topic A") };
        var service = BuildService([], queryRepo, writeRepo, llm);

        await service.RefreshAllAsync();

        Assert.False(topic.NeedsLlmReprocessing);
        Assert.Equal(0, topic.ReprocessAttempts);
        Assert.Equal(2, topic.Version);
        Assert.True(llm.VerifyCalled);
    }

    [Fact]
    public async Task RefreshAllAsync_Reprocess_ClearsFlag_WhenLlmReturnsNull()
    {
        var topic = new HealthTopic { Name = "Topic A", RawSource = new string('x', 200), NeedsLlmReprocessing = true };
        var queryRepo = new FakeQueryRepo { AllTopics = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { ParseResult = null };
        var service = BuildService([], queryRepo, writeRepo, llm);

        await service.RefreshAllAsync();

        Assert.False(topic.NeedsLlmReprocessing);
    }

    [Fact]
    public async Task RefreshAllAsync_Reprocess_SkipsLlmCall_WhenSourceTooShort()
    {
        var topic = new HealthTopic { Name = "Topic A", RawSource = "short", NeedsLlmReprocessing = true };
        var queryRepo = new FakeQueryRepo { AllTopics = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { ParseResult = GoodQualityTopic("Topic A") };
        var service = BuildService([], queryRepo, writeRepo, llm);

        await service.RefreshAllAsync();

        Assert.False(topic.NeedsLlmReprocessing);
        Assert.False(llm.ParseCalled);
    }

    [Fact]
    public async Task RefreshAllAsync_Reprocess_RenamesOnCaseOnlyMatch()
    {
        var topic = new HealthTopic { Name = "copd", RawSource = new string('x', 200), NeedsLlmReprocessing = true };
        var queryRepo = new FakeQueryRepo { AllTopics = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { ParseResult = GoodQualityTopic("COPD") };
        var service = BuildService([], queryRepo, writeRepo, llm);

        await service.RefreshAllAsync();

        Assert.Equal("COPD", topic.Name);
    }

    [Fact]
    public async Task RefreshAllAsync_Reprocess_KeepsOriginalName_WhenLlmReturnsDifferentSubject()
    {
        var topic = new HealthTopic { Name = "Bronchitis", RawSource = new string('x', 200), NeedsLlmReprocessing = true };
        var queryRepo = new FakeQueryRepo { AllTopics = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { ParseResult = GoodQualityTopic("Something Else Entirely") };
        var service = BuildService([], queryRepo, writeRepo, llm);

        await service.RefreshAllAsync();

        Assert.Equal("Bronchitis", topic.Name);
    }

    [Fact]
    public async Task RefreshAllAsync_Reprocess_GivesUp_AfterMaxAttempts()
    {
        var lowQuality = GoodQualityTopic("Topic A");
        lowQuality.Observations = [];
        var topic = new HealthTopic { Name = "Topic A", RawSource = new string('x', 200), NeedsLlmReprocessing = true, ReprocessAttempts = 2 };
        var queryRepo = new FakeQueryRepo { AllTopics = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { ParseResult = lowQuality };
        var service = BuildService([], queryRepo, writeRepo, llm);

        await service.RefreshAllAsync();

        Assert.Equal(3, topic.ReprocessAttempts);
        Assert.False(topic.NeedsLlmReprocessing);
    }

    [Fact]
    public async Task RefreshAllAsync_Reprocess_KeepsFlagTrue_WhenBelowMaxAttempts()
    {
        var lowQuality = GoodQualityTopic("Topic A");
        lowQuality.Observations = [];
        var topic = new HealthTopic { Name = "Topic A", RawSource = new string('x', 200), NeedsLlmReprocessing = true, ReprocessAttempts = 0 };
        var queryRepo = new FakeQueryRepo { AllTopics = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { ParseResult = lowQuality };
        var service = BuildService([], queryRepo, writeRepo, llm);

        await service.RefreshAllAsync();

        Assert.Equal(1, topic.ReprocessAttempts);
        Assert.True(topic.NeedsLlmReprocessing);
    }

    [Fact]
    public async Task RefreshAllAsync_Phase3_AssignsCategory_ToUncategorizedTopic()
    {
        var topic = new HealthTopic { Name = "Topic A", TopicType = "Disease", Category = null };
        var queryRepo = new FakeQueryRepo { AllTopics = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { CategoryResult = new() { ["Topic A"] = "Nervous System" } };
        var service = BuildService([], queryRepo, writeRepo, llm);

        await service.RefreshAllAsync();

        Assert.Equal("Nervous System", topic.Category);
        Assert.DoesNotContain(topic, writeRepo.Removed);
    }

    [Fact]
    public async Task RefreshAllAsync_Phase4_RemovesTopic_StillUncategorizedAfterPhase3()
    {
        var topic = new HealthTopic { Name = "Unclassifiable Topic", TopicType = "Disease", Category = null };
        var queryRepo = new FakeQueryRepo { AllTopics = [topic] };
        var writeRepo = new FakeWriteRepo();
        // No category returned for this topic — Phase 3 can't fix it, Phase 4 removes it.
        var llm = new FakeLlmProcessor { CategoryResult = [] };
        var service = BuildService([], queryRepo, writeRepo, llm);

        await service.RefreshAllAsync();

        Assert.Contains(topic, writeRepo.Removed);
    }

    [Fact]
    public async Task RefreshAllAsync_SwallowsProviderFetchException()
    {
        var topic = new HealthTopic { Name = "Topic A", SourceHash = "old-hash" };
        var throwing = new ThrowingProvider("BrokenSource");
        var queryRepo = new FakeQueryRepo { NeedingRefresh = [topic], AllTopics = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor();
        var service = BuildService([throwing], queryRepo, writeRepo, llm);

        // Provider throws for every fetch — no exception should escape RefreshAllAsync,
        // and the topic is left untouched (no data returned to apply).
        await service.RefreshAllAsync();

        Assert.Equal("old-hash", topic.SourceHash);
    }

    [Fact]
    public async Task RefreshAllAsync_AssignsIcd11Code_ForMatchedDiagnosableTopic()
    {
        var topic = new HealthTopic { Name = "Asthma", TopicType = "Disease", Category = "Respiratory System" };
        var queryRepo = new FakeQueryRepo { NeedingIcd11 = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor();
        var icd11 = new FakeIcd11CodingService();
        icd11.Matches["Asthma"] = new Icd11Match("CA23", "Asthma");
        var service = BuildService([], queryRepo, writeRepo, llm, icd11Service: icd11);

        await service.RefreshAllAsync();

        Assert.Equal("CA23", topic.Icd11Code);
        Assert.Equal("Asthma", topic.Icd11Title);
    }

    [Fact]
    public async Task RefreshAllAsync_LeavesIcd11CodeNull_WhenNoConfidentMatch()
    {
        var topic = new HealthTopic { Name = "Made Up Condition", TopicType = "Disease", Category = "Symptoms & Signs" };
        var queryRepo = new FakeQueryRepo { NeedingIcd11 = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor();
        var icd11 = new FakeIcd11CodingService();
        var service = BuildService([], queryRepo, writeRepo, llm, icd11Service: icd11);

        await service.RefreshAllAsync();

        Assert.Null(topic.Icd11Code);
        Assert.Contains("Made Up Condition", icd11.LookedUpNames);
    }

    private sealed class FakeProvider(string sourceName, Func<string, RawTopicData?> fetch) : IMedicalDataProvider
    {
        public string SourceName => sourceName;
        public Task<RawTopicData?> FetchTopicDataAsync(string topicName, CancellationToken ct = default) => Task.FromResult(fetch(topicName));
        public Task<IReadOnlyList<RawTopicData>> DiscoverTopicsAsync(IReadOnlySet<string> existingNames, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlySet<string>> GetKnownTopicNamesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class ThrowingProvider(string sourceName) : IMedicalDataProvider
    {
        public string SourceName => sourceName;
        public Task<RawTopicData?> FetchTopicDataAsync(string topicName, CancellationToken ct = default) => throw new InvalidOperationException("provider down");
        public Task<IReadOnlyList<RawTopicData>> DiscoverTopicsAsync(IReadOnlySet<string> existingNames, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlySet<string>> GetKnownTopicNamesAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeLlmProcessor : ILLMProcessor
    {
        public HealthTopic? ParseResult { get; set; }
        public bool ParseCalled { get; private set; }
        public bool VerifyCalled { get; private set; }
        public Dictionary<string, string> CategoryResult { get; set; } = [];

        public Task<HealthTopic?> ParseHealthTopicAsync(string rawText, string? topicType = null, string? discoveredName = null, CancellationToken ct = default, bool escalate = false)
        {
            ParseCalled = true;
            return Task.FromResult(ParseResult);
        }

        public Task<HealthTopic?> VerifyHealthTopicAsync(string rawText, HealthTopic extracted, CancellationToken ct = default, bool escalate = false)
        {
            VerifyCalled = true;
            return Task.FromResult<HealthTopic?>(extracted);
        }

        public Task<Dictionary<string, string>> ClassifyTopicNamesAsync(IReadOnlyList<TopicClassifyInput> topics, CancellationToken ct = default) =>
            Task.FromResult(new Dictionary<string, string>());

        public Task<Dictionary<string, string>> ClassifyTopicCategoriesAsync(IReadOnlyList<TopicCategoryInput> topics, CancellationToken ct = default) =>
            Task.FromResult(CategoryResult);

        public Task<BroaderNameResult> CompareBroaderNameAsync(string candidate, string existing, CancellationToken ct = default) =>
            Task.FromResult(new BroaderNameResult(existing, false));

        public Task<Dictionary<string, string>> MatchOriginalNamesAsync(IReadOnlyList<string> normalizedNames, IReadOnlyList<string> candidateNames, CancellationToken ct = default) =>
            Task.FromResult(new Dictionary<string, string>());

        public bool ShouldProcessTopicType(string? topicType) => true;

        public IReadOnlySet<string> GetValidCategories() => new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Nervous System", "Digestive System"
        };
    }

    private sealed class FakeQueryRepo : ITopicQueryRepository
    {
        public List<HealthTopic> NeedingRefresh { get; init; } = [];
        public List<HealthTopic> AllTopics { get; init; } = [];
        public List<HealthTopic> NeedingIcd11 { get; init; } = [];

        public Task<List<HealthTopic>> GetTopicsNeedingRefreshAsync(DateTime cutoff, CancellationToken ct) => Task.FromResult(NeedingRefresh);
        public Task<List<HealthTopic>> GetTopicsNeedingReprocessingAsync(CancellationToken ct) =>
            Task.FromResult(AllTopics.Where(t => t.NeedsLlmReprocessing).ToList());
        public Task<List<HealthTopic>> GetTopicsNeedingCategoryAsync(IReadOnlyCollection<string> validCategories, CancellationToken ct) =>
            Task.FromResult(AllTopics.Where(t => t.Category is null || !validCategories.Contains(t.Category)).ToList());
        public Task<List<HealthTopic>> GetTopicsNeedingIcd11Async(IReadOnlyCollection<string> codeableTypes, CancellationToken ct) => Task.FromResult(NeedingIcd11);
        public Task<List<HealthTopic>> GetTopicsWithEmptyStructuredFieldsAsync(CancellationToken ct) => Task.FromResult(new List<HealthTopic>());

        public Task<List<string>> GetAllTopicNamesAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetUncategorizedTopicsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<int> GetServedTopicCountAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetServedTopicBatchAsync(int skip, int take, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetUnclassifiedTopicsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<HealthTopic?> GetByNameTrackedAsync(string name, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetByNamesTrackedAsync(IEnumerable<string> names, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetTopicsWithoutOriginalNameAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<Dictionary<string, string>> GetOriginalNameMappingsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<HashSet<string>> GetAllSeenTopicNamesAsync(CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeWriteRepo : ITopicWriteRepository
    {
        public List<HealthTopic> Added { get; } = [];
        public List<HealthTopic> Removed { get; } = [];
        public List<HealthTopic> Reverted { get; } = [];
        private bool _hasChanges;

        public Task AddAsync(HealthTopic topic, CancellationToken ct)
        {
            Added.Add(topic);
            _hasChanges = true;
            return Task.CompletedTask;
        }

        public Task RemoveRangeAsync(IEnumerable<HealthTopic> topics, CancellationToken ct)
        {
            Removed.AddRange(topics);
            _hasChanges = true;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken ct)
        {
            _hasChanges = false;
            return Task.CompletedTask;
        }

        public bool HasChanges() => _hasChanges;
        public void RevertChanges(HealthTopic topic) => Reverted.Add(topic);
        public void RevertChanges(IEnumerable<HealthTopic> topics) => Reverted.AddRange(topics);
        public Task AddSeenTopicsAsync(IEnumerable<SeenTopic> topics, CancellationToken ct) => Task.CompletedTask;
        public Task<int> RetypeAsync(string name, string newType, CancellationToken ct) => Task.FromResult(0);
        public Task<int> ForceSourceRefreshAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<int> FlagForReprocessingAsync(IReadOnlyCollection<string> topicTypes, IReadOnlyCollection<string> names, CancellationToken ct) => Task.FromResult(0);
    }

    private sealed class FakeTopicRepo : ITopicRepository
    {
        public bool CacheInvalidated { get; private set; }
        public void InvalidateCache() => CacheInvalidated = true;

        public Task<IEnumerable<TopicListItem>> GetAllAsync(int skip = 0, int take = 50, string? topicType = null, string? category = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> GetCountAsync(string? topicType = null, string? category = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<HealthTopic?> GetByNameAsync(string name, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<HealthTopic?> GetByIcd11CodeAsync(string icd11Code, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<TopicListItem>> SearchAsync(string query, int skip = 0, int take = 50, string? topicType = null, string? category = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> SearchCountAsync(string query, string? topicType = null, string? category = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TopicTypeSummary>> GetDistinctTypesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TopicCategorySummary>> GetDistinctCategoriesAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IEnumerable<TopicChangeItem>> GetChangedSinceAsync(DateTime since, int skip = 0, int take = 50, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> GetChangedSinceCountAsync(DateTime since, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeIcd11CodingService : IIcd11CodingService
    {
        public Dictionary<string, Icd11Match> Matches { get; } = new();
        public List<string> LookedUpNames { get; } = new();

        public Task<Icd11Match?> LookupAsync(string topicName, CancellationToken ct = default)
        {
            LookedUpNames.Add(topicName);
            return Task.FromResult(Matches.TryGetValue(topicName, out var match) ? match : null);
        }
    }
}
