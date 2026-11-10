using Meducate.API;
using Meducate.API.Infrastructure;
using Meducate.API.Middleware;
using Meducate.Domain.Entities;
using Meducate.Domain.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meducate.Tests;

public class ApiKeyMiddlewareTests
{
    private static ApiKeyMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new ApiKeyMiddleware(next, cache, NullLogger<ApiKeyMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateContext(bool requiresKey = true)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        if (requiresKey)
        {
            var endpoint = new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new RequiresApiKeyAttribute()), "test");
            context.SetEndpoint(endpoint);
        }

        return context;
    }

    [Fact]
    public async Task NoRequiresApiKeyAttribute_PassesThrough()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext(requiresKey: false);

        await middleware.InvokeAsync(context, new StubApiKeyService(), new ApiKeyRateLimiter());

        Assert.True(called);
    }

    [Fact]
    public async Task MissingHeader_Returns401()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();

        await middleware.InvokeAsync(context, new StubApiKeyService(), new ApiKeyRateLimiter());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task MalformedKey_NoDot_Returns401()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();
        context.Request.Headers["X-Api-Key"] = "nodothere";

        await middleware.InvokeAsync(context, new StubApiKeyService(), new ApiKeyRateLimiter());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task UnknownKeyId_Returns401()
    {
        var middleware = CreateMiddleware();
        var context = CreateContext();
        context.Request.Headers["X-Api-Key"] = "unknown.secret";

        await middleware.InvokeAsync(context, new StubApiKeyService(), new ApiKeyRateLimiter());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task InactiveKey_Returns401()
    {
        var service = new StubApiKeyService
        {
            Client = new ApiClient { KeyId = "key1", IsActive = false }
        };
        var middleware = CreateMiddleware();
        var context = CreateContext();
        context.Request.Headers["X-Api-Key"] = "key1.secret";

        await middleware.InvokeAsync(context, service, new ApiKeyRateLimiter());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task ExpiredKey_Returns401()
    {
        var service = new StubApiKeyService
        {
            Client = new ApiClient { KeyId = "key1", IsActive = true, ExpiresAt = DateTime.UtcNow.AddDays(-1) }
        };
        var middleware = CreateMiddleware();
        var context = CreateContext();
        context.Request.Headers["X-Api-Key"] = "key1.secret";

        await middleware.InvokeAsync(context, service, new ApiKeyRateLimiter());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task WrongSecretAttempts_DoNotConsumeTheKeysRateLimit()
    {
        var client = new ApiClient { Id = Guid.NewGuid(), KeyId = "key1", IsActive = true, DailyLimit = 1000 };
        var limiter = new ApiKeyRateLimiter();

        // Anyone can claim a key id; failed secrets must not lock the real holder out.
        for (var i = 0; i < ApiKeyRateLimiter.PermitsPerMinute + 10; i++)
        {
            var bad = CreateContext();
            bad.Request.Headers["X-Api-Key"] = "key1.wrong";
            await CreateMiddleware().InvokeAsync(bad, new StubApiKeyService { Client = client, SecretValid = false }, limiter);
            Assert.Equal(401, bad.Response.StatusCode);
        }

        var good = CreateContext();
        good.Request.Headers["X-Api-Key"] = "key1.secret";
        await CreateMiddleware().InvokeAsync(good, new StubApiKeyService { Client = client, SecretValid = true }, limiter);

        Assert.NotEqual(401, good.Response.StatusCode);
        Assert.NotEqual(429, good.Response.StatusCode);
    }

    [Fact]
    public async Task ValidKey_IsLimitedToSixtyRequestsPerMinute()
    {
        var client = new ApiClient { Id = Guid.NewGuid(), KeyId = "key1", IsActive = true, DailyLimit = 1000 };
        var service = new StubApiKeyService { Client = client, SecretValid = true };
        var limiter = new ApiKeyRateLimiter();

        HttpContext last = null!;
        for (var i = 0; i < ApiKeyRateLimiter.PermitsPerMinute + 1; i++)
        {
            last = CreateContext();
            last.Request.Headers["X-Api-Key"] = "key1.secret";
            await CreateMiddleware().InvokeAsync(last, service, limiter);
        }

        Assert.Equal(429, last.Response.StatusCode);
        Assert.True(last.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public async Task DemoKey_GetsAFarHigherBurstLimitThanAPrivateKey()
    {
        var client = new ApiClient { Id = Guid.NewGuid(), KeyId = ApiConstants.DemoKeyId, IsActive = true, DailyLimit = ApiConstants.DemoDailyLimit };
        var service = new StubApiKeyService { Client = client, SecretValid = true };
        var limiter = new ApiKeyRateLimiter();

        HttpContext last = null!;
        for (var i = 0; i < ApiKeyRateLimiter.PermitsPerMinute * 3; i++)
        {
            last = CreateContext();
            last.Request.Headers["X-Api-Key"] = ApiConstants.DemoKeyId + ".secret";
            last.Request.Headers["CF-Connecting-IP"] = $"10.1.{i / 250}.{i % 250 + 1}"; // a different visitor each time
            await CreateMiddleware().InvokeAsync(last, service, limiter);
            Assert.NotEqual(429, last.Response.StatusCode);
        }

        for (var i = ApiKeyRateLimiter.PermitsPerMinute * 3; i <= ApiKeyRateLimiter.DemoPermitsPerMinute; i++)
        {
            last = CreateContext();
            last.Request.Headers["X-Api-Key"] = ApiConstants.DemoKeyId + ".secret";
            last.Request.Headers["CF-Connecting-IP"] = $"10.1.{i / 250}.{i % 250 + 1}"; // a different visitor each time
            await CreateMiddleware().InvokeAsync(last, service, limiter);
        }

        Assert.Equal(429, last.Response.StatusCode);
    }

    [Fact]
    public async Task DemoKey_OneAddressCannotUseMoreThanItsShare()
    {
        var client = new ApiClient { Id = Guid.NewGuid(), KeyId = ApiConstants.DemoKeyId, IsActive = true, DailyLimit = ApiConstants.DemoDailyLimit };
        var service = new StubApiKeyService { Client = client, SecretValid = true };
        var limiter = new ApiKeyRateLimiter();

        async Task<HttpContext> Call(string address)
        {
            var context = CreateContext();
            context.Request.Headers["X-Api-Key"] = ApiConstants.DemoKeyId + ".secret";
            context.Request.Headers["CF-Connecting-IP"] = address;
            await CreateMiddleware().InvokeAsync(context, service, limiter);
            return context;
        }

        for (var i = 0; i < ApiKeyRateLimiter.DemoPermitsPerAddressPerMinute; i++)
            Assert.NotEqual(429, (await Call("203.0.113.7")).Response.StatusCode);

        var refused = await Call("203.0.113.7");
        Assert.Equal(429, refused.Response.StatusCode);
        Assert.True(refused.Response.Headers.ContainsKey("Retry-After"));

        // Another visitor is unaffected.
        Assert.NotEqual(429, (await Call("203.0.113.8")).Response.StatusCode);
    }

    [Fact]
    public async Task PrivateKeys_AreNotLimitedPerAddress()
    {
        var client = new ApiClient { Id = Guid.NewGuid(), KeyId = "key1", IsActive = true, DailyLimit = 1000 };
        var service = new StubApiKeyService { Client = client, SecretValid = true };
        var limiter = new ApiKeyRateLimiter();

        // 60/min per key applies, but the per-address demo limits do not: a second key from the same address is fine.
        var other = new ApiClient { Id = Guid.NewGuid(), KeyId = "key2", IsActive = true, DailyLimit = 1000 };
        var otherService = new StubApiKeyService { Client = other, SecretValid = true };

        for (var i = 0; i < ApiKeyRateLimiter.PermitsPerMinute; i++)
        {
            var c = CreateContext();
            c.Request.Headers["X-Api-Key"] = "key1.secret";
            c.Request.Headers["CF-Connecting-IP"] = "203.0.113.9";
            await CreateMiddleware().InvokeAsync(c, service, limiter);
        }

        var next = CreateContext();
        next.Request.Headers["X-Api-Key"] = "key2.secret";
        next.Request.Headers["CF-Connecting-IP"] = "203.0.113.9";
        await CreateMiddleware().InvokeAsync(next, otherService, limiter);

        Assert.NotEqual(429, next.Response.StatusCode);
    }

    [Fact]
    public async Task InvalidSecret_Returns401()
    {
        var service = new StubApiKeyService
        {
            Client = new ApiClient { KeyId = "key1", IsActive = true },
            SecretValid = false
        };
        var middleware = CreateMiddleware();
        var context = CreateContext();
        context.Request.Headers["X-Api-Key"] = "key1.wrongsecret";

        await middleware.InvokeAsync(context, service, new ApiKeyRateLimiter());

        Assert.Equal(401, context.Response.StatusCode);
    }

    [Fact]
    public async Task RateLimitExceeded_Returns429()
    {
        var client = new ApiClient { KeyId = "key1", IsActive = true, DailyLimit = 10 };
        var service = new StubApiKeyService
        {
            Client = client,
            SecretValid = true,
            UsageCount = 10
        };
        var middleware = CreateMiddleware();
        var context = CreateContext();
        context.Request.Headers["X-Api-Key"] = "key1.validsecret";

        await middleware.InvokeAsync(context, service, new ApiKeyRateLimiter());

        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("86400", context.Response.Headers.RetryAfter.ToString());
    }

    [Fact]
    public async Task ValidKey_PassesThrough_AndSetsContextItems()
    {
        var called = false;
        var client = new ApiClient { KeyId = "key1", IsActive = true, DailyLimit = 100 };
        var service = new StubApiKeyService
        {
            Client = client,
            SecretValid = true,
            UsageCount = 5
        };
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext();
        context.Request.Headers["X-Api-Key"] = "key1.validsecret";

        await middleware.InvokeAsync(context, service, new ApiKeyRateLimiter());

        Assert.True(called);
        Assert.Same(client, context.Items["ApiClient"]);
        Assert.Equal(5, context.Items["ApiUsageCount"]);
    }

    [Fact]
    public async Task UnlimitedKey_PassesThrough_RegardlessOfUsage()
    {
        var called = false;
        var client = new ApiClient { KeyId = "key1", IsActive = true, DailyLimit = 0 };
        var service = new StubApiKeyService
        {
            Client = client,
            SecretValid = true,
            UsageCount = 999999
        };
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = CreateContext();
        context.Request.Headers["X-Api-Key"] = "key1.validsecret";

        await middleware.InvokeAsync(context, service, new ApiKeyRateLimiter());

        Assert.True(called);
    }

    private sealed class StubApiKeyService : IApiKeyService
    {
        public ApiClient? Client { get; set; }
        public bool SecretValid { get; set; }
        public int UsageCount { get; set; }

        public Task<ApiClient?> GetByKeyIdAsync(string keyId, CancellationToken ct = default)
            => Task.FromResult(Client);

        public Task<bool> ValidateSecretAsync(ApiClient client, string secret)
            => Task.FromResult(SecretValid);

        public Task<int> GetTodayUsageCountAsync(Guid clientId, CancellationToken ct = default)
            => Task.FromResult(UsageCount);

        public Task<(ApiClient client, string rawKey)?> CreateKeyForOrganisationAsync(Guid organisationId, string? name = null, int dailyLimit = 1000, CancellationToken ct = default)
            => Task.FromResult<(ApiClient, string)?>(null);

        public Task DisableKeyAsync(Guid clientId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> GetActiveKeyCountAsync(Guid organisationId, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<List<ApiClient>> GetActiveKeysForOrgAsync(Guid organisationId, CancellationToken ct = default)
            => Task.FromResult(new List<ApiClient>());

        public Task<ApiClient?> GetActiveKeyForOrgAsync(Guid keyId, Guid organisationId, CancellationToken ct = default)
            => Task.FromResult<ApiClient?>(null);

        public Task<Dictionary<Guid, int>> GetTodayUsageCountsAsync(List<Guid> clientIds, CancellationToken ct = default)
            => Task.FromResult(new Dictionary<Guid, int>());

        public Task<bool> RenameKeyAsync(Guid clientId, string name, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> HasActiveKeysAsync(Guid organisationId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<Dictionary<DateOnly, int>> GetUsageHistoryAsync(List<Guid> clientIds, int days = 7, CancellationToken ct = default)
            => Task.FromResult(new Dictionary<DateOnly, int>());

        public Task<List<(string Endpoint, int Count)>> GetTopEndpointsAsync(List<Guid> clientIds, int days = 7, int topN = 5, CancellationToken ct = default)
            => Task.FromResult(new List<(string, int)>());
    }
}
