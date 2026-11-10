using Meducate.Domain.Entities;
using Meducate.Domain.Services;

namespace Meducate.Application.Helpers;

internal static class TopicHelpers
{
    // Cap per-provider text to avoid blowing LLM token limits
    internal const int MaxCharsPerSource = 15_000;
    internal const int MinSummaryLength = 80;

    internal static string BuildMergedRawSource(IEnumerable<RawTopicData> sources)
    {
        var parts = new List<string>();

        // Prepend group names as context if available
        var groupNames = sources
            .Where(s => s.Groups is not null)
            .SelectMany(s => s.Groups!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (groupNames.Count > 0)
            parts.Add($"[MedlinePlus Groups: {string.Join(", ", groupNames)}]");

        foreach (var s in sources)
        {
            var text = s.RawText;
            if (text.Length > MaxCharsPerSource)
            {
                // Truncate at last space before the limit to avoid cutting mid-word
                var cutoff = text.LastIndexOf(' ', MaxCharsPerSource);
                text = cutoff > 0 ? text[..cutoff] : text[..MaxCharsPerSource];
            }
            parts.Add($"[{s.SourceName}]\n{text}");
        }

        return string.Join("\n---\n", parts);
    }

    // Fields the extraction prompt itself documents as "otherwise []" for a given type --
    // i.e. an empty array there is a valid, expected answer, not a sign the LLM failed.
    // Keeping this in sync with GetTypeInstructions in SemanticKernelLLMProcessor.
    private static readonly HashSet<string> TypesWhereEmptyObservationsIsValid = new(StringComparer.OrdinalIgnoreCase)
    {
        "Procedure", "Diagnostic Test", "Vaccine", "Nutrient"
    };

    private static readonly HashSet<string> TypesWhereEmptyFactorsIsValid = new(StringComparer.OrdinalIgnoreCase)
    {
        "Procedure", "Diagnostic Test", "Anatomy"
    };

    private static readonly HashSet<string> TypesWhereEmptyActionsIsValid = new(StringComparer.OrdinalIgnoreCase)
    {
        "Anatomy"
    };

    internal static string? CheckTopicQuality(HealthTopic topic)
    {
        if (string.IsNullOrWhiteSpace(topic.Summary) || topic.Summary.Length < MinSummaryLength)
            return $"summary too short ({topic.Summary?.Length ?? 0} chars, minimum {MinSummaryLength})";

        // Reject if the summary is just the name restated
        if (topic.Summary.Trim().Equals(topic.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            return "summary just restates the topic name";

        if (topic.Observations is not { Count: > 0 } && !TypesWhereEmptyObservationsIsValid.Contains(topic.TopicType ?? ""))
            return "no observations populated";

        if (topic.Factors is not { Count: > 0 } && !TypesWhereEmptyFactorsIsValid.Contains(topic.TopicType ?? ""))
            return "no factors populated";

        if (topic.Actions is not { Count: > 0 } && !TypesWhereEmptyActionsIsValid.Contains(topic.TopicType ?? ""))
            return "no actions populated";

        if (TextCleaner.HasMetaSentence(topic.Summary))
            return "summary talks about 'the source/text'";

        if (topic.TopicType is "Procedure" or "Diagnostic Test"
            && topic.Actions?.FirstOrDefault(IsAftercareItem) is { } aftercare)
            return $"actions contain aftercare ('{aftercare}')";

        return null;
    }

    // Procedure/test "actions" are what it treats or tests for. Recovery steps, follow-ups and
    // self-care instructions belong to the discharge pages and must not land there.
    private static readonly System.Text.RegularExpressions.Regex AftercarePattern = new(
        @"^(take|follow|avoid|wear|apply|applying|keep|do not|don't|learn|learning|arrang|rest\b|return|use of|wash|stop|talk to|call|ask|eat|drink)|\bfollow[- ]?up\b|\brecovery\b|\bpain (medic|manage|relief)\b|\bduring healing\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private const int MaxCopiedWords = 20;
    private const int MergedBulletCommas = 6;

    // A re-extraction that would leave a stored topic worse off. Such a result is discarded rather than served, and
    // the stored version stays live. It is worse when it:
    //   - fails the quality check the stored one passes,
    //   - empties a list, or cuts a list of 4+ items to half or less,
    //   - has more comma-packed "bullets" (several items squashed into one) than the stored one, or
    //   - follows the source wording for 20+ words in a row where the stored one does not.
    internal static bool IsRegression(HealthTopic stored, HealthTopic fresh, string? rawSource = null) =>
        (CheckTopicQuality(fresh) is not null && CheckTopicQuality(stored) is null)
        || Shrunk(stored.Observations, fresh.Observations)
        || Shrunk(stored.Factors, fresh.Factors)
        || Shrunk(stored.Actions, fresh.Actions)
        || TotalShrunk(stored, fresh)
        || MergedBullets(fresh) > MergedBullets(stored)
        || (rawSource is { Length: >= 100 } && CopiedWords(fresh, rawSource) >= MaxCopiedWords && CopiedWords(stored, rawSource) < MaxCopiedWords);

    // A list that lost half its items, or 3+ items and at least 30% of them, is treated as lost content.
    private static bool Shrunk(List<string>? stored, List<string>? fresh)
    {
        var before = stored?.Count ?? 0;
        var after = fresh?.Count ?? 0;
        return before > 0 && (after == 0 || (before >= 4 && (after * 2 <= before || (before - after >= 3 && after * 10 <= before * 7))));
    }

    // Across all three lists: losing 5+ items and a quarter of the total, even if no single list is cut hard.
    private static bool TotalShrunk(HealthTopic stored, HealthTopic fresh)
    {
        var before = (stored.Observations?.Count ?? 0) + (stored.Factors?.Count ?? 0) + (stored.Actions?.Count ?? 0);
        var after = (fresh.Observations?.Count ?? 0) + (fresh.Factors?.Count ?? 0) + (fresh.Actions?.Count ?? 0);
        return before - after >= 5 && after * 4 <= before * 3;
    }

    private static int MergedBullets(HealthTopic t) =>
        new[] { t.Observations, t.Factors, t.Actions }
            .SelectMany(l => l ?? [])
            .Count(i => i.Count(c => c == ',') >= MergedBulletCommas);

    internal static int CopiedWords(HealthTopic t, string rawSource) =>
        new[] { t.Summary }.Concat(t.Observations ?? []).Concat(t.Factors ?? []).Concat(t.Actions ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => ItemGrounding.LongestVerbatimRun(s!, rawSource))
            .DefaultIfEmpty(0).Max();

    // Quality issues the verifier can repair (content problems), as opposed to an empty or missing extraction.
    internal static bool IsVerifiable(string? qualityIssue) =>
        qualityIssue is null || qualityIssue.StartsWith("actions contain aftercare", StringComparison.Ordinal)
        || qualityIssue.StartsWith("summary talks about", StringComparison.Ordinal);

    internal static bool IsAftercareItem(string item) => AftercarePattern.IsMatch(item.Trim());

    internal static string GetSeenStatus(string topicType) => topicType switch
    {
        "Non-Medical" => "NonMedical",
        "Other" => "Unclassifiable",
        _ => "Accepted"
    };

    internal static string ToTitleCase(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
            words[i] = ProcessWord(words[i]);
        return string.Join(' ', words);

        static string ProcessWord(string word)
        {
            if (word.Length >= 2 && word.All(c => !char.IsLetter(c) || char.IsUpper(c)))
                return word;

            if (word.Contains('(') || word.Contains(')'))
            {
                var result = new System.Text.StringBuilder();
                var currentPart = new System.Text.StringBuilder();

                foreach (var ch in word)
                {
                    if (ch is '(' or ')')
                    {
                        if (currentPart.Length > 0)
                        {
                            result.Append(ProcessSimpleWord(currentPart.ToString()));
                            currentPart.Clear();
                        }
                        result.Append(ch);
                    }
                    else
                    {
                        currentPart.Append(ch);
                    }
                }

                if (currentPart.Length > 0)
                    result.Append(ProcessSimpleWord(currentPart.ToString()));

                return result.ToString();
            }

            if (word.Contains('/'))
            {
                var parts = word.Split('/');
                for (var j = 0; j < parts.Length; j++)
                    parts[j] = ProcessWord(parts[j]);
                return string.Join('/', parts);
            }

            if (word.Contains('-'))
            {
                var parts = word.Split('-');
                for (var j = 0; j < parts.Length; j++)
                    parts[j] = ProcessSimpleWord(parts[j]);
                return string.Join('-', parts);
            }

            return ProcessSimpleWord(word);
        }

        static string ProcessSimpleWord(string word)
        {
            if (string.IsNullOrEmpty(word))
                return word;

            if (word.Length >= 2 && word.All(c => !char.IsLetter(c) || char.IsUpper(c)))
                return word;

            // Mixed-case terms such as PrEP or mRNA are deliberate; do not flatten them.
            if (word.Length >= 3 && char.IsLetter(word[0]) && word.Skip(1).Any(char.IsUpper) && word.Any(char.IsLower) && !word.Contains('-') && !word.Contains('\''))
                return word;

            if (word.EndsWith("'s", StringComparison.OrdinalIgnoreCase))
            {
                var basePart = word[..^2];
                if (basePart.Length > 0)
                    return char.ToUpperInvariant(basePart[0]) + basePart[1..].ToLowerInvariant() + "'s";
            }

            return char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
        }
    }
}
