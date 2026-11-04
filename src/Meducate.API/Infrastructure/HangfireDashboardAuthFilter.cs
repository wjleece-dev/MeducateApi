using System.Security.Cryptography;
using System.Text;
using Hangfire.Dashboard;

namespace Meducate.API.Infrastructure;

internal sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    // New name: the previous scheme stored the raw password in a cookie called HangfireAuth, and a browser still
    // holding that one would send both values under the same name.
    internal const string CookieName = "HangfireSession";
    private const string LegacyCookieName = "HangfireAuth";
    internal const string LoginPath = "/hangfire-login";
    private const string PasswordKey = "Hangfire:DashboardPassword";

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var expectedPassword = httpContext.RequestServices.GetRequiredService<IConfiguration>()[PasswordKey];

        if (string.IsNullOrEmpty(expectedPassword))
            return false;

        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Meducate.API.HangfireAuth");

        if (httpContext.Request.Cookies.TryGetValue(CookieName, out var cookieValue))
        {
            if (Matches(cookieValue, SessionToken(expectedPassword)))
                return true;

            logger.LogWarning("Hangfire dashboard: session cookie present but not valid (length {Length})", cookieValue.Length);
        }

        WriteLoginPage(httpContext, httpContext.Request.Query.ContainsKey("error") ? "Invalid password." : null);
        return false;
    }

    // The password is posted to LoginPath, never put in the URL, so it stays out of access logs and history.
    internal static IResult Login(HttpContext httpContext, IConfiguration config)
    {
        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Meducate.API.HangfireAuth");
        var expectedPassword = config[PasswordKey];
        var submitted = httpContext.Request.HasFormContentType ? httpContext.Request.Form["password"].FirstOrDefault() : null;

        if (string.IsNullOrEmpty(expectedPassword) || !Matches(submitted, expectedPassword))
        {
            // Lengths only, never the values: enough to tell a trailing space or a truncated paste from a wrong password.
            logger.LogWarning("Hangfire dashboard: login rejected (form present: {Form}, submitted length {Submitted}, configured length {Configured})",
                httpContext.Request.HasFormContentType, submitted?.Length ?? 0, expectedPassword?.Length ?? 0);
            return Results.Redirect("/hangfire?error=1");
        }

        logger.LogInformation("Hangfire dashboard: login accepted");
        httpContext.Response.Cookies.Delete(LegacyCookieName, new CookieOptions { Path = "/", Secure = true, SameSite = SameSiteMode.Strict });

        httpContext.Response.Cookies.Append(CookieName, SessionToken(expectedPassword), new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/hangfire",
            MaxAge = TimeSpan.FromHours(12)
        });

        return Results.Redirect("/hangfire");
    }

    // The cookie carries a keyed digest, not the password itself.
    internal static string SessionToken(string password) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(password), "hangfire-dashboard-session"u8));

    internal static bool Matches(string? provided, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(provided ?? string.Empty)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    private static void WriteLoginPage(HttpContext httpContext, string? error = null)
    {
        httpContext.Response.ContentType = "text/html";
        httpContext.Response.StatusCode = 401;

        var errorHtml = error is not null
            ? $"<p style=\"color:red;\">{error}</p>"
            : "";

        var html = $"""
            <!DOCTYPE html>
            <html>
            <head><title>Hangfire Dashboard</title></head>
            <body style="font-family:sans-serif;display:flex;justify-content:center;align-items:center;height:100vh;margin:0;">
              <form method="post" action="{LoginPath}" style="text-align:center;">
                <h2>Hangfire Dashboard</h2>
                {errorHtml}
                <input type="password" name="password" placeholder="Password" required autocomplete="current-password"
                       style="padding:8px;font-size:16px;margin-right:8px;" />
                <button type="submit" style="padding:8px 16px;font-size:16px;">Login</button>
              </form>
            </body>
            </html>
            """;

        httpContext.Response.WriteAsync(html).GetAwaiter().GetResult();
    }
}
