using Meducate.API.Middleware;
using Meducate.Domain.Entities;
using Meducate.Domain.Services;
using Microsoft.AspNetCore.Http;

namespace Meducate.Tests;

public class UsageLoggingMiddlewareTests
{
    [Fact]
    public async Task CallsNextMiddleware()
    {
        var called = false;
        var middleware = new UsageLoggingMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, new StubUsageService());

        Assert.True(called);
    }

    [Fact]
    public async Task LogsUsage_WhenApiClientInItems()
    {
        var service = new StubUsageService();
        var middleware = new UsageLoggingMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/topics";
        var client = new ApiClient { Id = Guid.NewGuid(), IsActive = true, DailyLimit = 0 };
        client.Organisation = new Organisation { Name = "TestOrg" };
        context.Items["ApiClient"] = client;
        context.Items["ApiUsageCount"] = 5;

        await middleware.InvokeAsync(context, service);

        Assert.True(service.LogUsageCalled);
        Assert.Equal(client.Id, service.LoggedClientId);
    }

    [Fact]
    public async Task SkipsLogging_WhenNoApiClientInItems()
    {
        var service = new StubUsageService();
        var middleware = new UsageLoggingMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context, service);

        Assert.False(service.LogUsageCalled);
    }

    [Fact]
    public async Task ChecksOwnerEmail_WhenUsageHitsWarningThreshold()
    {
        // DailyLimit=5, 80% threshold -> 4. previousCount=3 + this request = 4.
        var service = new StubUsageService();
        var middleware = new UsageLoggingMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        var client = new ApiClient { Id = Guid.NewGuid(), IsActive = true, DailyLimit = 5 };
        client.Organisation = new Organisation { Name = "TestOrg" };
        context.Items["ApiClient"] = client;
        context.Items["ApiUsageCount"] = 3;

        // Owner email is null, so the middleware checks it but stops short of enqueuing the warning email.
        await middleware.InvokeAsync(context, service);

        Assert.True(service.GetOwnerEmailCalled);
    }

    [Fact]
    public async Task DoesNotCheckOwnerEmail_WhenUsageBelowWarningThreshold()
    {
        // DailyLimit=5, 80% threshold -> 4. previousCount=1 + this request = 2, well below threshold.
        var service = new StubUsageService();
        var middleware = new UsageLoggingMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        var client = new ApiClient { Id = Guid.NewGuid(), IsActive = true, DailyLimit = 5 };
        client.Organisation = new Organisation { Name = "TestOrg" };
        context.Items["ApiClient"] = client;
        context.Items["ApiUsageCount"] = 1;

        await middleware.InvokeAsync(context, service);

        Assert.False(service.GetOwnerEmailCalled);
    }

    [Fact]
    public async Task DoesNotCheckOwnerEmail_WhenUsageAlreadyPastWarningThreshold()
    {
        // DailyLimit=5, 80% threshold -> 4. previousCount=6 + this request = 7, past the threshold
        // already crossed by an earlier request — the warning should fire once, not on every request after.
        var service = new StubUsageService();
        var middleware = new UsageLoggingMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        var client = new ApiClient { Id = Guid.NewGuid(), IsActive = true, DailyLimit = 5 };
        client.Organisation = new Organisation { Name = "TestOrg" };
        context.Items["ApiClient"] = client;
        context.Items["ApiUsageCount"] = 6;

        await middleware.InvokeAsync(context, service);

        Assert.False(service.GetOwnerEmailCalled);
    }

    private sealed class StubUsageService : IApiKeyUsageService
    {
        public bool LogUsageCalled { get; private set; }
        public Guid LoggedClientId { get; private set; }
        public bool GetOwnerEmailCalled { get; private set; }

        public Task LogUsageAsync(Guid clientId, string? endpoint, string? method, int statusCode, string? organisationName, string? queryString, CancellationToken ct = default)
        {
            LogUsageCalled = true;
            LoggedClientId = clientId;
            return Task.CompletedTask;
        }

        public Task<string?> GetOrganisationOwnerEmailAsync(Guid organisationId, CancellationToken ct = default)
        {
            GetOwnerEmailCalled = true;
            // Returning null keeps the warning-email path from reaching the Hangfire enqueue call.
            return Task.FromResult<string?>(null);
        }

        public Task DeleteUserAccountAsync(Guid userId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
