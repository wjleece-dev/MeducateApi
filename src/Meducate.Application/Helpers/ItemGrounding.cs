using Meducate.Domain.Entities;
using Meducate.Domain.Services;

namespace Meducate.Application.Helpers;

internal sealed record GroundingDrop(string Field, string Item, string Reason);

// Reason is set when the judge's verdicts were not applied at all.
internal sealed record GroundingOutcome(IReadOnlyList<GroundingDrop> Dropped, string? Rejected = null);

// Turns the model's per-item verdicts into removals. An item stays unless the model said the source does not back it,
// or said it does but cannot point to words in the source that show it (QuoteMatcher). Anything the model did not
// judge stays: the check must never remove content because of a missing or garbled reply.
internal static class ItemGrounding
{
    private const double MaxDropShare = 0.8;
    private const int MinItemsForShareGuard = 4;

    internal static GroundingOutcome Apply(HealthTopic topic, string rawText, IReadOnlyList<ItemJudgement>? judgements)
    {
        var outcome = Evaluate(topic, rawText, judgements);
        if (outcome.Dropped.Count == 0)
            return outcome;

        topic.Observations = Without(topic.Observations, "observations", outcome.Dropped);
        topic.Factors = Without(topic.Factors, "factors", outcome.Dropped);
        topic.Actions = Without(topic.Actions, "actions", outcome.Dropped);
        return outcome;
    }

    // Same decision without changing the topic (used for dry runs over stored topics).
    internal static GroundingOutcome Evaluate(HealthTopic topic, string rawText, IReadOnlyList<ItemJudgement>? judgements)
    {
        if (judgements is not { Count: > 0 })
            return new GroundingOutcome([]);

        var source = QuoteMatcher.Tokens(rawText);
        var lists = new (string Field, List<string>? Items)[]
        {
            ("observations", topic.Observations), ("factors", topic.Factors), ("actions", topic.Actions),
        };

        var dropped = new List<GroundingDrop>();
        var proposed = 0;
        foreach (var (field, items) in lists)
        {
            if (items is not { Count: > 0 })
                continue;

            var fieldDrops = new List<GroundingDrop>();
            foreach (var item in items)
            {
                var verdict = judgements.FirstOrDefault(j => j.Field == field && string.Equals(j.Item, item, StringComparison.OrdinalIgnoreCase));
                if (verdict is null)
                    continue;

                if (!verdict.Supported)
                    fieldDrops.Add(new GroundingDrop(field, item, "judge: not in source"));
                else if (!QuoteMatcher.Supports(source, verdict.Quote))
                    fieldDrops.Add(new GroundingDrop(field, item, $"judge approved but quote not found in source: \"{verdict.Quote}\""));
            }

            proposed += fieldDrops.Count;

            // Emptying a list would just send the topic round the reprocess loop; leave such a field for that path.
            if (fieldDrops.Count < items.Count)
                dropped.AddRange(fieldDrops);
        }

        var total = lists.Sum(l => l.Items?.Count ?? 0);
        if (total >= MinItemsForShareGuard && proposed > total * MaxDropShare)
            return new GroundingOutcome([], $"judge rejected {proposed} of {total} items; treated as unreliable");

        return new GroundingOutcome(dropped);
    }

    private static List<string>? Without(List<string>? items, string field, IReadOnlyList<GroundingDrop> dropped) =>
        items?.Where(i => !dropped.Any(d => d.Field == field && string.Equals(d.Item, i, StringComparison.OrdinalIgnoreCase))).ToList();
}
