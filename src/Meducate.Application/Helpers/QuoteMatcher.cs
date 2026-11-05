using System.Text.RegularExpressions;

namespace Meducate.Application.Helpers;

// A model that says "this item is supported by the source" must show where. The quote it gives is checked here
// against the source text, so an approval counts only when the words really are there. Matching ignores case,
// punctuation and spacing, and tolerates a small amount of drift (a dropped article, a reordered clause) because
// models rarely copy more than a sentence perfectly.
internal static partial class QuoteMatcher
{
    private const double RequiredShare = 0.8;
    private const int WindowSlack = 4;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    internal static string[] Tokens(string text) =>
        NonAlphanumeric().Replace(System.Net.WebUtility.HtmlDecode(text).ToLowerInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    internal static bool Supports(string[] sourceTokens, string? quote)
    {
        if (string.IsNullOrWhiteSpace(quote))
            return false;

        var q = Tokens(quote);
        if (q.Length < 3)
            return false;

        var window = q.Length + WindowSlack;
        for (var start = 0; start < sourceTokens.Length; start++)
        {
            // Anchor on either of the quote's first two words, so one dropped leading word does not lose the match.
            if (sourceTokens[start] != q[0] && sourceTokens[start] != q[1])
                continue;

            var end = Math.Min(sourceTokens.Length, start + window);
            var cursor = start;
            var found = 0;
            foreach (var word in q)
            {
                var hit = Array.IndexOf(sourceTokens, word, cursor, end - cursor);
                if (hit < 0)
                    continue;

                found++;
                cursor = hit + 1;
            }

            if ((double)found / q.Length >= RequiredShare)
                return true;
        }

        return false;
    }
}
