namespace Meducate.API.Middleware;

internal sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<CorrelationIdMiddleware> _logger = logger;
    private const string HeaderName = "X-Correlation-Id";

    private static bool IsSafeCorrelationId(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                return false;
        }
        return true;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var provided = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = provided is { Length: > 0 and <= 64 } && IsSafeCorrelationId(provided)
            ? provided
            : Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await _next(context);
        }
    }
}
