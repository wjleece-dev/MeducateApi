using System.Diagnostics;

namespace Meducate.API.Middleware;

internal sealed class RequestTimingMiddleware(RequestDelegate next, ILogger<RequestTimingMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<RequestTimingMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();

        await _next(context);

        var elapsed = Stopwatch.GetElapsedTime(start);

        _logger.LogInformation(
            "{Method} {Path} responded {StatusCode} in {ElapsedMs:F1}ms",
            context.Request.Method,
            context.Request.Path,
            context.Response.StatusCode,
            elapsed.TotalMilliseconds);
    }
}
