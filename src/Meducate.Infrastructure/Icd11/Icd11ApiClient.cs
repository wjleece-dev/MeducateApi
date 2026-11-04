using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Meducate.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Meducate.Infrastructure.Icd11;

internal sealed class Icd11ApiClient(
    HttpClient httpClient,
    IConfiguration config,
    ILogger<Icd11ApiClient> logger) : IIcd11CodingService
{
    private const string TokenUrl = "https://icdaccessmanagement.who.int/connect/token";

    // Release pinned to a specific WHO MMS release; WHO cuts a new one roughly yearly. When this changes,
    // every stored code must be looked up again (POST /internal/topics/recode).
    private const string SearchUrl = "https://id.who.int/icd/release/11/2026-01/mms/search";

    private readonly HttpClient _httpClient = httpClient;
    private readonly string _clientId = config["Icd11:ClientId"] ?? "";
    private readonly string _clientSecret = config["Icd11:ClientSecret"] ?? "";
    private readonly ILogger<Icd11ApiClient> _logger = logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    private string? _cachedToken;
    private DateTime _tokenExpiresAtUtc = DateTime.MinValue;

    public async Task<Icd11Match?> LookupAsync(string topicName, CancellationToken ct = default)
    {
        try
        {
            var token = await GetAccessTokenAsync(ct);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{SearchUrl}?q={Uri.EscapeDataString(topicName)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("Accept-Language", "en");
            request.Headers.Add("API-Version", "v2");

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseSearchResponse(json, topicName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ICD-11: lookup failed for {Topic}", topicName);
            return null;
        }
    }

    // WHO's search results are sorted by relevance, which is not identity, so the first coded hit is often a
    // different condition. Icd11Matcher accepts a result only when it really is the topic; no match means no code.
    internal static Icd11Match? ParseSearchResponse(string json, string topicName)
    {
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.TryGetProperty("destinationEntities", out var entities) && entities.ValueKind == JsonValueKind.Array
            ? Icd11Matcher.Select(topicName, entities)
            : null;
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresAtUtc)
            return _cachedToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiresAtUtc)
                return _cachedToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["scope"] = "icdapi_access",
                })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}")));

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var token = doc.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;

            _cachedToken = token;
            // Refresh a few minutes early so an in-flight request never gets caught by expiry.
            _tokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(expiresIn - 300, 60));

            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
