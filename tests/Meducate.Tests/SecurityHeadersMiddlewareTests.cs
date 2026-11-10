using Meducate.API.Middleware;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Meducate.Tests;

public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task AddsAllSecurityHeadersInProduction()
    {
        var env = new FakeWebHostEnvironment { EnvironmentName = Environments.Production };
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, env);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("camera=(), microphone=(), geolocation=()", context.Response.Headers["Permissions-Policy"]);
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", context.Response.Headers["Content-Security-Policy"]);
    }

    [Fact]
    public async Task AddsPermissiveCSPInDevelopment()
    {
        var env = new FakeWebHostEnvironment { EnvironmentName = Environments.Development };
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask, env);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("camera=(), microphone=(), geolocation=()", context.Response.Headers["Permissions-Policy"]);
        Assert.Equal("default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; font-src 'self' data:; frame-ancestors 'none'", context.Response.Headers["Content-Security-Policy"]);
    }

    [Fact]
    public async Task CallsNextMiddleware()
    {
        var called = false;
        var env = new FakeWebHostEnvironment { EnvironmentName = Environments.Production };
        var middleware = new SecurityHeadersMiddleware(_ => { called = true; return Task.CompletedTask; }, env);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.True(called);
    }

    private class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
        public string ApplicationName { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Production;
    }
}
