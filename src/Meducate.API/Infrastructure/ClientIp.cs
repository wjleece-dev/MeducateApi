using System.Net;

namespace Meducate.API.Infrastructure;

internal static class ClientIp
{
    // Behind Cloudflare and Railway's edge the connection address is a proxy, and it varies per request,
    // so address-keyed limits never accumulated. Cloudflare overwrites CF-Connecting-IP with the real client address.
    internal static string Resolve(HttpContext context)
    {
        var header = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header) && IPAddress.TryParse(header.Trim(), out var ip))
            return ip.ToString();

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
