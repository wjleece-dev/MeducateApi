using System.Net;
using Meducate.API.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Meducate.Tests;

public class ClientIpTests
{
    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2a0a:ef40:6b5:4801:c22d:e36b:7a92:1485", "2a0a:ef40:6b5:4801::/64")]
    [InlineData("2a0a:ef40:6b5:4801:1:2:3:4", "2a0a:ef40:6b5:4801::/64")]
    public void Normalize_KeepsIPv4AndGroupsIPv6ByPrefix(string address, string expected) =>
        Assert.Equal(expected, ClientIp.Normalize(IPAddress.Parse(address)));

    [Fact]
    public void Resolve_PrefersCloudflareHeaderOverTheProxyAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.70.86.36");
        context.Request.Headers["CF-Connecting-IP"] = "198.51.100.9";

        Assert.Equal("198.51.100.9", ClientIp.Resolve(context));
    }

    [Fact]
    public void Resolve_FallsBackToTheConnectionAddressWhenTheHeaderIsMissingOrJunk()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("172.70.86.36");
        Assert.Equal("172.70.86.36", ClientIp.Resolve(context));

        context.Request.Headers["CF-Connecting-IP"] = "not-an-ip";
        Assert.Equal("172.70.86.36", ClientIp.Resolve(context));
    }
}
