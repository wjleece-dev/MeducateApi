using Meducate.Application.DTOs;
using Meducate.Domain.Repositories;
using Meducate.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using static Meducate.API.Endpoints.EndpointHelpers;

namespace Meducate.API.Endpoints;

internal static class OrgEndpoints
{
    internal static IEndpointRouteBuilder MapOrgEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/orgs", [Authorize] async (CreateOrganisationRequest request, IUserRepository userRepo, IOrganisationRepository orgRepo, HttpContext http, CancellationToken ct) =>
        {
            var (user, error) = await GetVerifiedUserAsync(http, userRepo, ct);
            if (error is not null) return error;

            var existing = await orgRepo.GetByUserIdAsync(user!.Id, ct);
            if (existing != null)
                return Results.Problem(
                    detail: "An organisation already exists for this account.",
                    statusCode: StatusCodes.Status400BadRequest);

            var org = await orgRepo.CreateAsync(request.OrganisationName, user.Id, ct);

            return Results.Created($"/api/v1/orgs/{org.Id}", org.Id);
        })
        .WithName("CreateOrganisation")
        .WithSummary("Create an organisation")
        .Produces(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .WithTags("Organisations");

        app.MapPost("/orgs/{id:guid}/keys", [Authorize] async (Guid id, CreateApiKeyRequest? request, IOrganisationRepository orgRepo, IUserRepository userRepo, IApiKeyService keys, HttpContext http, CancellationToken ct) =>
        {
            var (user, userError) = await GetVerifiedUserAsync(http, userRepo, ct);
            if (userError is not null) return userError;

            var (org, orgError) = await GetOwnedOrgAsync(id, user!, orgRepo, ct);
            if (orgError is not null) return orgError;

            if (request?.Name is not null && (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > ApiConstants.MaxKeyNameLength))
                return Results.Problem(
                    detail: $"Key name must be between 1 and {ApiConstants.MaxKeyNameLength} characters.",
                    statusCode: StatusCodes.Status400BadRequest);

            var activeKeyCount = await keys.GetActiveKeyCountAsync(id, ct);
            if (activeKeyCount >= ApiConstants.MaxKeysPerOrg)
            {
                return Results.Problem(
                    detail: $"Maximum of {ApiConstants.MaxKeysPerOrg} active API keys per organisation.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await keys.CreateKeyForOrganisationAsync(id, request?.Name, ct: ct);

            if (result is null)
            {
                return Results.Problem(
                    detail: "Organisation not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            var (client, rawKey) = result.Value;

            return Results.Created(
                $"/api/v1/orgs/{id}/keys/{client.KeyId}",
                new ApiKeyResult(
                    client.Id,
                    client.KeyId,
                    client.OrganisationId,
                    rawKey));
        })
        .WithName("CreateApiKey")
        .WithSummary("Create an API key")
        .Produces<ApiKeyResult>(StatusCodes.Status201Created)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithTags("Organisations");

        app.MapGet("/orgs/{id:guid}/keys", [Authorize] async (Guid id, IOrganisationRepository orgRepo, IUserRepository userRepo, IApiKeyService keys, HttpContext http, CancellationToken ct) =>
        {
            var (user, userError) = await GetVerifiedUserAsync(http, userRepo, ct);
            if (userError is not null) return userError;

            var (org, orgError) = await GetOwnedOrgAsync(id, user!, orgRepo, ct);
            if (orgError is not null) return orgError;

            var activeKeys = await keys.GetActiveKeysForOrgAsync(id, ct);
            var usageCounts = await keys.GetTodayUsageCountsAsync([.. activeKeys.Select(k => k.Id)], ct);

            var result = activeKeys.Select(k => new
            {
                k.Id,
                k.KeyId,
                k.Name,
                k.CreatedAt,
                k.LastUsedAt,
                k.ExpiresAt,
                k.DailyLimit,
                UsageToday = usageCounts.GetValueOrDefault(k.Id, 0)
            });

            return Results.Ok(result);
        })
        .WithName("ListApiKeys")
        .WithSummary("List API keys")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .WithTags("Organisations");

        app.MapPatch("/orgs/{orgId:guid}/keys/{keyId:guid}", [Authorize] async (Guid orgId, Guid keyId, UpdateApiKeyRequest request, IOrganisationRepository orgRepo, IUserRepository userRepo, IApiKeyService keys, HttpContext http, CancellationToken ct) =>
        {
            var (user, userError) = await GetVerifiedUserAsync(http, userRepo, ct);
            if (userError is not null) return userError;

            var (org, orgError) = await GetOwnedOrgAsync(orgId, user!, orgRepo, ct);
            if (orgError is not null) return orgError;

            if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > ApiConstants.MaxKeyNameLength)
                return Results.Problem(
                    detail: $"Key name must be between 1 and {ApiConstants.MaxKeyNameLength} characters.",
                    statusCode: StatusCodes.Status400BadRequest);

            var keyToRename = await keys.GetActiveKeyForOrgAsync(keyId, orgId, ct);

            if (keyToRename is null)
                return Results.Problem(
                    detail: "API key not found.",
                    statusCode: StatusCodes.Status404NotFound);

            await keys.RenameKeyAsync(keyId, request.Name, ct);

            return Results.NoContent();
        })
        .WithName("RenameApiKey")
        .WithSummary("Rename an API key")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithTags("Organisations");

        app.MapGet("/orgs/{id:guid}/usage/history", [Authorize] async (Guid id, IOrganisationRepository orgRepo, IUserRepository userRepo, IApiKeyService keys, HttpContext http, CancellationToken ct) =>
        {
            var (user, userError) = await GetVerifiedUserAsync(http, userRepo, ct);
            if (userError is not null) return userError;

            var (org, orgError) = await GetOwnedOrgAsync(id, user!, orgRepo, ct);
            if (orgError is not null) return orgError;

            var activeKeys = await keys.GetActiveKeysForOrgAsync(id, ct);
            var history = await keys.GetUsageHistoryAsync([.. activeKeys.Select(k => k.Id)], ct: ct);

            var result = history
                .OrderBy(h => h.Key)
                .Select(h => new { date = h.Key.ToString("yyyy-MM-dd"), count = h.Value });

            return Results.Ok(result);
        })
        .WithName("GetUsageHistory")
        .WithSummary("Get usage history")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .WithTags("Organisations");

        app.MapGet("/orgs/{id:guid}/usage/top-endpoints", [Authorize] async (Guid id, IOrganisationRepository orgRepo, IUserRepository userRepo, IApiKeyService keys, HttpContext http, CancellationToken ct) =>
        {
            var (user, userError) = await GetVerifiedUserAsync(http, userRepo, ct);
            if (userError is not null) return userError;

            var (org, orgError) = await GetOwnedOrgAsync(id, user!, orgRepo, ct);
            if (orgError is not null) return orgError;

            var activeKeys = await keys.GetActiveKeysForOrgAsync(id, ct);
            var topEndpoints = await keys.GetTopEndpointsAsync([.. activeKeys.Select(k => k.Id)], ct: ct);

            var result = topEndpoints.Select(e => new { endpoint = e.Endpoint, count = e.Count });

            return Results.Ok(result);
        })
        .WithName("GetTopEndpoints")
        .WithSummary("Get top endpoints")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .WithTags("Organisations");

        app.MapDelete("/orgs/{orgId:guid}/keys/{keyId:guid}", [Authorize] async (Guid orgId, Guid keyId, IOrganisationRepository orgRepo, IUserRepository userRepo, IApiKeyService keys, HttpContext http, CancellationToken ct) =>
        {
            var (user, userError) = await GetVerifiedUserAsync(http, userRepo, ct);
            if (userError is not null) return userError;

            var (org, orgError) = await GetOwnedOrgAsync(orgId, user!, orgRepo, ct);
            if (orgError is not null) return orgError;

            var keyToRevoke = await keys.GetActiveKeyForOrgAsync(keyId, orgId, ct);

            if (keyToRevoke is null)
            {
                return Results.Problem(
                    detail: "API key not found.",
                    statusCode: StatusCodes.Status404NotFound);
            }

            await keys.DisableKeyAsync(keyId, ct);

            return Results.NoContent();
        })
        .WithName("DeleteApiKey")
        .WithSummary("Delete an API key")
        .Produces(StatusCodes.Status204NoContent)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithTags("Organisations");

        return app;
    }
}
