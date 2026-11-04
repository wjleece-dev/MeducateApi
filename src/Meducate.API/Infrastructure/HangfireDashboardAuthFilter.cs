using System.Security.Cryptography;
using System.Text;
using Hangfire.Dashboard;

namespace Meducate.API.Infrastructure;

internal sealed class HangfireDashboardAuthFilter : IDashboardAuthorizationFilter
{
    private const string CookieName = "HangfireAuth";
    private const string PasswordKey = "Hangfire:DashboardPassword";

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expectedPassword = config[PasswordKey];

        if (string.IsNullOrEmpty(expectedPassword))
            return false;

        if (httpContext.Request.Cookies.TryGetValue(CookieName, out var cookieValue)
            && Matches(cookieValue, SessionToken(expectedPassword)))
            return true;

        // The password is posted, never put in the URL, so it stays out of access logs and history.
        if (httpContext.Request.Method == HttpMethods.Post && httpContext.Request.HasFormContentType)
        {
            var submitted = httpContext.Request.Form["password"].FirstOrDefault();
            if (Matches(submitted, expectedPassword))
            {
                httpContext.Response.Cookies.Append(CookieName, SessionToken(expectedPassword), new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Strict,
                    Path = "/hangfire",
                    MaxAge = TimeSpan.FromHours(12)
                });

                httpContext.Response.Redirect(httpContext.Request.Path.Value ?? "/hangfire");
                return true;
            }

            WriteLoginPage(httpContext, "Invalid password.");
            return false;
        }

        WriteLoginPage(httpContext);
        return false;
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
              <form method="post" style="text-align:center;">
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
