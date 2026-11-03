using System.Text.Json;
using Meducate.Web.Models;

namespace Meducate.Web.Services;

internal sealed class ApiService(HttpClient http, ILogger<ApiService> logger)
{
    private readonly HttpClient _http = http;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    internal Task<ApiResult> RegisterUserAsync(string username, string? mode = null, bool acceptedTerms = false, string? termsVersion = null, string? website = null, string? timestamp = null, CancellationToken cancellationToken = default) => PostAsync("/api/v1/users/register", new { email = username, mode, acceptedTerms, termsVersion, website, timestamp }, cancellationToken);
    internal Task<ApiResult> WaitlistAsync(string email, string? website = null, string? timestamp = null, CancellationToken cancellationToken = default) => PostAsync("/api/v1/waitlist", new { email, website, timestamp }, cancellationToken);

    internal async Task<ApiResult<Guid>> CreateOrganisationAsync(string organisationName, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync("/api/v1/orgs", new { organisationName }, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new ApiResult<Guid>
            {
                IsSuccess = false,
                Message = ExtractField(json, "detail") ?? "Failed to create organisation."
            };
        }

        // API returns the Guid directly (as a JSON string in a 201 response)
        if (Guid.TryParse(json.Trim('"'), out var orgId))
        {
            return new ApiResult<Guid>
            {
                IsSuccess = true,
                Data = orgId,
                Message = "Organisation created successfully."
            };
        }

        return new ApiResult<Guid>
        {
            IsSuccess = true,
            Message = "Organisation created successfully."
        };
    }

    internal async Task<ApiResult<ApiKeyResult>> CreateApiKeyAsync(Guid organisationId, string? name = null, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync($"/api/v1/orgs/{organisationId}/keys", new { name }, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new ApiResult<ApiKeyResult>
            {
                IsSuccess = false,
                Message = ExtractField(json, "detail") ?? "Failed to create API key."
            };
        }

        var result = JsonSerializer.Deserialize<ApiKeyResult>(json, _jsonOptions);
        return new ApiResult<ApiKeyResult>
        {
            IsSuccess = true,
            Data = result,
            Message = "API key created successfully."
        };
    }

    internal async Task<ApiResult> DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync("/api/v1/users/me", cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new ApiResult { IsSuccess = true, Message = "Account deleted successfully." };
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ApiResult
        {
            IsSuccess = false,
            Message = ExtractField(json, "detail") ?? "Failed to delete account."
        };
    }

    internal async Task<List<ApiKeyInfo>> GetApiKeysAsync(Guid organisationId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"/api/v1/orgs/{organisationId}/keys", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Array)
                return [];

            var keys = new List<ApiKeyInfo>();
            foreach (var item in root.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp) && idProp.TryGetGuid(out var id) &&
                    item.TryGetProperty("keyId", out var keyIdProp) &&
                    item.TryGetProperty("name", out var nameProp) &&
                    item.TryGetProperty("createdAt", out var createdAtProp) && createdAtProp.TryGetDateTime(out var createdAt) &&
                    item.TryGetProperty("dailyLimit", out var limitProp) && limitProp.TryGetInt32(out var dailyLimit) &&
                    item.TryGetProperty("usageToday", out var usageProp) && usageProp.TryGetInt32(out var usageToday))
                {
                    keys.Add(new ApiKeyInfo
                    {
                        Id = id,
                        KeyId = keyIdProp.GetString() ?? "",
                        Name = nameProp.GetString() ?? "",
                        CreatedAt = createdAt,
                        DailyLimit = dailyLimit,
                        UsageToday = usageToday
                    });
                }
            }

            return keys;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch API keys for organisation {OrgId}", organisationId);
            return [];
        }
    }

    internal async Task<List<UsageHistoryItem>> GetUsageHistoryAsync(Guid organisationId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"/api/v1/orgs/{organisationId}/usage/history", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<List<UsageHistoryItem>>(json, _jsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch usage history for organisation {OrgId}", organisationId);
            return [];
        }
    }

    internal async Task<List<TopEndpointItem>> GetTopEndpointsAsync(Guid organisationId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"/api/v1/orgs/{organisationId}/usage/top-endpoints", cancellationToken);

            if (!response.IsSuccessStatusCode)
                return [];

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<List<TopEndpointItem>>(json, _jsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch top endpoints for organisation {OrgId}", organisationId);
            return [];
        }
    }

    internal async Task<ApiResult> RevokeApiKeyAsync(Guid organisationId, Guid keyId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync($"/api/v1/orgs/{organisationId}/keys/{keyId}", cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new ApiResult { IsSuccess = true, Message = "Key revoked." };
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ApiResult
        {
            IsSuccess = false,
            Message = ExtractField(json, "detail") ?? "Failed to revoke key."
        };
    }

    internal async Task<ApiResult> RenameApiKeyAsync(Guid organisationId, Guid keyId, string name, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/orgs/{organisationId}/keys/{keyId}")
        {
            Content = JsonContent.Create(new { name })
        };

        using var response = await _http.SendAsync(request, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return new ApiResult { IsSuccess = true, Message = "Key renamed." };
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return new ApiResult
        {
            IsSuccess = false,
            Message = ExtractField(json, "detail") ?? "Failed to rename key."
        };
    }

    internal async Task<UserStatusResult?> GetUserStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var hasCookie = _http.DefaultRequestHeaders.Contains("Cookie");
            logger.LogDebug("[ApiService] GET /api/v1/users/me — has Cookie header: {HasCookie}", hasCookie);

            using var response = await _http.GetAsync("/api/v1/users/me", cancellationToken);
            logger.LogDebug("[ApiService] Response: {StatusCode}", (int)response.StatusCode);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return null;

            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var email = root.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            var isVerified = root.TryGetProperty("isEmailVerified", out var v) && v.GetBoolean();

            Guid? orgId = root.TryGetProperty("organisationId", out var o) && o.ValueKind != JsonValueKind.Null
                ? o.GetGuid()
                : null;

            var orgName = root.TryGetProperty("organisationName", out var n) && n.ValueKind != JsonValueKind.Null
                ? n.GetString()
                : null;

            var hasApiKeys = root.TryGetProperty("hasApiKeys", out var k) && k.GetBoolean();

            return new UserStatusResult(email, isVerified, orgId, orgName, hasApiKeys);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch user status");
            return null;
        }
    }

    private async Task<ApiResult> PostAsync<T>(string url, T? body, CancellationToken cancellationToken)
    {
        using var response = body is null
            ? await _http.PostAsync(url, null, cancellationToken)
            : await _http.PostAsJsonAsync(url, body, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            // Success responses may have a message field in the body
            var message = ExtractField(json, "message") ?? "Operation completed successfully.";
            return new ApiResult { IsSuccess = true, Message = message };
        }

        return new ApiResult
        {
            IsSuccess = false,
            Message = ExtractField(json, "detail") ?? "Request failed."
        };
    }

    private static string? ExtractField(string? json, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        catch (JsonException)
        {
            // Not valid JSON
        }

        return null;
    }
}
