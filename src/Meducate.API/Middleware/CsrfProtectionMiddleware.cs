using Meducate.API.Infrastructure;

namespace Meducate.API.Middleware;

internal sealed class CsrfProtectionMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;
    private const string RequiredHeader = "X-Requested-By";
    private const string ExpectedValue = "MeducateAPI";

    public async Task InvokeAsync(HttpContext context)
    {
        var method = context.Request.Method;

        if (context.Request.Path.StartsWithSegments("/api")
            && method is "POST" or "PUT" or "PATCH" or "DELETE"
            && !context.Request.Headers.ContainsKey("X-Api-Key")
            && context.Request.Headers[RequiredHeader] != ExpectedValue)
        {
            await ProblemResponse.WriteAsync(context, StatusCodes.Status403Forbidden, "Missing or invalid X-Requested-By header.");
            return;
        }

        await _next(context);
    }
}
