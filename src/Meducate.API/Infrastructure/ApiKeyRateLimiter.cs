using System.Threading.RateLimiting;

namespace Meducate.API.Infrastructure;

// Per-key burst limit, applied only after the key's secret has been validated. Counting before
// authentication let anyone lock a key (notably the public demo key) out with requests carrying a wrong secret.
internal sealed class ApiKeyRateLimiter : IDisposable
{
    internal const int PermitsPerMinute = 60;

    private readonly PartitionedRateLimiter<Guid> _limiter = PartitionedRateLimiter.Create<Guid, Guid>(clientId =>
        RateLimitPartition.GetFixedWindowLimiter(clientId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = PermitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    internal bool TryAcquire(Guid clientId, out int retryAfterSeconds)
    {
        using var lease = _limiter.AttemptAcquire(clientId);
        retryAfterSeconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 60;
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
