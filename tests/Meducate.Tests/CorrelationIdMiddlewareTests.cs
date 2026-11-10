using Meducate.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Meducate.Tests;

public class CorrelationIdMiddlewareTests
{
    private static CorrelationIdMiddleware CreateMiddleware(RequestDelegate next)
    {
        var logger = new LoggerFactory().CreateLogger<CorrelationIdMiddleware>();
        return new CorrelationIdMiddleware(next, logger);
    }

    [Fact]
    public async Task GeneratesCorrelationId_WhenNotProvided()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        var header = context.Response.Headers["X-Correlation-Id"].ToString();
        Assert.False(string.IsNullOrEmpty(header));
        Assert.True(Guid.TryParse(header, out _));
    }

    [Fact]
    public async Task UsesProvidedCorrelationId()
    {
        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "my-custom-id";

        await middleware.InvokeAsync(context);

        Assert.Equal("my-custom-id", context.Response.Headers["X-Correlation-Id"].ToString());
    }

    [Fact]
    public async Task StoresCorrelationId_InHttpContextItems()
    {
        string? capturedId = null;
        var middleware = CreateMiddleware(ctx =>
        {
            capturedId = ctx.Items["CorrelationId"] as string;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "test-123";

        await middleware.InvokeAsync(context);

        Assert.Equal("test-123", capturedId);
    }

    [Fact]
    public async Task CallsNextMiddleware()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }
}
