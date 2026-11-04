using Meducate.Application.Services;
using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;
using Meducate.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meducate.Tests;

public class TopicIngestionServiceTests
{
    private static TopicIngestionService BuildService(
        IEnumerable<IMedicalDataProvider> providers,
        FakeQueryRepo queryRepo,
        FakeWriteRepo writeRepo,
        FakeLlmProcessor llmProcessor,
        FakeTopicRepo? topicRepo = null,
        FakeIcd11CodingService? icd11Service = null)
    {
        var backfill = new TopicBackfillService(queryRepo, writeRepo, llmProcessor, icd11Service ?? new FakeIcd11CodingService(), NullLogger<TopicBackfillService>.Instance);
        return new TopicIngestionService(providers, queryRepo, writeRepo, llmProcessor, topicRepo ?? new FakeTopicRepo(), backfill, NullLogger<TopicIngestionService>.Instance);
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
    public async Task IngestAsync_DefaultsToOther_WhenClassifyThrows_AndSkipsAdding()
    {
        var provider = new FakeProvider("TestSource", [new RawTopicData("New Condition", new string('x', 200), "TestSource")]);
        var queryRepo = new FakeQueryRepo();
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { ThrowOnClassify = true };
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        Assert.Empty(writeRepo.Added);
    }

    [Fact]
    public async Task IngestAsync_SkipsNonMedicalTopics()
    {
        var provider = new FakeProvider("TestSource", [new RawTopicData("Earthquake", new string('x', 200), "TestSource")]);
        var queryRepo = new FakeQueryRepo();
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { ClassifyResult = new() { ["Earthquake"] = "Non-Medical" } };
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        Assert.Empty(writeRepo.Added);
    }

    [Fact]
    public async Task IngestAsync_SkipsFilteredTopicTypes()
    {
        var provider = new FakeProvider("TestSource", [new RawTopicData("Heart", new string('x', 200), "TestSource")]);
        var queryRepo = new FakeQueryRepo();
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { ClassifyResult = new() { ["Heart"] = "Anatomy" } };
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        Assert.Empty(writeRepo.Added);
    }

    [Fact]
    public async Task IngestAsync_SkipsWhenMergedSourceTooShort()
    {
        var provider = new FakeProvider("TestSource", [new RawTopicData("New Condition", "short", "TestSource")]);
        var queryRepo = new FakeQueryRepo();
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor { ClassifyResult = new() { ["New Condition"] = "Disease" } };
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        Assert.Empty(writeRepo.Added);
    }

    [Fact]
    public async Task IngestAsync_AddsNewTopic_OnHappyPath()
    {
        var provider = new FakeProvider("TestSource", [new RawTopicData("New Condition", new string('x', 200), "TestSource")]);
        var queryRepo = new FakeQueryRepo();
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor
        {
            ClassifyResult = new() { ["New Condition"] = "Disease" },
            ParseResult = GoodQualityTopic("New Condition")
        };
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        var added = Assert.Single(writeRepo.Added);
        Assert.Equal("New Condition", added.Name);
        Assert.True(llm.VerifyCalled);
        Assert.Single(writeRepo.SeenTopicsAdded, s => s.Name == "New Condition");
    }

    [Fact]
    public async Task IngestAsync_SkipsVerify_WhenQualityAlreadyLow()
    {
        var lowQuality = GoodQualityTopic("New Condition");
        lowQuality.Observations = [];

        var provider = new FakeProvider("TestSource", [new RawTopicData("New Condition", new string('x', 200), "TestSource")]);
        var queryRepo = new FakeQueryRepo();
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor
        {
            ClassifyResult = new() { ["New Condition"] = "Disease" },
            ParseResult = lowQuality
        };
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        Assert.False(llm.VerifyCalled);
        var added = Assert.Single(writeRepo.Added);
        Assert.True(added.NeedsLlmReprocessing);
    }

    [Fact]
    public async Task IngestAsync_SkipsTopic_WhenLlmExtractionReturnsNull()
    {
        var provider = new FakeProvider("TestSource", [new RawTopicData("New Condition", new string('x', 200), "TestSource")]);
        var queryRepo = new FakeQueryRepo();
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor
        {
            ClassifyResult = new() { ["New Condition"] = "Disease" },
            ParseResult = null
        };
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        Assert.Empty(writeRepo.Added);
    }

    [Fact]
    public async Task IngestAsync_SynonymCollision_RenamesExisting_WhenLlmSaysReplace()
    {
        var existing = new HealthTopic { Name = "Hypertension", RawSource = "old data" };
        var provider = new FakeProvider("TestSource", [new RawTopicData("High Blood Pressure", new string('x', 200), "TestSource")]);
        var queryRepo = new FakeQueryRepo { ExistingNames = ["Hypertension"] };
        queryRepo.TopicsByName["Hypertension"] = existing;
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor
        {
            ClassifyResult = new() { ["High Blood Pressure"] = "Disease" },
            ParseResult = GoodQualityTopic("Hypertension"),
            CompareBroaderNameResult = new BroaderNameResult("High Blood Pressure", true)
        };
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        Assert.Equal("High Blood Pressure", existing.Name);
        Assert.True(existing.NeedsLlmReprocessing);
        Assert.Empty(writeRepo.Added);
    }

    [Fact]
    public async Task IngestAsync_SynonymCollision_KeepsAsSeparateTopic_WhenLlmSaysDifferentSubject()
    {
        var existing = new HealthTopic { Name = "Bronchitis", RawSource = "old data" };
        var provider = new FakeProvider("TestSource", [new RawTopicData("Acute Bronchitis", new string('x', 200), "TestSource")]);
        var queryRepo = new FakeQueryRepo { ExistingNames = ["Bronchitis"] };
        queryRepo.TopicsByName["Bronchitis"] = existing;
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor
        {
            ClassifyResult = new() { ["Acute Bronchitis"] = "Disease" },
            ParseResult = GoodQualityTopic("Bronchitis"),
            // "different" subject: LLM returns the candidate name itself with ShouldReplace = false
            CompareBroaderNameResult = new BroaderNameResult("Acute Bronchitis", false)
        };
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        // The existing entity is untouched — the new topic is kept under its own
        // originally-discovered name instead of being merged or dropped.
        Assert.Equal("old data", existing.RawSource);
        Assert.False(existing.NeedsLlmReprocessing);
        var added = Assert.Single(writeRepo.Added);
        Assert.Equal("Acute Bronchitis", added.Name);
    }

    [Fact]
    public async Task IngestAsync_SynonymCollision_MergesSourceIntoExisting()
    {
        var existing = new HealthTopic { Name = "Bronchitis", RawSource = "old data" };
        var provider = new FakeProvider("TestSource", [new RawTopicData("Chest Cold", new string('x', 200), "TestSource")]);
        var queryRepo = new FakeQueryRepo { ExistingNames = ["Bronchitis"] };
        queryRepo.TopicsByName["Bronchitis"] = existing;
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor
        {
            ClassifyResult = new() { ["Chest Cold"] = "Disease" },
            ParseResult = GoodQualityTopic("Bronchitis"),
            // Same subject, existing name preferred — not equal to candidate, ShouldReplace = false
            CompareBroaderNameResult = new BroaderNameResult("Bronchitis", false)
        };
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        Assert.Contains("old data", existing.RawSource);
        Assert.True(existing.NeedsLlmReprocessing);
        Assert.Empty(writeRepo.Added);
    }

    [Fact]
    public async Task IngestAsync_RemovesStaleTopics_PastGracePeriod()
    {
        var staleTopic = new HealthTopic { Name = "Old Topic", LastSourceRefresh = DateTime.UtcNow.AddDays(-10) };
        var provider = new FakeProvider("TestSource", [], knownNames: new HashSet<string> { "Some Other Topic" });
        var queryRepo = new FakeQueryRepo { ExistingNames = ["Old Topic"], AllTrackedTopics = [staleTopic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor();
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        Assert.Contains(staleTopic, writeRepo.Removed);
    }

    [Fact]
    public async Task IngestAsync_KeepsStaleTopics_WithinGracePeriod()
    {
        var recentTopic = new HealthTopic { Name = "Old Topic", LastSourceRefresh = DateTime.UtcNow.AddDays(-2) };
        var provider = new FakeProvider("TestSource", [], knownNames: new HashSet<string> { "Some Other Topic" });
        var queryRepo = new FakeQueryRepo { ExistingNames = ["Old Topic"], AllTrackedTopics = [recentTopic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor();
        var service = BuildService([provider], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        Assert.DoesNotContain(recentTopic, writeRepo.Removed);
    }

    [Fact]
    public async Task IngestAsync_SwallowsProviderException_AndStillProcessesOthers()
    {
        var throwing = new ThrowingProvider("BrokenSource");
        var working = new FakeProvider("WorkingSource", [new RawTopicData("New Condition", new string('x', 200), "WorkingSource")]);
        var queryRepo = new FakeQueryRepo();
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor
        {
            ClassifyResult = new() { ["New Condition"] = "Disease" },
            ParseResult = GoodQualityTopic("New Condition")
        };
        var service = BuildService([throwing, working], queryRepo, writeRepo, llm);

        await service.IngestAsync();

        var added = Assert.Single(writeRepo.Added);
        Assert.Equal("New Condition", added.Name);
    }

    [Fact]
    public async Task IngestAsync_AssignsIcd11Code_ForMatchedDiagnosableTopic()
    {
        var topic = new HealthTopic { Name = "Asthma", TopicType = "Disease", Category = "Respiratory System" };
        var queryRepo = new FakeQueryRepo { NeedingIcd11 = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor();
        var icd11 = new FakeIcd11CodingService();
        icd11.Matches["Asthma"] = new Icd11Match("CA23", "Asthma");
        var service = BuildService([], queryRepo, writeRepo, llm, icd11Service: icd11);

        await service.IngestAsync();

        Assert.Equal("CA23", topic.Icd11Code);
        Assert.Equal("Asthma", topic.Icd11Title);
    }

    [Fact]
    public async Task IngestAsync_LeavesIcd11CodeNull_WhenNoConfidentMatch()
    {
        var topic = new HealthTopic { Name = "Made Up Condition", TopicType = "Disease", Category = "Symptoms & Signs" };
        var queryRepo = new FakeQueryRepo { NeedingIcd11 = [topic] };
        var writeRepo = new FakeWriteRepo();
        var llm = new FakeLlmProcessor();
        var icd11 = new FakeIcd11CodingService();
        var service = BuildService([], queryRepo, writeRepo, llm, icd11Service: icd11);

        await service.IngestAsync();

        Assert.Null(topic.Icd11Code);
        Assert.Contains("Made Up Condition", icd11.LookedUpNames);
    }

    private sealed class FakeProvider(string sourceName, IReadOnlyList<RawTopicData> discoveries, IReadOnlySet<string>? knownNames = null) : IMedicalDataProvider
    {
        public string SourceName => sourceName;
        public Task<RawTopicData?> FetchTopicDataAsync(string topicName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RawTopicData>> DiscoverTopicsAsync(IReadOnlySet<string> existingNames, CancellationToken ct = default) => Task.FromResult(discoveries);
        public Task<IReadOnlySet<string>> GetKnownTopicNamesAsync(CancellationToken ct = default) =>
            Task.FromResult(knownNames ?? new HashSet<string>(discoveries.Select(d => d.TopicName), StringComparer.OrdinalIgnoreCase));
    }

    private sealed class ThrowingProvider(string sourceName) : IMedicalDataProvider
    {
        public string SourceName => sourceName;
        public Task<RawTopicData?> FetchTopicDataAsync(string topicName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RawTopicData>> DiscoverTopicsAsync(IReadOnlySet<string> existingNames, CancellationToken ct = default) => throw new InvalidOperationException("provider down");
        public Task<IReadOnlySet<string>> GetKnownTopicNamesAsync(CancellationToken ct = default) => throw new InvalidOperationException("provider down");
    }

    private sealed class FakeLlmProcessor : ILLMProcessor
    {
        private static readonly HashSet<string> FilteredTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "Non-Medical", "Other", "Anatomy", "Drug", "Procedure", "Diagnostic Test", "Vaccine", "Nutrient", "Lifestyle"
        };

        public bool ThrowOnClassify { get; set; }
        public Dictionary<string, string> ClassifyResult { get; set; } = [];
        public HealthTopic? ParseResult { get; set; }
        public bool VerifyCalled { get; private set; }
        public BroaderNameResult CompareBroaderNameResult { get; set; } = new("different", false);

        public Task<Dictionary<string, string>> ClassifyTopicNamesAsync(IReadOnlyList<TopicClassifyInput> topics, CancellationToken ct = default) =>
            ThrowOnClassify ? throw new InvalidOperationException("LLM unavailable") : Task.FromResult(ClassifyResult);

        public Task<HealthTopic?> ParseHealthTopicAsync(string rawText, string? topicType = null, string? discoveredName = null, CancellationToken ct = default, bool escalate = false) =>
            Task.FromResult(ParseResult);

        public Task<HealthTopic?> VerifyHealthTopicAsync(string rawText, HealthTopic extracted, CancellationToken ct = default, bool escalate = false)
        {
            VerifyCalled = true;
            return Task.FromResult<HealthTopic?>(extracted);
        }

        public Task<BroaderNameResult> CompareBroaderNameAsync(string candidate, string existing, CancellationToken ct = default) =>
            Task.FromResult(CompareBroaderNameResult);

        public Task<Dictionary<string, string>> ClassifyTopicCategoriesAsync(IReadOnlyList<TopicCategoryInput> topics, CancellationToken ct = default) =>
            Task.FromResult(new Dictionary<string, string>());

        public Task<Dictionary<string, string>> MatchOriginalNamesAsync(IReadOnlyList<string> normalizedNames, IReadOnlyList<string> candidateNames, CancellationToken ct = default) =>
            Task.FromResult(new Dictionary<string, string>());

        public bool ShouldProcessTopicType(string? topicType) =>
            string.IsNullOrWhiteSpace(topicType) || !FilteredTypes.Contains(topicType);

        public IReadOnlySet<string> GetValidCategories() => new HashSet<string>();
    }

    private sealed class FakeQueryRepo : ITopicQueryRepository
    {
        public List<string> ExistingNames { get; init; } = [];
        public HashSet<string> SeenNames { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HealthTopic> TopicsByName { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> OriginalNameMap { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public List<HealthTopic> AllTrackedTopics { get; init; } = [];
        public List<HealthTopic> NeedingIcd11 { get; init; } = [];

        public Task<HashSet<string>> GetAllSeenTopicNamesAsync(CancellationToken ct) => Task.FromResult(SeenNames);
        public Task<List<string>> GetAllTopicNamesAsync(CancellationToken ct) => Task.FromResult(ExistingNames);
        public Task<HealthTopic?> GetByNameTrackedAsync(string name, CancellationToken ct) => Task.FromResult(TopicsByName.GetValueOrDefault(name));
        public Task<List<HealthTopic>> GetByNamesTrackedAsync(IEnumerable<string> names, CancellationToken ct) =>
            Task.FromResult(AllTrackedTopics.Where(t => names.Contains(t.Name, StringComparer.OrdinalIgnoreCase)).ToList());
        public Task<Dictionary<string, string>> GetOriginalNameMappingsAsync(CancellationToken ct) => Task.FromResult(OriginalNameMap);

        // Backfill/data-integrity plumbing not under test here — empty defaults so those steps no-op.
        public Task<List<HealthTopic>> GetTopicsNeedingRefreshAsync(DateTime cutoff, CancellationToken ct) => Task.FromResult(new List<HealthTopic>());
        public Task<List<HealthTopic>> GetTopicsNeedingReprocessingAsync(CancellationToken ct) => Task.FromResult(new List<HealthTopic>());
        public Task<List<HealthTopic>> GetUncategorizedTopicsAsync(CancellationToken ct) => Task.FromResult(new List<HealthTopic>());
        public Task<List<HealthTopic>> GetTopicsNeedingCategoryAsync(IReadOnlyCollection<string> validCategories, CancellationToken ct) => Task.FromResult(new List<HealthTopic>());
        public Task<List<HealthTopic>> GetTopicsNeedingIcd11Async(IReadOnlyCollection<string> codeableTypes, CancellationToken ct) => Task.FromResult(NeedingIcd11);
        public Task<int> GetServedTopicCountAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<List<HealthTopic>> GetServedTopicBatchAsync(int skip, int take, CancellationToken ct) => Task.FromResult(new List<HealthTopic>());
        public Task<List<HealthTopic>> GetUnclassifiedTopicsAsync(CancellationToken ct) => Task.FromResult(new List<HealthTopic>());
        public Task<List<HealthTopic>> GetTopicsWithoutOriginalNameAsync(CancellationToken ct) => Task.FromResult(new List<HealthTopic>());
        public Task<List<HealthTopic>> GetTopicsWithEmptyStructuredFieldsAsync(CancellationToken ct) => Task.FromResult(new List<HealthTopic>());
    }

    private sealed class FakeWriteRepo : ITopicWriteRepository
    {
        public List<HealthTopic> Added { get; } = [];
        public List<HealthTopic> Removed { get; } = [];
        public List<SeenTopic> SeenTopicsAdded { get; } = [];
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

        public Task<int> RenameAndRetypeAsync(string from, string to, string newType, CancellationToken ct) => Task.FromResult(0);
        public Task<int> RetypeAsync(string name, string newType, CancellationToken ct) => Task.FromResult(0);
        public Task<int> ClearIcd11CodesAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<int> SanitizeAllAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<int> ForceSourceRefreshAsync(CancellationToken ct) => Task.FromResult(0);
        public Task<int> FlagForReprocessingAsync(IReadOnlyCollection<string> topicTypes, IReadOnlyCollection<string> names, CancellationToken ct) => Task.FromResult(0);

        public Task AddSeenTopicsAsync(IEnumerable<SeenTopic> topics, CancellationToken ct)
        {
            SeenTopicsAdded.AddRange(topics);
            return Task.CompletedTask;
        }
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
