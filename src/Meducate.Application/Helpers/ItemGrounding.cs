using System.Text.RegularExpressions;
using Meducate.Domain.Entities;
using Meducate.Domain.Services;

namespace Meducate.Application.Helpers;

internal sealed record GroundingDrop(string Field, string Item, string Reason);

// Rejected is set when the judge's verdicts were not applied at all (it rejected almost everything).
// Recheck lists items the judge doubts but whose words appear in the source: they may contradict it (negation,
// direction, frequency, a number) or be general knowledge that merely shares words with it, so they get a second,
// stricter look. Retained lists items that should have been
// removed but could not be, because that would empty a field or the judge looked unreliable. Both are signals that
// the extraction cannot be trusted as it stands.
internal sealed record GroundingOutcome(
    IReadOnlyList<GroundingDrop> Dropped,
    string? Rejected = null,
    IReadOnlyList<ItemRef>? Recheck = null,
    IReadOnlyList<GroundingDrop>? Retained = null)
{
    internal IReadOnlyList<ItemRef> RecheckItems => Recheck ?? [];
    internal IReadOnlyList<GroundingDrop> RetainedItems => Retained ?? [];

    // True when unsupported content is still in the topic, so it should not be published or replace a stored topic.
    internal bool IsSuspect => Rejected is not null || RetainedItems.Count > 0;
}

// Removes list items the source does not back. Independent signals must agree, because each alone is wrong
// too often to delete on (measured on 216 topics: the model alone was wrong about half the time, and word overlap
// alone cannot tell "women" from "female gender"):
//   1. the model says the source does not support the item, and
//   2. either fewer than half of the item's content words appear anywhere in the source, or a second, stricter
//      model check also says the source does not state it. Word overlap cannot see "decreases" vs "increases", or
//      that "Age" is not a stated risk factor just because the word "age" occurs somewhere in the source.
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

    // Words that can flip the meaning of a statement while leaving almost every other word unchanged.
    private static readonly HashSet<string> QualifierWords = new(StringComparer.Ordinal)
    {
        "not", "no", "never", "without", "avoid", "cannot", "cant", "unable", "nor", "non", "except", "unless", "only",
        "rare", "rarely", "common", "commonly", "frequent", "frequently", "often", "always", "usually", "occasionally",
        "sometimes", "mild", "mildly", "moderate", "severe", "severely", "high", "higher", "low", "lower", "more", "less",
        "most", "least", "all", "none", "every", "both", "either", "should", "must", "may", "might", "can",
    };

    private static readonly string[] QualifierPrefixes =
    [
        "increas", "decreas", "reduc", "rais", "lower", "elevat", "declin", "worsen", "improv", "prevent", "cause", "avoid",
    ];

    [GeneratedRegex("[^a-z0-9 ]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex("(ing|ed|es|s|ly|ity|e)$")]
    private static partial Regex Suffix();

    internal static GroundingOutcome Apply(HealthTopic topic, string rawText, IReadOnlyList<ItemJudgement>? judgements,
        IReadOnlyList<ItemJudgement>? recheck = null)
    {
        var outcome = Evaluate(topic, rawText, judgements, recheck);
        if (outcome.Dropped.Count == 0)
            return outcome;

        topic.Observations = Without(topic.Observations, "observations", outcome.Dropped);
        topic.Factors = Without(topic.Factors, "factors", outcome.Dropped);
        topic.Actions = Without(topic.Actions, "actions", outcome.Dropped);
        return outcome;
    }

    // Same decision without changing the topic (used for dry runs over stored topics). Call it first without
    // `recheck` to learn which items need the stricter check, then again with that check's verdicts.
    internal static GroundingOutcome Evaluate(HealthTopic topic, string rawText, IReadOnlyList<ItemJudgement>? judgements,
        IReadOnlyList<ItemJudgement>? recheck = null)
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
        var retained = new List<GroundingDrop>();
        var pending = new List<ItemRef>();
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
                {
                    fieldDrops.Add(new GroundingDrop(field, item, $"judge: not in source; {coverage:P0} of its words appear in the source"));
                }
                else
                {
                    // Its words appear in the source, which is not proof the source says it ("Age" and "Polyp size" pass a
                    // word check, and so does "decreases blood pressure" against "increases blood pressure"). Only a second,
                    // stricter model check agreeing with the first removes it.
                    var second = recheck?.FirstOrDefault(j => j.Field == field && !j.Supported
                        && string.Equals(j.Item, item, StringComparison.OrdinalIgnoreCase));
                    if (second is not null)
                        fieldDrops.Add(new GroundingDrop(field, item, HasQualifier(item)
                            ? "judge and strict check: the source does not state it exactly (negation, direction, frequency, severity or number differs)"
                            : "judge and strict check: the source does not state it"));
                    else if (recheck is null)
                        pending.Add(new ItemRef(field, item));
                }
            }

            // Emptying a list would just send the topic round the reprocess loop, so such items are kept, but
            // reported as retained so the caller does not treat the topic as clean.
            if (fieldDrops.Count < items.Count)
                dropped.AddRange(fieldDrops);
            else
                retained.AddRange(fieldDrops);
        }

        var total = lists.Sum(l => l.Items?.Count ?? 0);
        if (total >= MinItemsForShareGuard && proposed > total * MaxDropShare)
            return new GroundingOutcome([], $"judge rejected {proposed} of {total} items; treated as unreliable", pending, [.. dropped, .. retained]);

        return new GroundingOutcome(dropped, null, pending, retained);
    }

    // Records, for every item that stays in the topic, the passage of the source the model said backs it. A passage
    // counts as verbatim only when it is found word for word in the source; otherwise the item is marked as judged
    // and no quote is kept, so a loosely worded or invented quote is never shown as evidence.
    internal static List<ItemEvidence> BuildEvidence(HealthTopic topic, string rawText, IReadOnlyList<ItemJudgement>? judgements,
        IReadOnlyList<ItemJudgement>? strict = null, IReadOnlyList<SourceLink>? links = null, IReadOnlyList<GroundingDrop>? excluding = null)
    {
        var evidence = new List<ItemEvidence>();
        if (judgements is not { Count: > 0 } || string.IsNullOrWhiteSpace(rawText))
            return evidence;

        var sections = SplitSections(rawText);
        var whole = Collapse(rawText);
        var lists = new (string Field, List<string>? Items)[]
        {
            ("observations", topic.Observations), ("factors", topic.Factors), ("actions", topic.Actions),
        };

        foreach (var (field, items) in lists)
        {
            foreach (var item in items ?? [])
            {
                if (excluding?.Any(d => d.Field == field && string.Equals(d.Item, item, StringComparison.OrdinalIgnoreCase)) == true)
                    continue;

                var verdict = Supporting(judgements, field, item) ?? Supporting(strict, field, item);
                if (verdict is null)
                    continue;

                var quote = verdict.Quote?.Trim();
                var squashed = quote is null ? "" : Collapse(quote);
                if (squashed.Length >= 12 && whole.Contains(squashed, StringComparison.Ordinal))
                {
                    var section = sections.FirstOrDefault(s => s.Text.Contains(squashed, StringComparison.Ordinal));
                    var url = section is null ? null : links?.FirstOrDefault(l => l.Title == section.LinkTitle)?.Url;
                    evidence.Add(new ItemEvidence(field, item, quote!.Length > 300 ? quote[..300] : quote, "verbatim", section?.Label, url));
                }
                else
                {
                    evidence.Add(new ItemEvidence(field, item, null, "judged"));
                }
            }
        }

        return evidence;
    }

    // The parts of the source that are not public-domain NLM text: the A.D.A.M. encyclopedia articles and patient
    // instructions. Copying a long run of NLM's own summary is fine; copying these closely is what we avoid.
    internal static string CopyrightedText(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return "";

        return string.Join("\n", SectionStart().Split(rawText)
            .Where(p => p.StartsWith("[Encyclopedia Article", StringComparison.Ordinal) || p.StartsWith("[Patient Instructions", StringComparison.Ordinal)));
    }

    private static ItemJudgement? Supporting(IReadOnlyList<ItemJudgement>? judgements, string field, string item) =>
        judgements?.FirstOrDefault(j => j.Supported && j.Field == field && string.Equals(j.Item, item, StringComparison.OrdinalIgnoreCase));

    private sealed record Section(string Label, string LinkTitle, string Text);

    // The raw source is several pages joined together, each starting with a bracketed heading.
    private static List<Section> SplitSections(string rawText)
    {
        var sections = new List<Section>();
        foreach (var part in SectionStart().Split(rawText))
        {
            if (string.IsNullOrWhiteSpace(part))
                continue;

            var heading = Regex.Match(part, @"^\[([^\]]*)\]").Groups[1].Value;
            string label, linkTitle;
            if (heading.StartsWith("Encyclopedia Article:", StringComparison.Ordinal))
            {
                linkTitle = heading["Encyclopedia Article:".Length..].Trim();
                label = "Encyclopedia article: " + linkTitle;
            }
            else if (heading.StartsWith("Patient Instructions:", StringComparison.Ordinal))
            {
                linkTitle = heading["Patient Instructions:".Length..].Trim();
                label = "Patient instructions: " + linkTitle;
            }
            else if (heading.StartsWith("PubMed", StringComparison.Ordinal))
            {
                linkTitle = "PubMed";
                label = "PubMed";
            }
            else
            {
                linkTitle = "MedlinePlus topic page";
                label = "MedlinePlus topic summary";
            }

            sections.Add(new Section(label, linkTitle, Collapse(part)));
        }

        return sections;
    }

    [GeneratedRegex(@"(?=\[(?:Encyclopedia Article|Patient Instructions|MedlinePlus|PubMed)[^\]]*\])")]
    private static partial Regex SectionStart();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string Collapse(string text) => Whitespace().Replace(Normalise(text), " ").Trim();

    // True when the item carries a word that can reverse its meaning without changing the words around it.
    internal static bool HasQualifier(string item)
    {
        if (item.Any(char.IsDigit))
            return true;

        return Normalise(item)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(w => QualifierWords.Contains(w) || QualifierPrefixes.Any(p => w.StartsWith(p, StringComparison.Ordinal)));
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
