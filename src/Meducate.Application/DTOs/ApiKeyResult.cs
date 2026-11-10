namespace Meducate.Application.DTOs;

internal sealed record ApiKeyResult(Guid Id, string KeyId, Guid OrganisationId, string ApiKey);
