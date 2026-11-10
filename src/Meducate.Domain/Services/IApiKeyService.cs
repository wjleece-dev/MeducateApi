using Meducate.Domain.Entities;

namespace Meducate.Domain.Services;

internal interface IApiKeyService
{
    Task<(ApiClient client, string rawKey)?> CreateKeyForOrganisationAsync(Guid organisationId, string? name = null, int dailyLimit = 1000, CancellationToken ct = default);
    Task<ApiClient?> GetByKeyIdAsync(string keyId, CancellationToken ct = default);
    Task<bool> ValidateSecretAsync(ApiClient client, string secret);
    Task DisableKeyAsync(Guid clientId, CancellationToken ct = default);
    Task<int> GetActiveKeyCountAsync(Guid organisationId, CancellationToken ct = default);
    Task<List<ApiClient>> GetActiveKeysForOrgAsync(Guid organisationId, CancellationToken ct = default);
    Task<ApiClient?> GetActiveKeyForOrgAsync(Guid keyId, Guid organisationId, CancellationToken ct = default);
    Task<Dictionary<Guid, int>> GetTodayUsageCountsAsync(List<Guid> clientIds, CancellationToken ct = default);
    Task<bool> RenameKeyAsync(Guid clientId, string name, CancellationToken ct = default);
    Task<int> GetTodayUsageCountAsync(Guid clientId, CancellationToken ct = default);
    Task<bool> HasActiveKeysAsync(Guid organisationId, CancellationToken ct = default);
    Task<Dictionary<DateOnly, int>> GetUsageHistoryAsync(List<Guid> clientIds, int days = 7, CancellationToken ct = default);
    Task<List<(string Endpoint, int Count)>> GetTopEndpointsAsync(List<Guid> clientIds, int days = 7, int topN = 5, CancellationToken ct = default);
}
