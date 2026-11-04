using System.Net;
using System.Net.Sockets;

namespace Meducate.API.Infrastructure;

internal static class ClientIp
{
    // Behind Cloudflare and Railway's edge the connection address is a proxy, and it varies per request,
    // so address-keyed limits never accumulated. Cloudflare overwrites CF-Connecting-IP with the real client address.
    internal static string Resolve(HttpContext context)
    {
        var header = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(header) && IPAddress.TryParse(header.Trim(), out var ip))
            return Normalize(ip);

        return context.Connection.RemoteIpAddress is { } remote ? Normalize(remote) : "unknown";
    }

    // One IPv6 customer controls a whole /64, so limiting single /128 addresses would be trivially evaded.
    internal static string Normalize(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily != AddressFamily.InterNetworkV6)
            return ip.ToString();

        Span<byte> bytes = stackalloc byte[16];
        ip.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();
        return $"{new IPAddress(bytes)}/64";
    }
}
