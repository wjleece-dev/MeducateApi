using System.Security.Claims;
using Meducate.Domain.Entities;
using Meducate.Domain.Repositories;

namespace Meducate.API.Endpoints;

internal static class EndpointHelpers
{
    internal static async Task<(User? User, IResult? Error)> GetVerifiedUserAsync(
        HttpContext http, IUserRepository userRepo, CancellationToken ct = default)
    {
        var rawId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (rawId is null || !Guid.TryParse(rawId, out var userId))
        {
            return (null, Results.Problem(
                detail: "Invalid session.",
                statusCode: StatusCodes.Status401Unauthorized));
        }

        var user = await userRepo.GetByIdAsync(userId, ct);

        if (user is null || !user.IsEmailVerified)
        {
            return (null, Results.Problem(
                detail: "You must have a verified email to perform this action.",
                statusCode: StatusCodes.Status403Forbidden));
        }

        return (user, null);
    }

    internal static async Task<(Organisation? Org, IResult? Error)> GetOwnedOrgAsync(
        Guid orgId, User user, IOrganisationRepository orgRepo, CancellationToken ct = default)
    {
        var org = await orgRepo.GetByIdAsync(orgId, ct);

        if (org is null || org.UserId != user.Id)
        {
            return (null, Results.Problem(
                detail: "Organisation not found.",
                statusCode: StatusCodes.Status404NotFound));
        }

        return (org, null);
    }
}
