using System.Text.Json;
using Meducate.API.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Meducate.Tests;

public class ProblemResponseTests
{
    private static async Task<JsonDocument> WriteAndParse(DefaultHttpContext context, int statusCode, string detail)
    {
        await ProblemResponse.WriteAsync(context, statusCode, detail);
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }

    [Theory]
    [InlineData(400, "Bad Request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not Found")]
    [InlineData(429, "Too Many Requests")]
    [InlineData(500, "Internal Server Error")]
    public async Task WritesCorrectStatusAndTitle(int statusCode, string expectedTitle)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        using var doc = await WriteAndParse(context, statusCode, "test detail");
        var root = doc.RootElement;

        Assert.Equal(statusCode, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal(expectedTitle, root.GetProperty("title").GetString());
        Assert.Equal(statusCode, root.GetProperty("status").GetInt32());
        Assert.Equal("test detail", root.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task IncludesTraceId_WhenCorrelationIdPresent()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Items["CorrelationId"] = "abc123";

        using var doc = await WriteAndParse(context, 400, "bad");
        var root = doc.RootElement;

        Assert.Equal("abc123", root.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task TraceIdIsNull_WhenNoCorrelationId()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        using var doc = await WriteAndParse(context, 400, "bad");
        var root = doc.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("traceId").ValueKind);
    }
}
