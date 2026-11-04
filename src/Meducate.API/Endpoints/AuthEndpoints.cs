using Meducate.Infrastructure.Auth;
using System.Security.Claims;
using Meducate.Application.DTOs;
using Meducate.Domain.Entities;
using Meducate.Domain.Enums;
using Meducate.Domain.Repositories;
using Meducate.Domain.Services;
using Meducate.Infrastructure.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

namespace Meducate.API.Endpoints;

internal static class AuthEndpoints
{
    internal static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/logout", [Authorize] async (HttpContext http) =>
        {
            await http.SignOutAsync("MeducateAPIAuth");
            return Results.NoContent();
        })
        .WithName("Logout")
        .WithSummary("Sign out")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .WithTags("Auth");

        app.MapPost("/users/register", [AllowAnonymous] async (RegisterRequest request, IUserRepository users, IEmailService emailSvc, VerificationLinkBuilder linkBuilder, ILogger<Program> logger, CancellationToken ct) =>
        {
            // Honeypot: reject if hidden field was filled (bots fill all fields)
            if (!string.IsNullOrEmpty(request.Website))
            {
                logger.LogWarning("Bot detected: honeypot field filled for {Email}", request.Email);
                return Results.Ok(new { message = "If an account exists for this email, we\u2019ve sent a verification link." });
            }

            // Time-based honeypot: reject if submitted in under 2 seconds
            if (!string.IsNullOrEmpty(request.Timestamp)
                && long.TryParse(request.Timestamp, out var renderTicks)
                && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - renderTicks < 2000)
            {
                logger.LogWarning("Bot detected: form submitted too quickly for {Email}", request.Email);
                return Results.Ok(new { message = "If an account exists for this email, we\u2019ve sent a verification link." });
            }

            var isSignIn = string.Equals(request.Mode, "signin", StringComparison.OrdinalIgnoreCase);

            if (!isSignIn && !request.AcceptedTerms)
            {
                return Results.Problem(
                    detail: "You must accept the Privacy Policy and Terms of Service to create an account.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            User user;
            if (isSignIn)
            {
                var existing = await users.GetByEmailAsync(request.Email, ct);
                if (existing is null)
                {
                    // Same answer as for a known address, so this form cannot be used to probe which emails have accounts.
                    return Results.Ok(new { message = "If an account exists for this email, we’ve sent a verification link." });
                }
                user = existing;
            }
            else
            {
                user = await users.GetOrCreateAsync(request.Email, ct);

                if (request.AcceptedTerms && request.TermsVersion is not null)
                {
                    user.AcceptedTermsAt = DateTime.UtcNow;
                    user.AcceptedTermsVersion = request.TermsVersion;
                    await users.SaveChangesAsync(ct);
                }
            }

            EmailResult emailResult;
            try
            {
                if (!user.IsEmailVerified)
                {
                    var verifyUrl = linkBuilder.Build(user.VerificationToken!);
                    emailResult = await emailSvc.SendVerificationEmailAsync(user.Email, verifyUrl);
                }
                else
                {
                    user.RotateVerificationToken();
                    await users.SaveChangesAsync(ct);

                    var loginUrl = linkBuilder.Build(user.VerificationToken!);
                    emailResult = await emailSvc.SendLoginEmailAsync(user.Email, loginUrl);
                }
            }
            catch (Resend.ResendException ex)
            {
                logger.LogError(ex, "Resend email failed (HTTP {StatusCode}, {ErrorType}): {ErrorMessage}",
                    ex.StatusCode, ex.ErrorType, ex.Message);
                return Results.Problem(
                    detail: "Email delivery failed. Please try again later.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send email for registration");
                return Results.Problem(
                    detail: "Something went wrong sending your email. Please try again.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            if (!emailResult.Sent && emailResult.RetryAfter is not null)
            {
                return Results.Problem(
                    detail: $"Too many email requests. Please try again in {EmailService.FormatRetryAfter(emailResult.RetryAfter.Value)}.",
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            return Results.Ok(new { message = "If an account exists for this email, we\u2019ve sent a verification link." });
        })
        .WithName("Register")
        .WithSummary("Register or sign in")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status502BadGateway)
        .WithTags("Auth");

        app.MapPost("/users/verify", [AllowAnonymous] async (VerifyUserRequest request, IUserRepository users, IEmailService emailSvc, VerificationLinkBuilder urlBuilder, HttpContext http, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                return Results.Problem(
                    detail: "This verification link is invalid.",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["verifyResult"] = VerifyUserResult.Invalid.ToString() });
            }

            var user = await users.GetByVerificationTokenAsync(request.Token, ct);

            if (user is not null)
            {
                if (user.IsEmailVerified)
                {
                    await Services.AuthSignIn.SignInAsync(http, user);

                    user.VerificationToken = null;
                    user.VerificationTokenExpiresAt = null;
                    await users.SaveChangesAsync(ct);

                    return Results.Ok(new { result = VerifyUserResult.AlreadyVerified.ToString(), message = "Your email is already verified." });
                }

                await users.VerifyAsync(user, ct);

                await Services.AuthSignIn.SignInAsync(http, user);

                return Results.Ok(new { result = VerifyUserResult.Success.ToString(), message = "Your email has been verified." });
            }

            var expiredUser = await users.GetByVerificationTokenIncludingExpiredAsync(request.Token, ct);

            if (expiredUser is not null && !expiredUser.IsEmailVerified)
            {
                expiredUser.RotateVerificationToken();
                await users.SaveChangesAsync(ct);

                var verifyUrl = urlBuilder.Build(expiredUser.VerificationToken!);
                var resendResult = await emailSvc.SendVerificationEmailAsync(expiredUser.Email, verifyUrl);

                if (!resendResult.Sent && resendResult.RetryAfter is not null)
                {
                    return Results.Problem(
                        detail: $"This verification link has expired. Please try again in {EmailService.FormatRetryAfter(resendResult.RetryAfter.Value)}.",
                        statusCode: StatusCodes.Status429TooManyRequests,
                        extensions: new Dictionary<string, object?> { ["verifyResult"] = VerifyUserResult.Expired.ToString() });
                }

                return Results.Problem(
                    detail: "This verification link has expired. We\u2019ve sent you a new one.",
                    statusCode: StatusCodes.Status400BadRequest,
                    extensions: new Dictionary<string, object?> { ["verifyResult"] = VerifyUserResult.Expired.ToString() });
            }

            return Results.Problem(
                detail: "This verification link is invalid.",
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?> { ["verifyResult"] = VerifyUserResult.Invalid.ToString() });
        })
        .WithName("VerifyEmail")
        .WithSummary("Verify email token")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .WithTags("Auth");

        app.MapGet("/users/me", [Authorize] async (IUserRepository users, IOrganisationRepository orgs, IApiKeyService apiKeys, HttpContext http, CancellationToken ct) =>
        {
            var rawId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (rawId is null || !Guid.TryParse(rawId, out var userId))
            {
                return Results.Problem(
                    detail: "Invalid session.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await users.GetByIdAsync(userId, ct);

            if (user is null)
            {
                return Results.Problem(
                    detail: "User not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var org = await orgs.GetByUserIdAsync(user.Id, ct);

            var hasApiKeys = org is not null
                && await apiKeys.HasActiveKeysAsync(org.Id, ct);

            return Results.Ok(new { user.Email, user.IsEmailVerified, OrganisationId = org?.Id, OrganisationName = org?.Name, HasApiKeys = hasApiKeys });
        })
        .WithName("GetCurrentUser")
        .WithSummary("Get current user")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithTags("Auth");

        app.MapDelete("/users/me", [Authorize] async (IUserRepository users, IApiKeyUsageService apiKeys, HttpContext http, CancellationToken ct) =>
        {
            // Require a fresh session (signed in within last 10 minutes) for destructive actions
            var authTimeClaim = http.User.FindFirstValue("auth_time");
            if (authTimeClaim is null
                || !long.TryParse(authTimeClaim, out var authTimeUnix)
                || DateTimeOffset.UtcNow.ToUnixTimeSeconds() - authTimeUnix > ApiConstants.FreshAuthWindowSeconds)
            {
                return Results.Problem(
                    detail: "Please sign in again before deleting your account.",
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var rawId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (rawId is null || !Guid.TryParse(rawId, out var userId))
            {
                return Results.Problem(
                    detail: "Invalid session.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var user = await users.GetByIdAsync(userId, ct);

            if (user is null)
            {
                return Results.Problem(
                    detail: "User not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            await apiKeys.DeleteUserAccountAsync(userId, ct);

            // Sign out
            await http.SignOutAsync("MeducateAPIAuth");

            return Results.NoContent();
        })
        .WithName("DeleteAccount")
        .WithSummary("Delete user account")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status403Forbidden)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithTags("Auth");

        return app;
    }
}
