using System.Security.Cryptography;

namespace Meducate.API.Middleware;

internal sealed class ETagMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/v1/topics"))
        {
            await _next(context);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await _next(context);
        }
        catch
        {
            // The buffer is disposed on the way out; leave the real stream in place so the
            // exception middleware can still write its error response.
            context.Response.Body = originalBody;
            throw;
        }

        if (context.Response.StatusCode is >= 200 and < 300 && buffer.Length > 0)
        {
            var hash = SHA256.HashData(buffer.ToArray());
            var etag = $"\"{Convert.ToHexString(hash[..8]).ToLowerInvariant()}\"";

            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = "no-cache";

            if (Matches(context.Request.Headers.IfNoneMatch, etag))
            {
                // Nothing has been written to the real stream yet (the body is in the buffer), so there is
                // nothing to truncate; compression wraps that stream and does not support SetLength.
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.ContentLength = null;
                context.Response.Body = originalBody;
                return;
            }
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody);
        context.Response.Body = originalBody;
    }

    // Weak comparison (RFC 9110): proxies and compression turn "x" into W/"x", and clients echo that back.
    private static bool Matches(Microsoft.Extensions.Primitives.StringValues header, string etag) =>
        header.SelectMany(v => v!.Split(',', StringSplitOptions.TrimEntries))
            .Any(t => t == "*" || (t.StartsWith("W/", StringComparison.Ordinal) ? t[2..] : t) == etag);
}
