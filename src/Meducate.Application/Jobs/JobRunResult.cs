namespace Meducate.Application.Jobs;

internal sealed record JobRunResult(
    DateTimeOffset RanAt,
    long DurationMs,
    int BatchIndex,
    int TotalBatches,
    int TopicsChecked,
    int Failures,
    int Warnings,
    IReadOnlyList<string> FailureDetails);
