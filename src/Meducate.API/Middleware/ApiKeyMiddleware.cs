using Meducate.API.Infrastructure;
using Meducate.Domain.Entities;
using Meducate.Domain.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Meducate.API.Middleware;

internal sealed class ApiKeyMiddleware(RequestDelegate next, IMemoryCache cache, ILogger<ApiKeyMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<ApiKeyMiddleware> _logger = logger;
    private const string HeaderName = "X-Api-Key";

    private static readonly TimeSpan ClientCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UsageCacheDuration = TimeSpan.FromSeconds(15);

    public async Task InvokeAsync(HttpContext context, IApiKeyService apiKeyService)
    {
        // Only enforce API key on endpoints tagged with [RequiresApiKey]
        var endpoint = context.GetEndpoint();
        var requiresKey = endpoint?.Metadata.GetMetadata<RequiresApiKeyAttribute>() is not null;

        if (!requiresKey)
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var header) ||
            string.IsNullOrWhiteSpace(header))
        {
            await ProblemResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Missing API key.");
            return;
        }

        var parts = header.ToString().Split('.', 2);
        if (parts.Length != 2)
        {
            await ProblemResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Invalid API key format.");
            return;
        }

        var keyId = parts[0];
        var secret = parts[1];

        // Cache the client lookup by KeyId (including negative results to prevent DB abuse)
        var cacheKey = $"apiclient:{keyId}";
        var cacheHit = _cache.TryGetValue(cacheKey, out ApiClient? client);
        if (!cacheHit)
        {
            client = await apiKeyService.GetByKeyIdAsync(keyId);
            _cache.Set(cacheKey, client, client is not null ? ClientCacheDuration : TimeSpan.FromSeconds(5));
        }

        if (client == null || !client.IsActive || client.IsExpired)
        {
            await ProblemResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Invalid API key.");
            return;
        }

        var valid = await apiKeyService.ValidateSecretAsync(client, secret);
        if (!valid)
        {
            _logger.LogWarning("Failed secret validation for keyId '{KeyId}'", keyId);
            await ProblemResponse.WriteAsync(context, StatusCodes.Status401Unauthorized, "Invalid API key.");
            return;
        }

        // Cache usage count — short TTL so limits stay responsive
        var today = DateTime.UtcNow.Date;
        var usageCacheKey = $"apiusage:{client.Id}:{today:yyyy-MM-dd}";
        if (!_cache.TryGetValue(usageCacheKey, out int usageCount))
        {
            var ct = context.RequestAborted;
            usageCount = await apiKeyService.GetTodayUsageCountAsync(client.Id, ct);

            _cache.Set(usageCacheKey, usageCount, UsageCacheDuration);
        }

        if (client.DailyLimit > 0 && usageCount >= client.DailyLimit)
        {
            context.Response.Headers.RetryAfter = "86400";
            await ProblemResponse.WriteAsync(context, StatusCodes.Status429TooManyRequests,
                "Daily request limit reached. Resets at midnight UTC. Get a free key with 1,000 requests/day at https://meducateapi.com/register.");
            return;
        }

        // Rate limit headers
        context.Response.Headers["X-RateLimit-Limit"] = client.DailyLimit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, client.DailyLimit - usageCount - 1).ToString();
        var nextMidnightUtc = DateTime.UtcNow.Date.AddDays(1);
        context.Response.Headers["X-RateLimit-Reset"] = new DateTimeOffset(nextMidnightUtc).ToUnixTimeSeconds().ToString();

        // attach client + cached count to HttpContext for downstream middleware
        context.Items["ApiClient"] = client;
        context.Items["ApiUsageCount"] = usageCount;

        await _next(context);
    }
}
