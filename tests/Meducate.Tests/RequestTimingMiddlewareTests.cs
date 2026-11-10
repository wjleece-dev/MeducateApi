using Meducate.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Meducate.Tests;

public class RequestTimingMiddlewareTests
{
    [Fact]
    public async Task CallsNextMiddleware()
    {
        var called = false;
        var logger = new LoggerFactory().CreateLogger<RequestTimingMiddleware>();
        var middleware = new RequestTimingMiddleware(_ => { called = true; return Task.CompletedTask; }, logger);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/topics";

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    [Fact]
    public async Task LogsRequestInfo()
    {
        var testLogger = new TestLogger<RequestTimingMiddleware>();
        var middleware = new RequestTimingMiddleware(_ => Task.CompletedTask, testLogger);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/topics";

        await middleware.InvokeAsync(context);

        Assert.Single(testLogger.LogEntries);
        Assert.Contains("GET", testLogger.LogEntries[0]);
        Assert.Contains("/api/topics", testLogger.LogEntries[0]);
    }

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<string> LogEntries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            LogEntries.Add(formatter(state, exception));
        }
    }
}
