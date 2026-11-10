using System.Security.Claims;
using System.Security.Cryptography;
using Meducate.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Meducate.API.Infrastructure;

internal static class AuthenticationSetup
{
    internal static IServiceCollection AddMeducateAuth(this IServiceCollection services)
    {
        services.AddAuthentication("MeducateAPIAuth")
            .AddCookie("MeducateAPIAuth", o =>
            {
                o.Cookie.Name = "meducateapi_auth";
                o.Cookie.Path = "/";
                o.Cookie.HttpOnly = true;
                o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                o.Cookie.SameSite = SameSiteMode.Strict;

                o.LoginPath = "/register";
                o.AccessDeniedPath = "/register";

                // 3 days idle (was 8h, which logged people out overnight); the 7-day absolute cap
                // (ApiConstants.MaxAuthAgeSeconds) still bounds total session age.
                o.ExpireTimeSpan = TimeSpan.FromDays(3);
                o.Cookie.MaxAge = TimeSpan.FromDays(7);
                o.SlidingExpiration = true;

                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments("/api"))
                        {
                            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            return Task.CompletedTask;
                        }

                        // LoginPath is a PathString and cannot carry a query, so target sign-in mode here.
                        ctx.Response.Redirect("/register?mode=signin");
                        return Task.CompletedTask;
                    },

                    OnRedirectToAccessDenied = ctx =>
                    {
                        if (ctx.Request.Path.StartsWithSegments("/api"))
                        {
                            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                            return Task.CompletedTask;
                        }

                        ctx.Response.Redirect("/register?mode=signin");
                        return Task.CompletedTask;
                    },

                    OnValidatePrincipal = async ctx =>
                    {
                        var authTimeClaim = ctx.Principal?.FindFirstValue("auth_time");
                        if (!IsAuthTimeValid(authTimeClaim, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                        {
                            ctx.RejectPrincipal();
                            await ctx.HttpContext.SignOutAsync("MeducateAPIAuth");
                            return;
                        }

                        var stampClaim = ctx.Principal?.FindFirstValue("security_stamp");
                        if (stampClaim is null)
                        {
                            ctx.RejectPrincipal();
                            await ctx.HttpContext.SignOutAsync("MeducateAPIAuth");
                            return;
                        }

                        var lastCheck = ctx.Properties.Items.TryGetValue("last_stamp_check", out var ts)
                            ? ts
                            : null;

                        if (!IsStampRecheckDue(lastCheck, DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                            return;

                        var userId = ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                        if (userId is null || !Guid.TryParse(userId, out var uid))
                        {
                            ctx.RejectPrincipal();
                            await ctx.HttpContext.SignOutAsync("MeducateAPIAuth");
                            return;
                        }

                        var db = ctx.HttpContext.RequestServices.GetRequiredService<MeducateDbContext>();
                        var stamp = await db.Users
                            .Where(u => u.Id == uid)
                            .Select(u => new { u.SecurityStamp })
                            .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);

                        if (stamp is null || !StampsMatch(stamp.SecurityStamp, stampClaim))
                        {
                            ctx.RejectPrincipal();
                            await ctx.HttpContext.SignOutAsync("MeducateAPIAuth");
                            return;
                        }

                        ctx.Properties.Items["last_stamp_check"] =
                            DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
                        ctx.ShouldRenew = true;
                    }
                };
            });

        services.AddAuthorization();
        services.AddValidation();

        return services;
    }

    internal static bool IsAuthTimeValid(string? authTimeClaim, long nowUnixSeconds) =>
        authTimeClaim is not null
        && long.TryParse(authTimeClaim, out var authTimeUnix)
        && nowUnixSeconds - authTimeUnix <= ApiConstants.MaxAuthAgeSeconds;

    internal static bool IsStampRecheckDue(string? lastStampCheck, long nowUnixSeconds)
    {
        var lastCheck = lastStampCheck is not null && long.TryParse(lastStampCheck, out var lastUnix)
            ? lastUnix
            : 0L;

        return nowUnixSeconds - lastCheck >= ApiConstants.StampCheckIntervalSeconds;
    }

    internal static bool StampsMatch(string storedStamp, string claimStamp) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(storedStamp),
            System.Text.Encoding.UTF8.GetBytes(claimStamp));
}
