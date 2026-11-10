using System.Net.Mail;

namespace Meducate.Application.Helpers;

internal enum SubmissionTiming { Ok, TooFast, Invalid }

internal static class WaitlistHelpers
{
    private const int MaxEmailLength = 254;
    private const long MinFormAgeMs = 2_000;
    private static readonly long MaxFormAgeMs = (long)TimeSpan.FromDays(7).TotalMilliseconds;

    /// <summary>
    /// Trims + lowercases, and accepts only a plain "local@domain.tld" address
    /// (no display names, no whitespace, domain must contain a dot).
    /// </summary>
    internal static bool TryNormalizeEmail(string? raw, out string normalized)
    {
        normalized = "";
        var email = raw?.Trim();

        if (string.IsNullOrEmpty(email) || email.Length > MaxEmailLength)
            return false;

        if (email.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            return false;

        if (!MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            return false;

        var host = parsed.Host;
        if (!host.Contains('.') || host.StartsWith('.') || host.EndsWith('.'))
            return false;

        normalized = email.ToLowerInvariant();
        return true;
    }

    /// <summary>
    /// Checks the hidden render timestamp the form sends. Too fast (under 2s, or from the
    /// future) looks like a bot; missing, unparseable or over 7 days old means a stale or
    /// hand-built request.
    /// </summary>
    internal static SubmissionTiming CheckTiming(string? timestampMs, DateTimeOffset now)
    {
        if (!long.TryParse(timestampMs, out var renderedAt))
            return SubmissionTiming.Invalid;

        var age = now.ToUnixTimeMilliseconds() - renderedAt;

        if (age < MinFormAgeMs)
            return SubmissionTiming.TooFast;

        return age > MaxFormAgeMs ? SubmissionTiming.Invalid : SubmissionTiming.Ok;
    }
}
