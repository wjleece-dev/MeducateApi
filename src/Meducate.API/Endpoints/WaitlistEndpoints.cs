using Meducate.Application.Helpers;
using Meducate.Domain.Repositories;
using Meducate.Domain.Services;
using Microsoft.AspNetCore.Authorization;

namespace Meducate.API.Endpoints;

internal static class WaitlistEndpoints
{
    // Website and Timestamp are the same hidden-field honeypot pair the registration form uses.
    private sealed record WaitlistRequest(string Email, string? Website = null, string? Timestamp = null);

    private const string JoinedMessage = "You're on the list. We'll be in touch when Pro launches.";

    // ponytail: one overall cap, not per-sender, so a botnet of distinct IPs still can't flood
    // storage or the admin inbox. Website signups share a single sender IP at the API (the web
    // app calls it privately), so the per-IP limit is effectively site-wide for them too --
    // forward the client IP from the web app if real signup volume ever approaches these limits.
    private const int MaxSignupsPerHour = 20;

    internal static IEndpointRouteBuilder MapWaitlistEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/waitlist", [AllowAnonymous] async (
            WaitlistRequest request,
            IWaitlistRepository waitlist,
            IEmailService emailSvc,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            // Bots fill every field, including the one humans can't see. Answer exactly as if
            // it worked so they learn nothing.
            if (!string.IsNullOrEmpty(request.Website))
            {
                logger.LogWarning("Waitlist bot detected: honeypot field filled");
                return Results.Ok(new { message = JoinedMessage });
            }

            if (!WaitlistHelpers.TryNormalizeEmail(request.Email, out var email))
                return Results.Problem("A valid email address is required.", statusCode: StatusCodes.Status400BadRequest);

            switch (WaitlistHelpers.CheckTiming(request.Timestamp, DateTimeOffset.UtcNow))
            {
                case SubmissionTiming.TooFast:
                    logger.LogWarning("Waitlist bot detected: form submitted too quickly");
                    return Results.Ok(new { message = JoinedMessage });

                case SubmissionTiming.Invalid:
                    return Results.Problem("Please reload the page and try again.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (await waitlist.CountSinceAsync(DateTime.UtcNow.AddHours(-1), ct) >= MaxSignupsPerHour)
                return Results.Problem("Too many signups right now. Please try again later.", statusCode: StatusCodes.Status429TooManyRequests);

            // Already listed: same answer, no second notification, nothing revealed.
            if (!await waitlist.TryAddAsync(email, ct))
                return Results.Ok(new { message = JoinedMessage });

            // The signup is saved, so a failed notification must not turn into an error (and
            // must not lose the lead, which is what used to happen).
            try
            {
                await emailSvc.SendWaitlistNotificationAsync(email);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Waitlist signup saved but admin notification failed");
            }

            return Results.Ok(new { message = JoinedMessage });
        })
        .RequireRateLimiting(ApiConstants.WaitlistRateLimitPolicy)
        .WithName("JoinWaitlist")
        .WithSummary("Join Pro waitlist")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status429TooManyRequests)
        .WithTags("Waitlist");

        return app;
    }
}
