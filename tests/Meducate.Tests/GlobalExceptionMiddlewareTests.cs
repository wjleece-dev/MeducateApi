using Meducate.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Meducate.Tests;

public class GlobalExceptionMiddlewareTests
{
    private static GlobalExceptionMiddleware CreateMiddleware(RequestDelegate next)
    {
        var logger = new LoggerFactory().CreateLogger<GlobalExceptionMiddleware>();
        return new GlobalExceptionMiddleware(next, logger);
    }

    [Fact]
    public async Task NoException_PassesThrough()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.NotEqual(500, context.Response.StatusCode);
    }

    [Fact]
    public async Task UnhandledException_Returns500()
    {
        var middleware = CreateMiddleware(_ => throw new InvalidOperationException("Test error"));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task UnhandledException_ReturnsGenericMessage()
    {
        var middleware = CreateMiddleware(_ => throw new Exception("Sensitive details"));
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Contains("An unexpected error occurred.", body);
        Assert.Contains("\"title\"", body);
        Assert.Contains("\"status\"", body);
        Assert.DoesNotContain("Sensitive details", body);
    }
}
