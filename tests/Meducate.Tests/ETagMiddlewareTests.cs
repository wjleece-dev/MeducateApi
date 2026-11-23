using System.Text;
using Meducate.API.Middleware;
using Microsoft.AspNetCore.Http;

namespace Meducate.Tests;

public class ETagMiddlewareTests
{
    [Fact]
    public async Task NonTopicPath_SkipsETagProcessing()
    {
        var called = false;
        var middleware = new ETagMiddleware(_ => { called = true; return Task.CompletedTask; });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/orgs";

        await middleware.InvokeAsync(context);

        Assert.True(called);
        Assert.False(context.Response.Headers.ContainsKey("ETag"));
    }

    [Fact]
    public async Task TopicPath_WhenHandlerThrows_RestoresTheOriginalBodyStream()
    {
        var middleware = new ETagMiddleware(_ => throw new InvalidOperationException("boom"));
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/topics";
        var original = new MemoryStream();
        context.Response.Body = original;

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Same(original, context.Response.Body);
    }

    [Fact]
    public async Task TopicPath_SetsETagAndCacheControl()
    {
        var middleware = new ETagMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("{\"name\":\"test\"}")).AsTask();
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/topics";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.False(string.IsNullOrEmpty(context.Response.Headers.ETag.ToString()));
        Assert.Equal("no-cache", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task MatchingIfNoneMatch_Returns304()
    {
        var body = Encoding.UTF8.GetBytes("{\"name\":\"test\"}");

        // First request to get the ETag
        string etag = null!;
        var firstMiddleware = new ETagMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.Body.WriteAsync(body).AsTask();
        });
        var firstContext = new DefaultHttpContext();
        firstContext.Request.Path = "/api/v1/topics";
        firstContext.Response.Body = new MemoryStream();
        await firstMiddleware.InvokeAsync(firstContext);
        etag = firstContext.Response.Headers.ETag.ToString();

        // Second request with If-None-Match
        var middleware = new ETagMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.Body.WriteAsync(body).AsTask();
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/topics";
        context.Request.Headers.IfNoneMatch = etag;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status304NotModified, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("W/")]
    [InlineData("")]
    public async Task IfNoneMatch_MatchesStrongOrWeakFormOfTheEtag(string prefix)
    {
        var body = Encoding.UTF8.GetBytes("{\"name\":\"test\"}");
        var first = new DefaultHttpContext();
        first.Request.Path = "/api/v1/topics";
        first.Response.Body = new MemoryStream();
        await new ETagMiddleware(ctx => ctx.Response.Body.WriteAsync(body).AsTask()).InvokeAsync(first);
        var etag = first.Response.Headers.ETag.ToString();

        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/topics";
        context.Request.Headers.IfNoneMatch = $"\"other\", {prefix}{etag}";
        // Compression wraps the real stream and throws on SetLength; the 304 path must not touch it.
        context.Response.Body = new NoSetLengthStream();

        await new ETagMiddleware(ctx => ctx.Response.Body.WriteAsync(body).AsTask()).InvokeAsync(context);

        Assert.Equal(StatusCodes.Status304NotModified, context.Response.StatusCode);
    }

    private sealed class NoSetLengthStream : MemoryStream
    {
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    [Fact]
    public async Task NonMatchingIfNoneMatch_ReturnsFullResponse()
    {
        var middleware = new ETagMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 200;
            return ctx.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("{\"name\":\"test\"}")).AsTask();
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/topics";
        context.Request.Headers.IfNoneMatch = "\"stale-etag\"";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(200, context.Response.StatusCode);
        Assert.NotEqual("\"stale-etag\"", context.Response.Headers.ETag.ToString());
    }
}
