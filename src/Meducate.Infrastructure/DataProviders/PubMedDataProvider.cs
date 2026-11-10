using System.Text.Json;
using System.Xml.Linq;
using Meducate.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Meducate.Infrastructure.DataProviders;

internal sealed class PubMedDataProvider(
    HttpClient httpClient,
    IConfiguration config,
    ILogger<PubMedDataProvider> logger) : IMedicalDataProvider
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly string? _apiKey = config["PubMed:ApiKey"];
    private readonly ILogger<PubMedDataProvider> _logger = logger;

    private static readonly TimeSpan ThrottleDelay = TimeSpan.FromMilliseconds(350);

    public string SourceName => "PubMed";

    public async Task<RawTopicData?> FetchTopicDataAsync(string topicName, CancellationToken ct = default)
    {
        try
        {
            var ids = await SearchArticleIdsAsync($"{topicName} AND review[pt]", 5, ct);
            if (ids.Count == 0)
                return null;

            var abstractText = await FetchAbstractsAsync(ids, ct);
            if (string.IsNullOrWhiteSpace(abstractText))
                return null;

            return new RawTopicData(topicName, abstractText, SourceName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PubMed: failed to fetch data for {Topic}", topicName);
            return null;
        }
    }

    public Task<IReadOnlyList<RawTopicData>> DiscoverTopicsAsync(
        IReadOnlySet<string> existingNames, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<RawTopicData>>([]);
    }

    private async Task<List<string>> SearchArticleIdsAsync(
        string query, int maxResults, CancellationToken ct, int? relativeDays = null)
    {
        var url = $"esearch.fcgi?db=pubmed&term={Uri.EscapeDataString(query)}&retmax={maxResults}&sort=relevance&retmode=json";
        if (relativeDays.HasValue)
            url += $"&reldate={relativeDays.Value}&datetype=edat";
        _logger.LogInformation("PubMed: searching with URL: {Url}", url);

        url = AppendApiKey(url);

        await Task.Delay(ThrottleDelay, ct);
        var response = await _httpClient.GetStringAsync(url, ct);

        _logger.LogInformation("PubMed: ESearch response length: {Length} chars", response.Length);

        var ids = ParseSearchResponse(response);

        if (ids.Count == 0)
            _logger.LogWarning("PubMed: ESearch returned no article IDs");
        else
            _logger.LogInformation("PubMed: ESearch returned {Count} article IDs", ids.Count);

        return ids;
    }

    internal static List<string> ParseSearchResponse(string json)
    {
        using var doc = JsonDocument.Parse(json);

        if (doc.RootElement.TryGetProperty("esearchresult", out var searchResult))
        {
            if (searchResult.TryGetProperty("ERROR", out _))
                return [];

            if (searchResult.TryGetProperty("idlist", out var idList))
            {
                var ids = new List<string>();
                foreach (var id in idList.EnumerateArray())
                    ids.Add(id.GetString()!);

                return ids;
            }
        }

        return [];
    }

    private async Task<string> FetchAbstractsAsync(List<string> ids, CancellationToken ct)
    {
        var idParam = string.Join(",", ids);
        var url = $"efetch.fcgi?db=pubmed&id={idParam}&retmode=xml&rettype=abstract";
        url = AppendApiKey(url);

        _logger.LogInformation("PubMed: fetching abstracts for {Count} articles", ids.Count);

        await Task.Delay(ThrottleDelay, ct);
        var xml = await _httpClient.GetStringAsync(url, ct);

        _logger.LogInformation("PubMed: EFetch abstract response length: {Length} chars", xml.Length);

        var abstracts = ParseAbstractsXml(xml);

        _logger.LogInformation("PubMed: extracted {Count} abstract sections", abstracts.Count);

        return string.Join("\n\n", abstracts);
    }

    internal static List<string> ParseAbstractsXml(string xml)
    {
        var doc = XDocument.Parse(xml);

        return doc.Descendants("AbstractText")
            .Select(e => e.Value)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
    }

    internal string AppendApiKey(string url)
    {
        if (!string.IsNullOrWhiteSpace(_apiKey))
            url += $"&api_key={Uri.EscapeDataString(_apiKey)}";
        return url;
    }
}
