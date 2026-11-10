using System.Xml.Linq;
using Meducate.Application.Helpers;
using Meducate.Domain.Services;
using Microsoft.Extensions.Logging;

namespace Meducate.Infrastructure.DataProviders;

// MedlinePlus's general Health Topics feed (see MedlinePlusDataProvider) rarely produces
// standalone Nutrient topics -- vitamins/minerals mostly show up as passing mentions inside
// disease articles, not their own page. MedlinePlus separately publishes short glossary-style
// term definitions specifically for vitamins, minerals, and nutrition, which map far more
// directly onto the Nutrient type. This provider is scoped to just those three feeds.
internal sealed class MedlinePlusDefinitionsProvider(HttpClient httpClient, ILogger<MedlinePlusDefinitionsProvider> logger) : IMedicalDataProvider
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<MedlinePlusDefinitionsProvider> _logger = logger;

    private static readonly string[] FeedPaths =
    [
        "xml/vitaminsdefinitions.xml",
        "xml/mineralsdefinitions.xml",
        "xml/nutritiondefinitions.xml",
    ];

    private List<ParsedTerm>? _cachedTerms;
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    internal sealed record ParsedTerm(string Term, string Definition, string? Reference, string? ReferenceUrl);

    public string SourceName => "MedlinePlus Definitions";

    public async Task<RawTopicData?> FetchTopicDataAsync(string topicName, CancellationToken ct = default)
    {
        try
        {
            var terms = await GetOrLoadTermsAsync(ct);
            var match = terms.FirstOrDefault(t => string.Equals(t.Term, topicName, StringComparison.OrdinalIgnoreCase));
            return match is null ? null : ToRawTopicData(match);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MedlinePlus Definitions: failed to fetch data for {Topic}", topicName);
            return null;
        }
    }

    public async Task<IReadOnlyList<RawTopicData>> DiscoverTopicsAsync(IReadOnlySet<string> existingNames, CancellationToken ct = default)
    {
        try
        {
            var terms = await GetOrLoadTermsAsync(ct);

            var newTerms = terms
                .Where(t => !existingNames.Contains(t.Term))
                .Select(ToRawTopicData)
                .ToList();

            _logger.LogInformation("MedlinePlus Definitions: {Total} terms, {New} are new", terms.Count, newTerms.Count);

            return newTerms;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MedlinePlus Definitions: discovery failed");
            return [];
        }
    }

    public async Task<IReadOnlySet<string>> GetKnownTopicNamesAsync(CancellationToken ct = default)
    {
        try
        {
            var terms = await GetOrLoadTermsAsync(ct);
            return new HashSet<string>(terms.Select(t => t.Term), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MedlinePlus Definitions: failed to get known term names");
            return new HashSet<string>();
        }
    }

    private static RawTopicData ToRawTopicData(ParsedTerm term)
    {
        var text = string.IsNullOrWhiteSpace(term.Reference)
            ? term.Definition
            : $"[Source: {term.Reference.Trim()}]\n{term.Definition}";

        return new RawTopicData(term.Term, text, "MedlinePlus", ContentHash: ContentHasher.ComputeHash(text));
    }

    private async Task<List<ParsedTerm>> GetOrLoadTermsAsync(CancellationToken ct)
    {
        if (_cachedTerms is not null)
            return _cachedTerms;

        await _cacheLock.WaitAsync(ct);
        try
        {
            if (_cachedTerms is not null)
                return _cachedTerms;

            var terms = new List<ParsedTerm>();
            foreach (var path in FeedPaths)
            {
                try
                {
                    var xml = await _httpClient.GetStringAsync(path, ct);
                    terms.AddRange(ParseDefinitionsXml(xml));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MedlinePlus Definitions: failed to load feed {Path}", path);
                }
            }

            // Feeds can repeat the same general term (e.g. "Dietary Supplements" appears in
            // more than one) -- keep the first occurrence.
            var deduped = terms
                .GroupBy(t => t.Term, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            _logger.LogInformation("MedlinePlus Definitions: parsed {Count} unique terms from {FeedCount} feeds", deduped.Count, FeedPaths.Length);
            _cachedTerms = deduped;
            return deduped;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    internal static List<ParsedTerm> ParseDefinitionsXml(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);
        var terms = new List<ParsedTerm>();

        foreach (var group in doc.Descendants("term-group"))
        {
            var term = CleanLeadingMarker(group.Element("term")?.Value);
            var definition = CleanLeadingMarker(group.Element("definition")?.Value);

            if (string.IsNullOrWhiteSpace(term) || string.IsNullOrWhiteSpace(definition))
                continue;

            var reference = group.Attribute("reference")?.Value?.Trim();
            var referenceUrl = group.Attribute("reference-url")?.Value?.Trim();

            terms.Add(new ParsedTerm(term, definition, reference, referenceUrl));
        }

        return terms;
    }

    // MedlinePlus's definitions XML exports a stray leading '>' inside the CDATA content
    // of both <term> and <definition> elements -- strip it rather than pass it through.
    private static string? CleanLeadingMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        value = value.Trim();
        return value.StartsWith('>') ? value[1..].Trim() : value;
    }
}
