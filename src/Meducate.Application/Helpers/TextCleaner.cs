using System.Net;
using System.Text.RegularExpressions;

namespace Meducate.Application.Helpers;

// Deterministic clean-up for LLM output, so served text never carries HTML entities, stray
// whitespace, copied page footers or sentences about "the source text" regardless of model variance.
internal static partial class TextCleaner
{
    internal const int MaxTags = 8;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?<=[.!?])\s+(?=[A-Z0-9""'(])")]
    private static partial Regex SentenceBoundary();

    // "...not detailed in the text", "the source does not state...", "as the provided information says"
    [GeneratedRegex(@"\b(not|no|n't|without)\b[^.!?]*\b(in|by|from|within) the (source|text|provided (text|source|information))\b|\bthe (source|text|provided (text|source|information)) (does not|doesn't|did not|states|says|mentions|provides|lacks)\b", RegexOptions.IgnoreCase)]
    private static partial Regex MetaSentence();

    [GeneratedRegex(@"\[Source:\s*([^\]]+)\]")]
    private static partial Regex SourceTag();

    internal static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Decode twice: the source sometimes double-encodes (&amp;#39;).
        var decoded = WebUtility.HtmlDecode(WebUtility.HtmlDecode(text));
        return Whitespace().Replace(decoded.Replace(' ', ' '), " ").Trim();
    }

    internal static string CleanItem(string item)
    {
        var text = Clean(item);
        // List items are phrases, not sentences: drop a lone trailing full stop.
        return text.EndsWith('.') && text.IndexOf(". ", StringComparison.Ordinal) < 0 ? text[..^1].TrimEnd() : text;
    }

    internal static string CleanSummary(string summary)
    {
        var sentences = SentenceBoundary().Split(Clean(summary))
            .Where(s => !MetaSentence().IsMatch(s))
            .ToList();

        // A trailing fragment with no terminal punctuation is a copied page footer
        // (e.g. "NIH: National Cancer Institute"); drop it if real sentences precede it.
        if (sentences.Count > 1 && !EndsSentence(sentences[^1]))
            sentences.RemoveAt(sentences.Count - 1);

        var result = string.Join(' ', sentences).Trim();
        return result.Length > 0 && !EndsSentence(result) ? result + "." : result;
    }

    internal static bool HasMetaSentence(string summary) => MetaSentence().IsMatch(summary);

    internal static List<string> CleanTags(IEnumerable<string> tags) =>
        [.. tags.Select(t => Clean(t).ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxTags)];

    // The "[Source: National Heart, Lung, and Blood Institute]" tag the provider writes into the raw text.
    internal static string? SourceOrganisation(string? rawText)
    {
        var match = rawText is null ? null : SourceTag().Match(rawText);
        return match is { Success: true } ? Clean(match.Groups[1].Value) : null;
    }

    // Applies the same clean-up the extraction pipeline now does to a stored topic.
    // Returns true when anything changed. Idempotent.
    internal static bool SanitizeTopic(Meducate.Domain.Entities.HealthTopic topic)
    {
        var changed = false;

        void Set(ref string value, string cleaned)
        {
            if (!string.Equals(value, cleaned, StringComparison.Ordinal)) { value = cleaned; changed = true; }
        }

        void SetList(ref List<string>? value, List<string> cleaned)
        {
            if (value is null || !value.SequenceEqual(cleaned, StringComparer.Ordinal)) { value = cleaned; changed = true; }
        }

        var summary = topic.Summary ?? string.Empty;
        Set(ref summary, CleanSummary(summary));
        topic.Summary = summary;

        static List<string> Items(IEnumerable<string>? items) =>
            [.. (items ?? []).Select(CleanItem).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

        var observations = topic.Observations; SetList(ref observations, Items(topic.Observations)); topic.Observations = observations;
        var factors = topic.Factors; SetList(ref factors, Items(topic.Factors)); topic.Factors = factors;
        var actions = topic.Actions; SetList(ref actions, Items(topic.Actions)); topic.Actions = actions;
        var tags = topic.Tags; SetList(ref tags, CleanTags(topic.Tags ?? [])); topic.Tags = tags;

        var citations = Items(topic.Citations);
        if (citations.Count == 0 && SourceOrganisation(topic.RawSource) is { } organisation)
            citations.Add(organisation);
        var current = topic.Citations; SetList(ref current, citations); topic.Citations = current;

        return changed;
    }

    private static bool EndsSentence(string s) => s.TrimEnd().EndsWithAny('.', '!', '?', ')', '"');

    private static bool EndsWithAny(this string s, params char[] chars) => s.Length > 0 && chars.Contains(s[^1]);
}
