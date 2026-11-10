using Meducate.API.Infrastructure;

namespace Meducate.API.Middleware;

internal sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger = logger;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (BadHttpRequestException)
        {
            // Unbindable route/query values: a client error, not a server fault (Development throws these; Production answers 400 itself)
            await ProblemResponse.WriteAsync(context, StatusCodes.Status400BadRequest, "One or more request parameters are invalid.");
        }
        catch (Exception ex)
        {
            var traceId = context.Items.TryGetValue("CorrelationId", out var cid) ? cid as string : null;
            _logger.LogError(ex, "Unhandled exception on {Method} {Path} [TraceId: {TraceId}]",
                context.Request.Method, context.Request.Path, traceId);

            await ProblemResponse.WriteAsync(context, StatusCodes.Status500InternalServerError, "An unexpected error occurred.");
        }
    }
}
