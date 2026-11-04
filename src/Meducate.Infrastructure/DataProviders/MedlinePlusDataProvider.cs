using System.Text.RegularExpressions;
using System.Xml.Linq;
using Meducate.Application.Helpers;
using Meducate.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Meducate.Infrastructure.DataProviders;

internal sealed partial class MedlinePlusDataProvider(HttpClient httpClient, ILogger<MedlinePlusDataProvider> logger) : IMedicalDataProvider
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<MedlinePlusDataProvider> _logger = logger;

    private List<ParsedTopic>? _cachedTopics;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    // Portal page — increased to capture treatment sections that appear late on the page
    private const int MaxPortalPageChars = 7_000;

    // Each encyclopedia article — two may be fetched, so budget is split
    private const int MaxEncyclopediaChars = 6_000;

    // Throttle between fetches to avoid rate-limiting MedlinePlus
    private static readonly TimeSpan PageFetchDelay = TimeSpan.FromMilliseconds(300);

    internal sealed record ParsedTopic(
        string Title,
        List<string> AlsoCalled,
        string Summary,
        List<string> Groups,
        string? Url,
        string? PrimaryInstitute,
        List<string> EncyclopediaUrls,
        Dictionary<string, string>? EncyclopediaTitles = null);

    public string SourceName => "MedlinePlus";

    public async Task<RawTopicData?> FetchTopicDataAsync(string topicName, CancellationToken ct = default)
    {
        try
        {
            var topics = await GetOrLoadTopicsAsync(ct);
            var match = topics.FirstOrDefault(t =>
                string.Equals(t.Title, topicName, StringComparison.OrdinalIgnoreCase)
                || t.AlsoCalled.Any(a => string.Equals(a, topicName, StringComparison.OrdinalIgnoreCase)));

            if (match is null)
                return null;

            // Fetch encyclopedia first — if available, skip the portal page entirely.
            // Portal pages are mostly navigation chrome and link lists; the XML summary
            // is cleaner and the encyclopedia covers the clinical detail.
            var encyclopediaText = await FetchFirstEncyclopediaArticleAsync(match.EncyclopediaUrls, match.EncyclopediaTitles, ct);
            string? portalText = null;
            if (string.IsNullOrWhiteSpace(encyclopediaText))
                portalText = await FetchPageTextAsync(match.Url, MaxPortalPageChars, ct);
            var rawText = BuildRawText(match, portalText, encyclopediaText);

            return new RawTopicData(topicName, rawText, SourceName, match.Groups, ContentHasher.ComputeHash(rawText));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MedlinePlus: failed to fetch data for {Topic}", topicName);
            return null;
        }
    }

    public async Task<IReadOnlyList<RawTopicData>> DiscoverTopicsAsync(IReadOnlySet<string> existingNames, CancellationToken ct = default)
    {
        try
        {
            var topics = await GetOrLoadTopicsAsync(ct);

            var newTopics = topics
                .Where(t => !existingNames.Contains(t.Title))
                .Select(t => new RawTopicData(t.Title, t.Summary, SourceName, t.Groups, ContentHasher.ComputeHash(t.Summary)))
                .ToList();

            _logger.LogInformation("MedlinePlus: {Total} health topics, {New} are new",
                topics.Count, newTopics.Count);

            return newTopics;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MedlinePlus: discovery failed");
            return [];
        }
    }

    public async Task<IReadOnlySet<string>> GetKnownTopicNamesAsync(CancellationToken ct = default)
    {
        try
        {
            var topics = await GetOrLoadTopicsAsync(ct);
            var names = topics.Select(t => t.Title)
                .Concat(topics.SelectMany(t => t.AlsoCalled));
            return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MedlinePlus: failed to get known topic names");
            return new HashSet<string>();
        }
    }

    private async Task<List<ParsedTopic>> GetOrLoadTopicsAsync(CancellationToken ct)
    {
        if (_cachedTopics is not null)
            return _cachedTopics;

        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cachedTopics is not null)
                return _cachedTopics;

            return await LoadTopicsAsync(ct);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    private async Task<List<ParsedTopic>> LoadTopicsAsync(CancellationToken ct)
    {
        var xmlUrl = await ResolveXmlUrlAsync(ct);
        _logger.LogInformation("MedlinePlus: downloading XML from {Url}", xmlUrl);

        var xmlContent = await _httpClient.GetStringAsync(xmlUrl, ct);
        var topics = ParseTopicsXml(xmlContent);

        _logger.LogInformation("MedlinePlus: parsed {Count} health topics from XML", topics.Count);
        _cachedTopics = topics;
        return topics;
    }

    internal static List<ParsedTopic> ParseTopicsXml(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);

        var topics = new List<ParsedTopic>();

        foreach (var topic in doc.Descendants("health-topic"))
        {
            var language = topic.Attribute("language")?.Value;
            if (!string.Equals(language, "English", StringComparison.OrdinalIgnoreCase))
                continue;

            var title = topic.Attribute("title")?.Value?.Trim();
            var summary = topic.Element("full-summary")?.Value?.Trim()
                          ?? topic.Attribute("meta-desc")?.Value?.Trim();

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(summary))
                continue;

            var cleanSummary = StripHtmlTags(summary);
            if (cleanSummary.Length < 50)
                continue;

            var alsoCalled = topic.Elements("also-called")
                .Select(a => a.Value?.Trim() ?? "")
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .ToList();

            var seeReferences = topic.Elements("see-reference")
                .Select(s => s.Value?.Trim() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            var groups = topic.Elements("group")
                .Select(g => g.Value?.Trim() ?? "")
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .ToList();

            var url = topic.Attribute("url")?.Value?.Trim();
            var primaryInstitute = topic.Element("primary-institute")?.Value?.Trim();

            // Collect encyclopedia article URLs from <site> elements — sort by
            // title relevance so the main condition article is preferred over
            // related tests or procedures (which appear first alphabetically).
            // Expand title words with also-called and see-reference synonyms so
            // e.g. "High blood pressure in adults - hypertension" scores higher
            // than "High blood pressure - medicine-related".
            var titleWords = title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Concat(alsoCalled.SelectMany(a => a.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                .Concat(seeReferences.SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
                .Where(w => w.Length > 2)
                .Select(w => w.ToLowerInvariant())
                .ToHashSet();

            double TitleOverlap(string siteTitle)
            {
                var words = siteTitle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var matchCount = words.Count(w => titleWords.Contains(w.ToLowerInvariant().TrimEnd('.', ',', ';', ':')));
                // Ratio rewards focused titles (e.g. "Asthma" ranks above "Allergies, asthma, and dust")
                return words.Length > 0 ? (double)matchCount / words.Length : 0;
            }

            var encyclopedia = topic.Elements("site")
                .Select(s => (
                    Url: s.Attribute("url")?.Value?.Trim() ?? "",
                    SiteTitle: s.Attribute("title")?.Value?.Trim() ?? ""))
                .Where(s =>
                    (s.Url.Contains("/ency/article/", StringComparison.OrdinalIgnoreCase)
                     || s.Url.Contains("/ency/patientinstructions/", StringComparison.OrdinalIgnoreCase))
                    && !s.Url.Contains("/spanish/ency/", StringComparison.OrdinalIgnoreCase)
                    // An article sharing no word with the topic or its synonyms is about something else
                    // (the Menstruation topic used to be summarised from the Amenorrhea article).
                    && TitleOverlap(s.SiteTitle) > 0)
                .OrderByDescending(s =>
                    // /ency/article/ entries are main condition articles; patient instructions are supplementary
                    TitleOverlap(s.SiteTitle) + (s.Url.Contains("/ency/article/", StringComparison.OrdinalIgnoreCase) ? 0.1 : 0.0))
                .DistinctBy(s => s.Url)
                .Take(2)
                .ToList();

            var encyclopediaUrls = encyclopedia.Select(s => s.Url).ToList();
            var encyclopediaTitles = encyclopedia.ToDictionary(s => s.Url, s => s.SiteTitle);

            topics.Add(new ParsedTopic(title, alsoCalled, cleanSummary, groups, url, primaryInstitute, encyclopediaUrls, encyclopediaTitles));
        }

        return topics;
    }

    private async Task<string?> FetchFirstEncyclopediaArticleAsync(List<string> urls, Dictionary<string, string>? titles, CancellationToken ct)
    {
        var parts = new List<string>();
        foreach (var url in urls)
        {
            // Clean chrome before truncating so the char budget is spent on
            // clinical content rather than navigation boilerplate.
            var text = await FetchPageTextAsync(url, MaxEncyclopediaChars, ct, CleanEncyclopediaText);
            if (!string.IsNullOrWhiteSpace(text))
            {
                // Discharge/self-care pages describe aftercare, not the procedure or condition itself;
                // labelling them lets the extraction prompt keep them out of the structured lists.
                var label = url.Contains("/ency/patientinstructions/", StringComparison.OrdinalIgnoreCase)
                    ? "Patient Instructions"
                    : "Encyclopedia Article";
                var heading = titles is not null && titles.TryGetValue(url, out var title) ? $"{label}: {title}" : label;
                parts.Add($"[{heading}]\n{text}");
            }
        }
        return parts.Count > 0 ? string.Join("\n\n---\n\n", parts) : null;
    }

    private async Task<string?> FetchPageTextAsync(string? url, int maxChars, CancellationToken ct,
        Func<string, string>? cleaner = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            await Task.Delay(PageFetchDelay, ct);
            var html = await _httpClient.GetStringAsync(url, ct);
            var text = StripHtmlTags(html);

            if (cleaner is not null)
                text = cleaner(text);

            if (text.Length > maxChars)
            {
                var cutoff = text.LastIndexOf(' ', maxChars);
                text = cutoff > 0 ? text[..cutoff] : text[..maxChars];
            }

            return text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MedlinePlus: failed to fetch page {Url}", url);
            return null;
        }
    }

    private static string CleanEncyclopediaText(string text)
    {
        // Strip the consistent navigation header on MedlinePlus encyclopedia pages.
        // Every article ends its chrome with this phrase before the clinical content begins.
        const string headerMarker = "please enable JavaScript.";
        var headerEnd = text.IndexOf(headerMarker, StringComparison.OrdinalIgnoreCase);
        if (headerEnd >= 0)
            text = text[(headerEnd + headerMarker.Length)..].TrimStart();

        // Strip the editorial footer. "Review Date" is specific to the encyclopedia
        // footer and does not appear in clinical article body text.
        var footerStart = text.IndexOf("Review Date", StringComparison.OrdinalIgnoreCase);
        if (footerStart > 0)
            text = text[..footerStart].TrimEnd();

        return text;
    }

    private static string BuildRawText(ParsedTopic topic, string? portalText, string? encyclopediaText)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(topic.PrimaryInstitute))
            parts.Add($"[Source: {topic.PrimaryInstitute}]");

        if (!string.IsNullOrWhiteSpace(encyclopediaText))
        {
            // Use the clean XML summary as a concise intro and let the encyclopedia
            // article cover the structured clinical detail. The portal page is skipped
            // when encyclopedia content is available (see FetchTopicDataAsync).
            parts.Add(topic.Summary);
            parts.Add(encyclopediaText);
        }
        else if (!string.IsNullOrWhiteSpace(portalText))
        {
            parts.Add(portalText);
        }
        else
        {
            parts.Add(topic.Summary);
        }

        return string.Join("\n\n", parts);
    }

    private async Task<string> ResolveXmlUrlAsync(CancellationToken ct)
    {
        var html = await _httpClient.GetStringAsync("xml.html", ct);
        var match = XmlUrlPattern().Match(html);

        if (match.Success)
            return match.Value;

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        _logger.LogWarning("MedlinePlus: could not find XML URL on download page, falling back to today's date");
        return $"xml/mplus_topics_{today}.xml";
    }

    private static string StripHtmlTags(string html)
    {
        // Remove script and style blocks entirely (including their content)
        var text = ScriptBlockPattern().Replace(html, "");
        text = StyleBlockPattern().Replace(text, "");
        // Strip remaining tags
        text = HtmlTagPattern().Replace(text, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = CollapseWhitespace().Replace(text, " ");
        return text.Trim();
    }

    [GeneratedRegex(@"xml/mplus_topics_\d{4}-\d{2}-\d{2}\.xml")]
    private static partial Regex XmlUrlPattern();

    [GeneratedRegex(@"<script[\s\S]*?</script>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptBlockPattern();

    [GeneratedRegex(@"<style[\s\S]*?</style>", RegexOptions.IgnoreCase)]
    private static partial Regex StyleBlockPattern();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagPattern();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex CollapseWhitespace();
}
