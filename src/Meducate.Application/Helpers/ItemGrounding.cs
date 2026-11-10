using System.Text.RegularExpressions;
using Meducate.Domain.Entities;
using Meducate.Domain.Services;

namespace Meducate.Application.Helpers;

internal sealed record GroundingDrop(string Field, string Item, string Reason);

// Reason is set when the judge's verdicts were not applied at all.
internal sealed record GroundingOutcome(IReadOnlyList<GroundingDrop> Dropped, string? Rejected = null);

// Removes list items the source does not back. Two independent signals must agree, because each alone is wrong
// too often to delete on (measured on 216 topics: the model alone was wrong about half the time, and word overlap
// alone cannot tell "women" from "female gender"):
//   1. the model says the source does not support the item, and
//   2. fewer than half of the item's content words appear anywhere in the source.
// Anything the model did not judge stays, so a missing or garbled reply never removes content.
internal static partial class ItemGrounding
{
    private const double MinWordCoverage = 0.5;
    private const double MaxDropShare = 0.8;
    private const int MinItemsForShareGuard = 4;

    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "from", "that", "this", "these", "those", "than", "then", "such", "into", "also",
        "may", "can", "could", "should", "would", "not", "other", "more", "most", "some", "any", "all", "your", "you", "have", "has", "had",
        "are", "was", "were", "been", "its",
    };

    [GeneratedRegex("[^a-z0-9 ]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex("(ing|ed|es|s|ly|ity|e)$")]
    private static partial Regex Suffix();

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

        var sourceWords = Words(rawText).ToHashSet();
        var sourceText = Normalise(rawText);
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
                var verdict = judgements.FirstOrDefault(j => j.Field == field && !j.Supported
                    && string.Equals(j.Item, item, StringComparison.OrdinalIgnoreCase));
                if (verdict is null)
                    continue;

                proposed++;
                var coverage = Coverage(item, sourceWords, sourceText);
                if (coverage < MinWordCoverage)
                    fieldDrops.Add(new GroundingDrop(field, item, $"judge: not in source; {coverage:P0} of its words appear in the source"));
            }

            // Emptying a list would just send the topic round the reprocess loop; leave such a field for that path.
            if (fieldDrops.Count < items.Count)
                dropped.AddRange(fieldDrops);
        }

        var total = lists.Sum(l => l.Items?.Count ?? 0);
        if (total >= MinItemsForShareGuard && proposed > total * MaxDropShare)
            return new GroundingOutcome([], $"judge rejected {proposed} of {total} items; treated as unreliable");

        return new GroundingOutcome(dropped);
    }

    // Share of the item's content words found in the source (1 when it has none to check). A word also counts when
    // the source contains its first five letters, so obese/obesity and blockage/blockages are not mistaken for gaps.
    internal static double Coverage(string item, HashSet<string> sourceWords, string sourceText)
    {
        var words = Words(item).Distinct().ToList();
        return words.Count == 0 ? 1 : (double)words.Count(w => sourceWords.Contains(w) || (w.Length >= 5 && sourceText.Contains(w[..5]))) / words.Count;
    }

    // Longest run of consecutive words in `text` that also appears, in the same order, in `source`.
    // Used to spot output that follows the source wording too closely.
    internal static int LongestVerbatimRun(string text, string source)
    {
        const int gram = 8;
        var src = Normalise(source).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var words = Normalise(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var grams = new HashSet<string>();
        for (var i = 0; i + gram <= src.Length; i++)
            grams.Add(string.Join(' ', src[i..(i + gram)]));

        var best = 0;
        for (var i = 0; i + gram <= words.Length; i++)
        {
            if (!grams.Contains(string.Join(' ', words[i..(i + gram)])))
                continue;

            var j = i;
            var length = gram;
            while (j + gram < words.Length && grams.Contains(string.Join(' ', words[(j + 1)..(j + 1 + gram)])))
            {
                j++;
                length++;
            }

            best = Math.Max(best, length);
            i = j;
        }

        return best;
    }

    private static string Normalise(string text) =>
        NonAlphanumeric().Replace(System.Net.WebUtility.HtmlDecode(text).ToLowerInvariant(), " ");

    internal static IEnumerable<string> Words(string text) =>
        Normalise(text)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 3 && !Filler.Contains(w))
            .Select(w => Suffix().Replace(w, ""));

    private static List<string>? Without(List<string>? items, string field, IReadOnlyList<GroundingDrop> dropped) =>
        items?.Where(i => !dropped.Any(d => d.Field == field && string.Equals(d.Item, i, StringComparison.OrdinalIgnoreCase))).ToList();
}
