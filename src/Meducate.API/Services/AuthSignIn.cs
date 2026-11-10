using System.Security.Claims;
using Meducate.Domain.Entities;
using Microsoft.AspNetCore.Authentication;

namespace Meducate.API.Services;

internal static class AuthSignIn
{
    internal static Task SignInAsync(HttpContext http, User user) =>
        http.SignInAsync("MeducateAPIAuth", new ClaimsPrincipal(
            new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("auth_time", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
                new Claim("security_stamp", user.SecurityStamp)
            ], "MeducateAPIAuth")));
}
