namespace Meducate.API.Middleware;

internal sealed class SecurityHeadersMiddleware(RequestDelegate next, IWebHostEnvironment env)
{
    private readonly RequestDelegate _next = next;
    private readonly IWebHostEnvironment _env = env;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

        var isHangfire = context.Request.Path.StartsWithSegments("/hangfire");

        // More permissive CSP for Hangfire dashboard (uses inline scripts/styles) and dev tools
        var csp = _env.IsDevelopment() || isHangfire
            ? "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self' data:; frame-ancestors 'none'"
            : "default-src 'none'; frame-ancestors 'none'";

        context.Response.Headers.ContentSecurityPolicy = csp;

        await _next(context);
    }
}
