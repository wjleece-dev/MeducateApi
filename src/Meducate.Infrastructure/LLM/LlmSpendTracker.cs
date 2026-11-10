using System.Globalization;
using Meducate.Domain.Entities;
using Meducate.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Meducate.Infrastructure.LLM;

internal sealed class LlmSpendTracker(ILlmUsageStore store, IConfiguration config, ILogger<LlmSpendTracker> logger) : ILlmSpendTracker
{
    private const decimal DefaultDailyBudgetUsd = 2.00m;

    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateOnly _day;
    private decimal _spentToday;
    private bool _budgetWarned;

    public decimal DailyBudgetUsd { get; } =
        decimal.TryParse(config["OpenAI:DailyBudgetUsd"], NumberStyles.Number, CultureInfo.InvariantCulture, out var configured) && configured > 0
            ? configured
            : DefaultDailyBudgetUsd;

    public async Task EnsureWithinBudgetAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await LoadTodayAsync(ct);

            if (_spentToday < DailyBudgetUsd)
                return;

            if (!_budgetWarned)
            {
                _budgetWarned = true;
                logger.LogWarning("LLM daily budget reached (${Spent:F2} of ${Budget:F2}); further model calls are refused until tomorrow (UTC)",
                    _spentToday, DailyBudgetUsd);
            }

            throw new LlmBudgetExceededException(_spentToday, DailyBudgetUsd);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecordAsync(string model, int inputTokens, int outputTokens, CancellationToken ct)
    {
        var cost = LlmPricing.CostUsd(model, inputTokens, outputTokens);

        await _lock.WaitAsync(CancellationToken.None);
        try
        {
            await LoadTodayAsync(ct);
            _spentToday += cost;
        }
        finally
        {
            _lock.Release();
        }

        await store.AddAsync(new LlmUsage { Model = model, InputTokens = inputTokens, OutputTokens = outputTokens, CostUsd = cost }, CancellationToken.None);
    }

    public async Task<LlmSpendSummary> GetSummaryAsync(int days, CancellationToken ct)
    {
        var since = DateTime.UtcNow.Date.AddDays(-(Math.Max(days, 1) - 1));
        var rows = await store.GetSinceAsync(since, ct);
        var today = DateTime.UtcNow.Date;

        var todayRows = rows.Where(r => r.At >= today).ToList();
        return new LlmSpendSummary(
            DailyBudgetUsd,
            todayRows.Sum(r => r.CostUsd),
            [.. todayRows.GroupBy(r => r.Model)
                .Select(g => new LlmModelSpend(g.Key, g.Count(), g.Sum(r => (long)r.InputTokens), g.Sum(r => (long)r.OutputTokens), g.Sum(r => r.CostUsd)))
                .OrderByDescending(m => m.CostUsd)],
            [.. rows.GroupBy(r => DateOnly.FromDateTime(r.At))
                .Select(g => new LlmDaySpend(g.Key, g.Count(), g.Sum(r => r.CostUsd)))
                .OrderByDescending(d => d.Day)]);
    }

    // The day's total is read from the store once per UTC day (and after a restart), then kept in memory.
    private async Task LoadTodayAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_day == today)
            return;

        var rows = await store.GetSinceAsync(DateTime.UtcNow.Date, ct);
        _spentToday = rows.Sum(r => r.CostUsd);
        _day = today;
        _budgetWarned = false;
    }
}
