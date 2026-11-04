using Meducate.Application.Services;
using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;
using Meducate.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Meducate.Tests;

public class DataIntegrityCheckServiceTests
{
    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Nervous System", "Digestive System"
    };

    [Fact]
    public void ComputeOverlapScore_ReturnsOne_WhenSummaryHasNoMeaningfulTerms()
    {
        // Every word is either a stopword or shorter than 4 characters.
        var score = DataIntegrityCheckService.ComputeOverlapScore("It is to be.", "Diabetes causes fatigue.");

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void ComputeOverlapScore_ReturnsOne_WhenAllTermsAppearInSource()
    {
        var score = DataIntegrityCheckService.ComputeOverlapScore(
            "Diabetes causes fatigue",
            "Diabetes often causes chronic fatigue issues");

        Assert.Equal(1.0, score);
    }

    [Fact]
    public void ComputeOverlapScore_ReturnsPartialRatio_WhenSomeTermsMissing()
    {
        // Terms: diabetes, causes, blindness (3). Source only contains diabetes, causes (2/3).
        var score = DataIntegrityCheckService.ComputeOverlapScore(
            "Diabetes causes blindness",
            "Diabetes causes fatigue");

        Assert.Equal(2.0 / 3.0, score, precision: 5);
    }

    [Fact]
    public async Task RunAsync_ShortCircuits_WhenNoServedTopics()
    {
        var repo = new FakeQueryRepo { ServedCount = 0 };
        var email = new FakeEmailService();
        var service = new DataIntegrityCheckService(repo, new FakeLlmProcessor(), email, new FakeConfiguration(null), NullLogger<DataIntegrityCheckService>.Instance);

        var result = await service.RunAsync(null, CancellationToken.None);

        Assert.Equal(0, result.TopicsChecked);
        Assert.False(email.AlertSent);
        Assert.False(repo.BatchRequested);
    }

    [Fact]
    public async Task RunAsync_FlagsInvalidCategory_AsFailure()
    {
        var topic = new HealthTopic { Name = "Weird Topic", Category = "Not A Real Category", TopicType = "Disease", Summary = "Some summary text here." };
        var repo = new FakeQueryRepo { ServedCount = 1, Batch = [topic] };
        var email = new FakeEmailService();
        var service = new DataIntegrityCheckService(repo, new FakeLlmProcessor(), email, new FakeConfiguration("admin@example.com"), NullLogger<DataIntegrityCheckService>.Instance);

        var result = await service.RunAsync(null, CancellationToken.None);

        Assert.Equal(1, result.Failures);
        Assert.True(email.AlertSent);
    }

    [Fact]
    public async Task RunAsync_FlagsMissingSummary_AsFailure()
    {
        var topic = new HealthTopic { Name = "No Summary Topic", Category = "Nervous System", TopicType = "Disease", Summary = null };
        var repo = new FakeQueryRepo { ServedCount = 1, Batch = [topic] };
        var service = new DataIntegrityCheckService(repo, new FakeLlmProcessor(), new FakeEmailService(), new FakeConfiguration(null), NullLogger<DataIntegrityCheckService>.Instance);

        var result = await service.RunAsync(null, CancellationToken.None);

        Assert.Equal(1, result.Failures);
    }

    [Fact]
    public async Task RunAsync_FlagsEmptyStructuredFields_AsWarningNotFailure()
    {
        var topic = new HealthTopic
        {
            Name = "Sparse Topic",
            Category = "Nervous System",
            TopicType = "Disease",
            Summary = "A perfectly fine summary.",
            Observations = [],
            Factors = [],
            Actions = []
        };
        var repo = new FakeQueryRepo { ServedCount = 1, Batch = [topic] };
        var service = new DataIntegrityCheckService(repo, new FakeLlmProcessor(), new FakeEmailService(), new FakeConfiguration(null), NullLogger<DataIntegrityCheckService>.Instance);

        var result = await service.RunAsync(null, CancellationToken.None);

        Assert.Equal(0, result.Failures);
        Assert.Equal(1, result.Warnings);
    }

    [Fact]
    public async Task RunAsync_DoesNotSendAlert_WhenNoFailures()
    {
        var topic = new HealthTopic { Name = "Fine Topic", Category = "Nervous System", TopicType = "Disease", Summary = "All good here." };
        var repo = new FakeQueryRepo { ServedCount = 1, Batch = [topic] };
        var email = new FakeEmailService();
        var service = new DataIntegrityCheckService(repo, new FakeLlmProcessor(), email, new FakeConfiguration("admin@example.com"), NullLogger<DataIntegrityCheckService>.Instance);

        await service.RunAsync(null, CancellationToken.None);

        Assert.False(email.AlertSent);
    }

    [Fact]
    public async Task RunAsync_DoesNotSendAlert_WhenAlertEmailNotConfigured()
    {
        var topic = new HealthTopic { Name = "Broken Topic", Category = "Nervous System", TopicType = null, Summary = "Some text." };
        var repo = new FakeQueryRepo { ServedCount = 1, Batch = [topic] };
        var email = new FakeEmailService();
        var service = new DataIntegrityCheckService(repo, new FakeLlmProcessor(), email, new FakeConfiguration(null), NullLogger<DataIntegrityCheckService>.Instance);

        var result = await service.RunAsync(null, CancellationToken.None);

        Assert.Equal(1, result.Failures);
        Assert.False(email.AlertSent);
    }

    [Fact]
    public async Task RunAsync_IncludesGlobalUncategorizedTopics_InFailureCount()
    {
        var needingCategory = new List<HealthTopic> { new() { Name = "Uncategorized Topic" } };
        var repo = new FakeQueryRepo { ServedCount = 0, NeedingCategory = needingCategory };
        var service = new DataIntegrityCheckService(repo, new FakeLlmProcessor(), new FakeEmailService(), new FakeConfiguration(null), NullLogger<DataIntegrityCheckService>.Instance);

        var result = await service.RunAsync(null, CancellationToken.None);

        Assert.Equal(1, result.Failures);
    }

    private sealed class FakeLlmProcessor : ILLMProcessor
    {
        public Task<HealthTopic?> ParseHealthTopicAsync(string rawText, string? topicType = null, string? discoveredName = null, CancellationToken ct = default, bool escalate = false) => throw new NotImplementedException();
        public Task<HealthTopic?> VerifyHealthTopicAsync(string rawText, HealthTopic extracted, CancellationToken ct = default, bool escalate = false) => throw new NotImplementedException();
        public Task<Dictionary<string, string>> ClassifyTopicNamesAsync(IReadOnlyList<TopicClassifyInput> topics, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Dictionary<string, string>> ClassifyTopicCategoriesAsync(IReadOnlyList<TopicCategoryInput> topics, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<BroaderNameResult> CompareBroaderNameAsync(string candidate, string existing, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Dictionary<string, string>> MatchOriginalNamesAsync(IReadOnlyList<string> normalizedNames, IReadOnlyList<string> candidateNames, CancellationToken ct = default) => throw new NotImplementedException();
        public bool ShouldProcessTopicType(string? topicType) => throw new NotImplementedException();
        public IReadOnlySet<string> GetValidCategories() => ValidCategories;
    }

    private sealed class FakeQueryRepo : ITopicQueryRepository
    {
        public int ServedCount { get; init; }
        public List<HealthTopic> Batch { get; init; } = [];
        public List<HealthTopic> NeedingCategory { get; init; } = [];
        public bool BatchRequested { get; private set; }

        public Task<List<HealthTopic>> GetTopicsNeedingCategoryAsync(IReadOnlyCollection<string> validCategories, CancellationToken ct) => Task.FromResult(NeedingCategory);
        public Task<List<HealthTopic>> GetTopicsNeedingIcd11Async(IReadOnlyCollection<string> codeableTypes, CancellationToken ct) => Task.FromResult(new List<HealthTopic>());
        public Task<int> GetServedTopicCountAsync(CancellationToken ct) => Task.FromResult(ServedCount);
        public Task<List<HealthTopic>> GetServedTopicBatchAsync(int skip, int take, CancellationToken ct)
        {
            BatchRequested = true;
            return Task.FromResult(Batch);
        }

        public Task<List<string>> GetAllTopicNamesAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetTopicsNeedingRefreshAsync(DateTime cutoff, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetTopicsNeedingReprocessingAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetUncategorizedTopicsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetUnclassifiedTopicsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<HealthTopic?> GetByNameTrackedAsync(string name, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetByNamesTrackedAsync(IEnumerable<string> names, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetTopicsWithoutOriginalNameAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<Dictionary<string, string>> GetOriginalNameMappingsAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<HashSet<string>> GetAllSeenTopicNamesAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<List<HealthTopic>> GetTopicsWithEmptyStructuredFieldsAsync(CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeEmailService : IEmailService
    {
        public bool AlertSent { get; private set; }

        public Task<EmailResult> SendDataIntegrityAlertAsync(string email, int failureCount, int warningCount, int batchChecked, int batchIndex, int totalBatches, IReadOnlyList<string> failureDetails)
        {
            AlertSent = true;
            return Task.FromResult(new EmailResult(true));
        }

        public Task<EmailResult> SendVerificationEmailAsync(string email, string verificationUrl) => throw new NotImplementedException();
        public Task<EmailResult> SendLoginEmailAsync(string email, string loginUrl) => throw new NotImplementedException();
        public Task<EmailResult> SendRateLimitWarningEmailAsync(string email, string keyName, int currentUsage, int dailyLimit) => throw new NotImplementedException();
        public Task<EmailResult> SendBackupAsync(string toEmail, string fileName, byte[] content, string summary) => throw new NotImplementedException();
        public Task<EmailResult> SendWaitlistNotificationAsync(string submittedEmail) => throw new NotImplementedException();
    }

    private sealed class FakeConfiguration(string? alertEmail) : IConfiguration
    {
        public string? this[string key]
        {
            get => key == "Admin:AlertEmail" ? alertEmail : null;
            set => throw new NotImplementedException();
        }

        public IEnumerable<IConfigurationSection> GetChildren() => throw new NotImplementedException();
        public IChangeToken GetReloadToken() => throw new NotImplementedException();
        public IConfigurationSection GetSection(string key) => throw new NotImplementedException();
    }
}
