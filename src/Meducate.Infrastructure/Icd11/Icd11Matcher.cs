using System.Text.Json;
using System.Text.RegularExpressions;
using Meducate.Domain.Services;

namespace Meducate.Infrastructure.Icd11;

// Picks the WHO search result that actually is the topic, or none. The search ranks by relevance, not identity:
// "Anxiety Disorder" ranks "Secondary anxiety syndrome" first, "Heart Diseases" ranks a myocardium code, "Gas"
// ranks granuloma annulare. A wrong diagnostic code is worse than no code, so a result is only accepted when its
// title or one of WHO's own listed synonyms equals the topic name, or its title is the topic name plus only
// residual wording ("..., unspecified").
internal static partial class Icd11Matcher
{
    private static readonly HashSet<string> GenericWords =
        ["disease", "disorder", "infection", "syndrome", "problem", "condition"];

    private static readonly HashSet<string> FillerWords =
    [
        "unspecified", "other", "specified", "not", "elsewhere", "classified", "without", "mention", "whether", "or",
        "of", "the", "and", "nos", "a", "an", "at", "for", "due", "to", "by", "in", "with",
        "malignant", "neoplasm", "site", "type", "stage", "cause", "unknown", "level",
    ];

    private static readonly Dictionary<string, string[]> Equivalents = new() { ["cancer"] = ["cancer", "neoplasm", "malignant", "carcinoma"] };

    [GeneratedRegex("<.*?>")]
    private static partial Regex HtmlTags();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    internal static Icd11Match? Select(string topicName, JsonElement entities)
    {
        var name = Normalize(topicName);
        var nameWords = name.Split(' ');
        var candidates = new List<Candidate>();

        foreach (var entity in entities.EnumerateArray())
        {
            var code = entity.TryGetProperty("theCode", out var codeEl) && codeEl.ValueKind == JsonValueKind.String ? codeEl.GetString() : null;
            // Chapters and blocks have no code; "&" marks a stem+extension cluster, which is not a topic-level code.
            if (string.IsNullOrWhiteSpace(code) || code.Contains('&'))
                continue;

            var rawTitle = entity.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
            var title = rawTitle is null ? null : HtmlTags().Replace(rawTitle, "").Trim();
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var synonyms = new List<string>();
            if (entity.TryGetProperty("matchingPVs", out var pvs) && pvs.ValueKind == JsonValueKind.Array)
            {
                foreach (var pv in pvs.EnumerateArray())
                {
                    if (pv.TryGetProperty("label", out var label) && label.GetString() is { } text)
                        synonyms.Add(Normalize(HtmlTags().Replace(text, "")));
                }
            }

            candidates.Add(new Candidate(code, title, synonyms));
        }

        // 1. The title is the topic.
        var exact = candidates.FirstOrDefault(c => Normalize(c.Title) == name);
        if (exact is not null)
            return new Icd11Match(exact.Code, exact.Title);

        // 2. WHO lists the topic name as a synonym of one of the strongest results.
        var bySynonym = candidates.Take(5).FirstOrDefault(c => c.Synonyms.Any(s => StripNos(s) == name));
        if (bySynonym is not null)
            return new Icd11Match(bySynonym.Code, bySynonym.Title);

        // 3. The title is the topic name plus residual wording only.
        var core = nameWords.Where(w => !GenericWords.Contains(w)).ToList();
        if (core.Count == 0)
            return null;

        var residual = candidates.Take(5).FirstOrDefault(c =>
        {
            var words = Normalize(c.Title).Split(' ');
            return core.All(w => (Equivalents.GetValueOrDefault(w) ?? [w]).Any(words.Contains))
                && words.All(w => nameWords.Contains(w) || FillerWords.Contains(w) || GenericWords.Contains(w)
                    || core.Any(k => (Equivalents.GetValueOrDefault(k) ?? []).Contains(w)));
        });

        return residual is null ? null : new Icd11Match(residual.Code, residual.Title);
    }

    // Lower case, American spelling, no possessives or punctuation, naive singular, so that
    // "Alzheimer's Diseases" equals "Alzheimer disease" and "Leukemia" equals "leukaemia".
    internal static string Normalize(string text)
    {
        var spelled = text.ToLowerInvariant().Replace('’', '\'').Replace("'s", "")
            .Replace("ae", "e").Replace("oe", "e").Replace("colour", "color");

        var words = NonAlphanumeric().Replace(spelled, " ").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length > 3 && w.EndsWith("ies", StringComparison.Ordinal) ? w[..^3] + "y"
                : w.Length > 3 && w.EndsWith('s') && !w.EndsWith("ss", StringComparison.Ordinal)
                    && !w.EndsWith("us", StringComparison.Ordinal) && !w.EndsWith("is", StringComparison.Ordinal) ? w[..^1] : w);

        return string.Join(' ', words);
    }

    private static string StripNos(string synonym) =>
        synonym.EndsWith(" nos", StringComparison.Ordinal) ? synonym[..^4].Trim() : synonym;

    private sealed record Candidate(string Code, string Title, List<string> Synonyms);
}
