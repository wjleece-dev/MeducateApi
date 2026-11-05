using System.Text.RegularExpressions;

namespace Meducate.Application.Helpers;

// A model that says "this item is supported by the source" must show where. The quote it gives is checked here
// against the source text, so an approval counts only when the words really are there. Matching ignores case,
// punctuation and spacing, and tolerates a small amount of drift (a dropped article, a reordered clause).
//
// Models often stitch a lead-in from elsewhere onto the item ("Symptoms may include: Palpitations ..."), so the quote
// is checked in fragments (split at colons, semicolons, ellipses and full stops). A fragment counts only if it is in
// the source AND shares a content word with the item, so a generic lead-in cannot vouch for an invented item.
internal static partial class QuoteMatcher
{
    private const double RequiredShare = 0.8;
    private const int WindowSlack = 4;
    private const int StemLength = 5;

    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "that", "this", "from", "are", "was", "can", "may", "also", "other", "such", "some", "any", "not",
    };

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"[:;\n…]|\.\.\.|\.\s")]
    private static partial Regex FragmentBreak();

    internal static string[] Tokens(string text) =>
        NonAlphanumeric().Replace(System.Net.WebUtility.HtmlDecode(text).ToLowerInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    internal static bool Supports(string[] sourceTokens, string? quote, string? item = null)
    {
        if (string.IsNullOrWhiteSpace(quote))
            return false;

        var itemStems = item is null ? null : Stems(Tokens(item));
        foreach (var fragment in new[] { quote }.Concat(FragmentBreak().Split(quote)))
        {
            var q = Tokens(fragment);
            if (q.Length == 0 || !InSource(sourceTokens, q))
                continue;

            if (itemStems is null || itemStems.Count == 0 || Stems(q).Overlaps(itemStems))
                return true;
        }

        return false;
    }

    private static HashSet<string> Stems(string[] tokens) =>
        [.. tokens.Where(t => t.Length > 2 && !Filler.Contains(t)).Select(t => t.Length > StemLength ? t[..StemLength] : t)];

    private static bool InSource(string[] source, string[] q)
    {
        // One or two words prove little unless they sit together exactly.
        if (q.Length < 3)
            return IndexOfRun(source, q) >= 0;

        var window = q.Length + WindowSlack;
        for (var start = 0; start < source.Length; start++)
        {
            // Anchor on either of the quote's first two words, so one dropped leading word does not lose the match.
            if (source[start] != q[0] && source[start] != q[1])
                continue;

            var end = Math.Min(source.Length, start + window);
            var cursor = start;
            var found = 0;
            foreach (var word in q)
            {
                var hit = Array.IndexOf(source, word, cursor, end - cursor);
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

    private static int IndexOfRun(string[] source, string[] run)
    {
        for (var i = 0; i + run.Length <= source.Length; i++)
        {
            var j = 0;
            while (j < run.Length && source[i + j] == run[j])
                j++;
            if (j == run.Length)
                return i;
        }

        return -1;
    }
}
