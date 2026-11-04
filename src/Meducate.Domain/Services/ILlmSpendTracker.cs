using Meducate.Domain.Entities;

namespace Meducate.Domain.Services;

internal interface ILlmUsageStore
{
    Task<List<LlmUsage>> GetSinceAsync(DateTime sinceUtc, CancellationToken ct);
    Task AddAsync(LlmUsage usage, CancellationToken ct);
}

internal interface ILlmSpendTracker
{
    decimal DailyBudgetUsd { get; }

    // Throws LlmBudgetExceededException once today's recorded spend has reached the daily budget.
    Task EnsureWithinBudgetAsync(CancellationToken ct);

    Task RecordAsync(string model, int inputTokens, int outputTokens, CancellationToken ct);

    Task<LlmSpendSummary> GetSummaryAsync(int days, CancellationToken ct);
}

internal sealed class LlmBudgetExceededException(decimal spentUsd, decimal budgetUsd)
    : Exception($"Daily LLM budget reached: ${spentUsd:F2} of ${budgetUsd:F2} spent today.")
{
    public decimal SpentUsd { get; } = spentUsd;
    public decimal BudgetUsd { get; } = budgetUsd;
}

internal sealed record LlmModelSpend(string Model, int Calls, long InputTokens, long OutputTokens, decimal CostUsd);

internal sealed record LlmDaySpend(DateOnly Day, int Calls, decimal CostUsd);

internal sealed record LlmSpendSummary(
    decimal DailyBudgetUsd,
    decimal SpentTodayUsd,
    IReadOnlyList<LlmModelSpend> TodayByModel,
    IReadOnlyList<LlmDaySpend> Days);
