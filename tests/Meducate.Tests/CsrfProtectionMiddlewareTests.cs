using Meducate.API.Middleware;
using Microsoft.AspNetCore.Http;

namespace Meducate.Tests;

public class CsrfProtectionMiddlewareTests
{
    private static CsrfProtectionMiddleware CreateMiddleware(RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new CsrfProtectionMiddleware(next);
    }

    [Fact]
    public async Task GET_Request_PassesThrough()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/conditions";

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.NotEqual(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task POST_WithoutHeaders_Returns403WithJson()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/orgs";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
    }

    [Fact]
    public async Task POST_WithCsrfHeader_PassesThrough()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/orgs";
        context.Request.Headers["X-Requested-By"] = "MeducateAPI";

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task POST_WithApiKey_BypassesCsrf()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/conditions";
        context.Request.Headers["X-Api-Key"] = "some-key";

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task POST_WithWrongCsrfValue_Returns403()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/orgs";
        context.Request.Headers["X-Requested-By"] = "WrongValue";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task DELETE_WithoutHeaders_Returns403()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Method = "DELETE";
        context.Request.Path = "/api/users/me";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task PATCH_WithoutHeaders_Returns403()
    {
        var middleware = CreateMiddleware();
        var context = new DefaultHttpContext();
        context.Request.Method = "PATCH";
        context.Request.Path = "/api/orgs/123/keys/456";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task NonApiPath_SkipsCsrfCheck()
    {
        var called = false;
        var middleware = CreateMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/health";

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }
}
