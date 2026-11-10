using Meducate.Domain.Entities;
using Meducate.Domain.Services;
using Meducate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Meducate.Infrastructure.ApiKeys;

internal sealed class ApiKeyService(MeducateDbContext db) : IApiKeyService, IApiKeyUsageService
{
    private readonly MeducateDbContext _db = db;

    public async Task<(ApiClient client, string rawKey)?> CreateKeyForOrganisationAsync(Guid organisationId, string? name = null, int dailyLimit = 1000, CancellationToken ct = default)
    {
        var org = await _db.Organisations
            .FirstOrDefaultAsync(o => o.Id == organisationId, ct);

        if (org is null)
            return null;

        // For the first key (founding key), use org name if no name provided
        string keyName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            keyName = name;
        }
        else
        {
            var hasExistingKeys = await _db.ApiClients
                .AnyAsync(c => c.OrganisationId == organisationId && c.IsActive, ct);

            keyName = hasExistingKeys
                ? $"Key {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}"
                : org.Name;
        }

        var keyId = ApiKeyHasher.GenerateKeyId();
        var secret = ApiKeyHasher.GenerateSecret();
        var (hashed, salt) = ApiKeyHasher.HashSecret(secret);

        var client = new ApiClient
        {
            Id = Guid.NewGuid(),
            KeyId = keyId,
            Name = keyName,
            OrganisationId = org.Id,
            DailyLimit = dailyLimit,
            HashedSecret = hashed,
            Salt = salt,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _db.ApiClients.Add(client);
        await _db.SaveChangesAsync(ct);

        var rawKey = $"{keyId}.{secret}";
        return (client, rawKey);
    }

    public Task<ApiClient?> GetByKeyIdAsync(string keyId, CancellationToken ct = default) =>
        _db.ApiClients
            .AsNoTracking()
            .Include(a => a.Organisation)
            .FirstOrDefaultAsync(a => a.KeyId == keyId, ct);

    public Task<bool> ValidateSecretAsync(ApiClient client, string secret) =>
        Task.FromResult(ApiKeyHasher.Verify(secret, client.HashedSecret, client.Salt));

    public async Task DisableKeyAsync(Guid clientId, CancellationToken ct = default)
    {
        await _db.ApiClients
            .Where(c => c.Id == clientId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false), ct);
    }

    public Task<int> GetActiveKeyCountAsync(Guid organisationId, CancellationToken ct = default) =>
        _db.ApiClients.CountAsync(c =>
            c.OrganisationId == organisationId
            && c.IsActive
            && (!c.ExpiresAt.HasValue || c.ExpiresAt > DateTime.UtcNow), ct);

    public Task<List<ApiClient>> GetActiveKeysForOrgAsync(Guid organisationId, CancellationToken ct = default) =>
        _db.ApiClients
            .Where(c => c.OrganisationId == organisationId
                && c.IsActive
                && (!c.ExpiresAt.HasValue || c.ExpiresAt > DateTime.UtcNow))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

    public Task<ApiClient?> GetActiveKeyForOrgAsync(Guid keyId, Guid organisationId, CancellationToken ct = default) =>
        _db.ApiClients
            .FirstOrDefaultAsync(c => c.Id == keyId
                && c.OrganisationId == organisationId
                && c.IsActive
                && (!c.ExpiresAt.HasValue || c.ExpiresAt > DateTime.UtcNow), ct);

    public async Task<Dictionary<Guid, int>> GetTodayUsageCountsAsync(List<Guid> clientIds, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;

        var counts = await _db.ApiUsageLogs
            .Where(u => clientIds.Contains(u.ClientId) && u.Timestamp >= today)
            .GroupBy(u => u.ClientId)
            .Select(g => new { ClientId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return counts.ToDictionary(x => x.ClientId, x => x.Count);
    }

    public async Task<bool> RenameKeyAsync(Guid clientId, string name, CancellationToken ct = default)
    {
        var client = await _db.ApiClients.FindAsync([clientId], ct);
        if (client is null || !client.IsActive)
            return false;

        client.Name = name;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> GetTodayUsageCountAsync(Guid clientId, CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;
        return await _db.ApiUsageLogs
            .Where(u => u.ClientId == clientId && u.Timestamp >= today)
            .CountAsync(ct);
    }

    public async Task LogUsageAsync(Guid clientId, string? endpoint, string? method, int statusCode, string? organisationName, string? queryString, CancellationToken ct = default)
    {
        var log = new ApiUsageLog
        {
            ClientId = clientId,
            Timestamp = DateTime.UtcNow,
            Endpoint = endpoint,
            Method = method,
            StatusCode = statusCode,
            OrganisationName = organisationName,
            QueryString = queryString
        };

        _db.ApiUsageLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        // Update LastUsedAt without loading the entity (single UPDATE statement)
        await _db.ApiClients
            .Where(c => c.Id == clientId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastUsedAt, DateTime.UtcNow), ct);
    }

    public async Task<string?> GetOrganisationOwnerEmailAsync(Guid organisationId, CancellationToken ct = default)
    {
        var org = await _db.Organisations
            .AsNoTracking()
            .Include(o => o.User)
            .FirstOrDefaultAsync(o => o.Id == organisationId, ct);

        return org?.User?.Email;
    }

    public Task<bool> HasActiveKeysAsync(Guid organisationId, CancellationToken ct = default) =>
        _db.ApiClients.AnyAsync(c => c.OrganisationId == organisationId && c.IsActive, ct);

    public async Task<Dictionary<DateOnly, int>> GetUsageHistoryAsync(List<Guid> clientIds, int days = 7, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-(days - 1));

        var counts = await _db.ApiUsageLogs
            .Where(u => clientIds.Contains(u.ClientId) && u.Timestamp >= cutoff)
            .GroupBy(u => u.Timestamp.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var lookup = counts.ToDictionary(x => DateOnly.FromDateTime(x.Date), x => x.Count);

        // Fill missing days with 0
        var result = new Dictionary<DateOnly, int>();
        for (var i = 0; i < days; i++)
        {
            var day = DateOnly.FromDateTime(cutoff.AddDays(i));
            result[day] = lookup.GetValueOrDefault(day, 0);
        }

        return result;
    }

    public async Task<List<(string Endpoint, int Count)>> GetTopEndpointsAsync(List<Guid> clientIds, int days = 7, int topN = 5, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.Date.AddDays(-(days - 1));

        var results = await _db.ApiUsageLogs
            .Where(u => clientIds.Contains(u.ClientId) && u.Timestamp >= cutoff && u.Endpoint != null)
            .GroupBy(u => u.Endpoint!)
            .Select(g => new { Endpoint = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(topN)
            .ToListAsync(ct);

        return results.Select(x => (x.Endpoint, x.Count)).ToList();
    }

    public async Task DeleteUserAccountAsync(Guid userId, CancellationToken ct = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        var org = await _db.Organisations
            .FirstOrDefaultAsync(o => o.UserId == userId, ct);

        if (org is not null)
        {
            var apiKeyIds = await _db.ApiClients
                .Where(c => c.OrganisationId == org.Id)
                .Select(c => c.Id)
                .ToListAsync(ct);

            // Disable keys first so ApiKeyMiddleware rejects new requests
            await _db.ApiClients
                .Where(c => c.OrganisationId == org.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false), ct);

            if (apiKeyIds.Count > 0)
            {
                await _db.ApiUsageLogs
                    .Where(u => apiKeyIds.Contains(u.ClientId))
                    .ExecuteDeleteAsync(ct);
            }

            await _db.ApiClients
                .Where(c => c.OrganisationId == org.Id)
                .ExecuteDeleteAsync(ct);

            _db.Organisations.Remove(org);
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is not null)
            _db.Users.Remove(user);

        await _db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
