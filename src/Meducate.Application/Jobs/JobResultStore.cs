using System.Collections.Concurrent;

namespace Meducate.Application.Jobs;

internal sealed class JobResultStore
{
    private readonly ConcurrentDictionary<string, JobRunResult> _results = new(StringComparer.OrdinalIgnoreCase);

    internal void Set(string jobName, JobRunResult result) =>
        _results[jobName] = result;

    internal JobRunResult? Get(string jobName) =>
        _results.TryGetValue(jobName, out var result) ? result : null;
}
