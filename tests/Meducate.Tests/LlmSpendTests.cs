using Meducate.Domain.Entities;
using Meducate.Domain.Services;
using Meducate.Infrastructure.LLM;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.SemanticKernel;
using OpenAI.Chat;

namespace Meducate.Tests;

public class LlmSpendTests
{
    [Theory]
    [InlineData("gpt-4o-mini", 1_000_000, 0, 0.15)]
    [InlineData("gpt-4o-mini-2024-07-18", 0, 1_000_000, 0.60)]
    [InlineData("gpt-4.1", 1_000_000, 1_000_000, 10.00)]
    [InlineData("gpt-4.1-mini", 1_000_000, 0, 0.40)]
    [InlineData("gpt-4o", 1_000_000, 0, 2.50)]
    [InlineData("some-future-model", 1_000_000, 1_000_000, 12.50)]
    public void Pricing_UsesTheModelsRatesAndAssumesTheDearestForUnknownModels(string model, long input, long output, double expected) =>
        Assert.Equal((decimal)expected, LlmPricing.CostUsd(model, input, output));

    [Fact]
    public async Task Tracker_RefusesCallsOnceTheDailyBudgetIsSpent()
    {
        var tracker = NewTracker(out var store, budget: "0.01");

        await tracker.EnsureWithinBudgetAsync(default);
        await tracker.RecordAsync("gpt-4.1", 1_000, 0, default); // $0.002
        await tracker.EnsureWithinBudgetAsync(default);

        await tracker.RecordAsync("gpt-4.1", 4_000, 0, default); // $0.008, total $0.010
        var ex = await Assert.ThrowsAsync<LlmBudgetExceededException>(() => tracker.EnsureWithinBudgetAsync(default));

        Assert.Equal(0.01m, ex.BudgetUsd);
        Assert.Equal(2, store.Rows.Count);
    }

    [Fact]
    public async Task Tracker_CountsSpendAlreadyRecordedBeforeARestart()
    {
        var store = new FakeStore();
        store.Rows.Add(new LlmUsage { Model = "gpt-4.1", CostUsd = 5m, At = DateTime.UtcNow });
        var tracker = new LlmSpendTracker(store, Config("2.00"), NullLogger<LlmSpendTracker>.Instance);

        await Assert.ThrowsAsync<LlmBudgetExceededException>(() => tracker.EnsureWithinBudgetAsync(default));
    }

    [Fact]
    public async Task Tracker_IgnoresYesterdaysSpend_AndSummarisesTodayByModel()
    {
        var store = new FakeStore();
        store.Rows.Add(new LlmUsage { Model = "gpt-4.1", CostUsd = 9m, At = DateTime.UtcNow.Date.AddHours(-3) });
        var tracker = new LlmSpendTracker(store, Config("2.00"), NullLogger<LlmSpendTracker>.Instance);

        await tracker.EnsureWithinBudgetAsync(default);
        await tracker.RecordAsync("gpt-4o-mini", 10_000, 1_000, default);

        var summary = await tracker.GetSummaryAsync(7, default);

        Assert.Equal(2.00m, summary.DailyBudgetUsd);
        Assert.Single(summary.TodayByModel);
        Assert.Equal("gpt-4o-mini", summary.TodayByModel[0].Model);
        Assert.Equal(LlmPricing.CostUsd("gpt-4o-mini", 10_000, 1_000), summary.SpentTodayUsd);
    }

    [Fact]
    public async Task Filter_RecordsUsageFromTheResult_AndRefusesTheCallWhenOverBudget()
    {
        var tracker = NewTracker(out var store, budget: "0.001");
        var kernel = Kernel.CreateBuilder().Build();
        kernel.FunctionInvocationFilters.Add(new SpendGuardFilter(tracker, "gpt-4o-mini"));

        var function = KernelFunctionFactory.CreateFromMethod(
            (Kernel k) => new FunctionResult(
                KernelFunctionFactory.CreateFromMethod(() => "x"),
                "answer",
                metadata: new Dictionary<string, object?> { ["Usage"] = OpenAIChatModelFactory.ChatTokenUsage(outputTokenCount: 100, inputTokenCount: 10_000, totalTokenCount: 10_100) }),
            "Fake");

        var first = await kernel.InvokeAsync(function);

        Assert.NotNull(first);
        var recorded = Assert.Single(store.Rows);
        Assert.Equal(10_000, recorded.InputTokens);
        Assert.Equal(100, recorded.OutputTokens);
        Assert.Equal("gpt-4o-mini", recorded.Model);

        // $0.0015 + $0.00006 spent, over the $0.001 budget: the next call never reaches the model.
        await Assert.ThrowsAsync<LlmBudgetExceededException>(() => kernel.InvokeAsync(function));
        Assert.Single(store.Rows);
    }

    [Fact]
    public async Task Filter_EstimatesTheSpend_WhenTheResultCarriesNoUsage()
    {
        var tracker = NewTracker(out var store, budget: "5");
        var kernel = Kernel.CreateBuilder().Build();
        kernel.FunctionInvocationFilters.Add(new SpendGuardFilter(tracker, "gpt-4o-mini"));
        var function = KernelFunctionFactory.CreateFromMethod((string rawText) => "some answer", "Plain");

        await kernel.InvokeAsync(function, new KernelArguments { ["rawText"] = new string('x', 9_000) });

        var recorded = Assert.Single(store.Rows);
        Assert.True(recorded.InputTokens >= 3_000);
        Assert.Equal("gpt-4o-mini", recorded.Model);
    }

    private static LlmSpendTracker NewTracker(out FakeStore store, string budget)
    {
        store = new FakeStore();
        return new LlmSpendTracker(store, Config(budget), NullLogger<LlmSpendTracker>.Instance);
    }

    private static IConfiguration Config(string budget) =>
        new ConfigurationBuilder().AddInMemoryCollection([new KeyValuePair<string, string?>("OpenAI:DailyBudgetUsd", budget)]).Build();

    private sealed class FakeStore : ILlmUsageStore
    {
        public List<LlmUsage> Rows { get; } = [];

        public Task<List<LlmUsage>> GetSinceAsync(DateTime sinceUtc, CancellationToken ct) =>
            Task.FromResult(Rows.Where(r => r.At >= sinceUtc).ToList());

        public Task AddAsync(LlmUsage usage, CancellationToken ct)
        {
            Rows.Add(usage);
            return Task.CompletedTask;
        }
    }
}
