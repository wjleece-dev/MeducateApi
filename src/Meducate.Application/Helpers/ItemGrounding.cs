using System.Text.RegularExpressions;
using Meducate.Domain.Entities;
using Meducate.Domain.Services;

namespace Meducate.Application.Helpers;

internal sealed record GroundingDrop(string Field, string Item, string Reason);

// Rejected is set when the judge's verdicts were not applied at all (it rejected almost everything).
// Retained lists items that should have been removed but could not be, because that would empty a field or the
// judge looked unreliable. Either is a signal that the extraction cannot be trusted as it stands.
internal sealed record GroundingOutcome(
    IReadOnlyList<GroundingDrop> Dropped,
    string? Rejected = null,
    IReadOnlyList<GroundingDrop>? Retained = null)
{
    internal IReadOnlyList<GroundingDrop> RetainedItems => Retained ?? [];

    // True when unsupported content is still in the topic, so it should not be published or replace a stored topic.
    internal bool IsSuspect => Rejected is not null || RetainedItems.Count > 0;
}

// Removes list items the source does not back. Two independent signals must agree, because each alone is wrong
// too often to delete on (measured: the model alone was wrong about half the time, and word overlap alone cannot
// tell "women" from "female gender"):
//   1. the model says the source does not support the item, and
//   2. either fewer than half of the item's content words appear anywhere in the source, or the item says the
//      opposite of the source sentence it matches (a direction, frequency or number that differs). Word overlap
//      cannot see "decreases" vs "increases", so that case is checked directly. Negation ("should" vs "should not")
//      is not checked: wording such as "if ulcers do not heal" made it wrong far more often than right.
// A second model check on every doubted item was tried and removed: on 184 proposals it was right 10 times.
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

    // Pairs of opposite wording; a statement using one side contradicts a source that uses only the other.
    private static readonly string[][] Opposites =
    [
        ["increas", "rais", "elevat", "higher", "more", "high", "greater"],
        ["decreas", "reduc", "lower", "declin", "less", "low", "fewer"],
        ["rare", "rarely", "uncommon", "seldom", "infrequent"],
        ["common", "commonly", "frequent", "frequently", "often", "usually", "typical"],
        ["safe", "safely"],
        ["unsafe", "dangerous", "harmful", "toxic"],
    ];

    // A sentence with a negation ("not common") can express a polarity without using its word, so it is not compared.
    private static readonly HashSet<string> Negations = new(StringComparer.Ordinal)
    {
        "not", "no", "never", "without", "avoid", "cannot", "cant", "unable", "nor", "non",
    };

    [GeneratedRegex("[^a-z0-9 ]")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex("(ing|ed|es|s|ly|ity|e)$")]
    private static partial Regex Suffix();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthetical();

    [GeneratedRegex(@"(?<=[.!?])\s+|\s---\s|\n+")]
    private static partial Regex SentenceBreak();

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
        List<SentenceInfo>? sentences = null;
        var lists = new (string Field, List<string>? Items)[]
        {
            ("observations", topic.Observations), ("factors", topic.Factors), ("actions", topic.Actions),
        };

        var dropped = new List<GroundingDrop>();
        var retained = new List<GroundingDrop>();
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
                else if (Contradicts(item, sentences ??= Sentences(rawText)))
                {
                    fieldDrops.Add(new GroundingDrop(field, item, "judge: says the opposite of the source (direction, frequency or number differs)"));
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
            return new GroundingOutcome([], $"judge rejected {proposed} of {total} items; treated as unreliable", [.. dropped, .. retained]);

        return new GroundingOutcome(dropped, null, retained);
    }

    private sealed record SentenceInfo(HashSet<string> Stems, HashSet<string> Tokens, HashSet<string> Numbers, int TokenCount);

    private static List<SentenceInfo> Sentences(string rawText) =>
        [.. SentenceBreak().Split(rawText)
            .Select(s => Normalise(s).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(t => t.Length >= 3)
            .Select(t => new SentenceInfo(
                t.Where(w => w.Length > 3 && !Filler.Contains(w)).Select(w => Suffix().Replace(w, "")).ToHashSet(),
                t.ToHashSet(),
                t.Where(w => w.All(char.IsDigit)).ToHashSet(),
                t.Length))];

    // True when the item says the opposite of every source sentence that is about the same thing: a different
    // direction (increases/decreases), frequency (rare/common) or number. Needs at least two content words to
    // decide which sentence the item is about.
    private static bool Contradicts(string item, IReadOnlyList<SentenceInfo> sentences)
    {
        // Wording inside brackets is an aside ("(usually before age 45)"), not the claim, so it is ignored for direction.
        var tokens = Normalise(Parenthetical().Replace(item, " ")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var itemNumbers = Normalise(item).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.All(char.IsDigit)).ToHashSet();
        var itemGroups = Opposites.Select((g, i) => (Index: i, Present: tokens.Any(t => g.Any(p => t.StartsWith(p, StringComparison.Ordinal))))).Where(x => x.Present).Select(x => x.Index).ToList();
        if (itemNumbers.Count == 0 && itemGroups.Count == 0)
            return false;

        var content = tokens
            .Where(w => w.Length > 3 && !Filler.Contains(w) && !Negations.Contains(w) && !Opposites.Any(g => g.Any(p => w.StartsWith(p, StringComparison.Ordinal))))
            .Select(w => Suffix().Replace(w, ""))
            .Distinct().ToList();
        if (content.Count < 2)
            return false;

        var need = (int)Math.Ceiling(content.Count * 0.7);
        var about = sentences.Where(s => content.Count(c => s.Stems.Contains(c)) >= need).ToList();
        return about.Count > 0 && about.All(s => Conflicts(s, itemNumbers, itemGroups));
    }

    private static bool Conflicts(SentenceInfo sentence, HashSet<string> itemNumbers, List<int> itemGroups)
    {
        foreach (var g in itemGroups)
        {
            var mine = Opposites[g];
            var theirs = Opposites[g % 2 == 0 ? g + 1 : g - 1];
            var sentenceHasMine = sentence.Tokens.Any(t => mine.Any(p => t.StartsWith(p, StringComparison.Ordinal)));
            var sentenceHasTheirs = sentence.Tokens.Any(t => theirs.Any(p => t.StartsWith(p, StringComparison.Ordinal)));
            // "rare" and "not common" say the same thing, so a negation in the sentence makes the comparison unreliable.
            if (!sentenceHasMine && sentenceHasTheirs && !sentence.Tokens.Any(Negations.Contains))
                return true;
        }

        return itemNumbers.Count > 0 && sentence.Numbers.Count > 0 && !itemNumbers.Overlaps(sentence.Numbers);
    }

    // Records, for every item that stays in the topic, the passage of the source the model said backs it. A passage
    // counts as verbatim only when it is found word for word in the source; otherwise the item is marked as judged
    // and no quote is kept, so a loosely worded or invented quote is never shown as evidence.
    internal static List<ItemEvidence> BuildEvidence(HealthTopic topic, string rawText, IReadOnlyList<ItemJudgement>? judgements,
        IReadOnlyList<SourceLink>? links = null, IReadOnlyList<GroundingDrop>? excluding = null)
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

                var verdict = Supporting(judgements, field, item);
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
                else if (ClosestPassage(item, sections) is { } closest)
                {
                    var url = links?.FirstOrDefault(l => l.Title == closest.Section.LinkTitle)?.Url;
                    evidence.Add(new ItemEvidence(field, item, closest.Passage, "matched", closest.Section.Label, url));
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

    private sealed record Section(string Label, string LinkTitle, string Text, string Original);

    private sealed record Closest(string Passage, Section Section);

    // The source sentence that shares the most wording with the item, when it shares nearly all of it. Used when the
    // model's own quote could not be found: the sentence returned is real source text, found by wording alone.
    private static Closest? ClosestPassage(string item, IReadOnlyList<Section> sections)
    {
        var words = Words(Parenthetical().Replace(item, " ")).Distinct().ToList();
        if (words.Count == 0)
            return null;

        Closest? best = null;
        var bestScore = 0.0;
        var bestLength = int.MaxValue;
        foreach (var section in sections)
        {
            foreach (var sentence in SentenceBreak().Split(section.Original))
            {
                var passage = CleanPassage(sentence);
                if (passage.Length < 12)
                    continue;

                var stems = Words(passage).ToHashSet();
                var score = (double)words.Count(stems.Contains) / words.Count;
                if (score < 0.75 || (words.Count > 1 && words.Count(stems.Contains) < 2))
                    continue;

                if (score > bestScore || (score == bestScore && passage.Length < bestLength))
                {
                    best = new Closest(passage.Length > 300 ? passage[..300].TrimEnd() + "..." : passage, section);
                    bestScore = score;
                    bestLength = passage.Length;
                }
            }
        }

        return best;
    }

    // Drops the bracketed headings and separators the raw source uses between pages.
    private static string CleanPassage(string sentence) =>
        Whitespace().Replace(Regex.Replace(sentence, @"\[[^\]]*\]|---", " "), " ").Trim();

    // Gives entries that have no passage ("judged") their closest source sentence, when there is one.
    internal static (List<ItemEvidence> Evidence, int Changed) UpgradeJudged(IReadOnlyList<ItemEvidence> evidence, string rawText, IReadOnlyList<SourceLink>? links)
    {
        var sections = SplitSections(rawText);
        var result = new List<ItemEvidence>(evidence.Count);
        var changed = 0;
        foreach (var e in evidence)
        {
            if (e.Match == "judged" && ClosestPassage(e.Item, sections) is { } closest)
            {
                var url = links?.FirstOrDefault(l => l.Title == closest.Section.LinkTitle)?.Url;
                result.Add(new ItemEvidence(e.Field, e.Item, closest.Passage, "matched", closest.Section.Label, url));
                changed++;
            }
            else
            {
                result.Add(e);
            }
        }

        return (result, changed);
    }

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

            sections.Add(new Section(label, linkTitle, Collapse(part), part));
        }

        return sections;
    }

    [GeneratedRegex(@"(?=\[(?:Encyclopedia Article|Patient Instructions|MedlinePlus|PubMed)[^\]]*\])")]
    private static partial Regex SectionStart();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static string Collapse(string text) => Whitespace().Replace(Normalise(text), " ").Trim();

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
