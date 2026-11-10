using System.Threading.RateLimiting;

namespace Meducate.API.Infrastructure;

// Per-key burst limit, applied only after the key's secret has been validated. Counting before
// authentication let anyone lock a key (notably the public demo key) out with requests carrying a wrong secret.
internal sealed class ApiKeyRateLimiter : IDisposable
{
    internal const int PermitsPerMinute = 60;

    // The public demo key is shared by every visitor, so it gets a much higher burst limit than a private key.
    internal const int DemoPermitsPerMinute = 600;

    private readonly PartitionedRateLimiter<(Guid ClientId, int Permits)> _limiter =
        PartitionedRateLimiter.Create<(Guid ClientId, int Permits), (Guid, int)>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, k => new FixedWindowRateLimiterOptions
            {
                PermitLimit = k.Item2,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

    // The shared demo key is also limited per client address, so one developer cannot use up everyone's allowance.
    internal const int DemoPermitsPerAddressPerMinute = 60;
    internal const int DemoPermitsPerAddressPerDay = 300;

    private readonly PartitionedRateLimiter<string> _demoMinute = PartitionedRateLimiter.Create<string, string>(address =>
        RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = DemoPermitsPerAddressPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    private readonly PartitionedRateLimiter<string> _demoDay = PartitionedRateLimiter.Create<string, string>(address =>
        RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = DemoPermitsPerAddressPerDay,
            Window = TimeSpan.FromHours(24),
            QueueLimit = 0,
        }));

    internal bool TryAcquireDemoAddress(string address, out int retryAfterSeconds, out string reason)
    {
        reason = "";
        if (!TryAcquire(_demoMinute, address, out retryAfterSeconds))
        {
            reason = $"The shared demo key allows {DemoPermitsPerAddressPerMinute} requests per minute per address. Retry in {retryAfterSeconds} seconds, or create a free account for your own key.";
            return false;
        }

        if (!TryAcquire(_demoDay, address, out retryAfterSeconds))
        {
            reason = $"The shared demo key allows {DemoPermitsPerAddressPerDay} requests per day per address. Create a free account for your own key (1,000 per day).";
            return false;
        }

        return true;
    }

    private static bool TryAcquire(PartitionedRateLimiter<string> limiter, string address, out int retryAfterSeconds)
    {
        using var lease = limiter.AttemptAcquire(address);
        retryAfterSeconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 60;
        return lease.IsAcquired;
    }

    internal bool TryAcquire(Guid clientId, int permitsPerMinute, out int retryAfterSeconds)
    {
        using var lease = _limiter.AttemptAcquire((clientId, permitsPerMinute));
        retryAfterSeconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
            : 60;
        return lease.IsAcquired;
    }

    public void Dispose()
    {
        _limiter.Dispose();
        _demoMinute.Dispose();
        _demoDay.Dispose();
    }
}
