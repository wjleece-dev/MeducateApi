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

            if (context.Request.Headers.IfNoneMatch == etag)
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                context.Response.ContentLength = 0;
                originalBody.SetLength(0);
                context.Response.Body = originalBody;
                return;
            }
        }

        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody);
        context.Response.Body = originalBody;
    }
}
