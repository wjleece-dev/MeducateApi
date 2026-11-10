using System.Net;
using Meducate.Domain.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Resend;

namespace Meducate.Infrastructure.Email;

internal sealed class EmailService(IResend resend, IMemoryCache cache, ILogger<EmailService> logger, IConfiguration config) : IEmailService
{
    private readonly IResend _resend = resend;
    private readonly IMemoryCache _cache = cache;
    private readonly ILogger<EmailService> _logger = logger;
    private readonly IConfiguration _config = config;
    private readonly string _fromAddress = config["Resend:FromAddress"] ?? "no-reply@meducateapi.com";

    private const int MaxRetries = 3;
    private const int MaxEmailsPerRecipient = 3;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(15);
    private static readonly Lock _rateLimitLock = new();
    private const string ButtonStyle = "display:inline-block;padding:10px 16px;background:#0d6efd;color:#ffffff;text-decoration:none;border-radius:4px;";

    internal static string MaskEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return "***@***";
        return $"{email[0]}***@{email[(atIndex + 1)..]}";
    }

    private static string BuildEmailBody(string bodyHtml)
    {
        return $"""
            <p>Hello,</p>

            {bodyHtml}

            <p>If you didn't request this, you can safely ignore this email.</p>

            <p style="margin-top:24px;font-size:12px;color:#666;">MeducateAPI is intended for educational and informational purposes only.</p>
            """;
    }

    private static string BuildPlainTextBody(string bodyText)
    {
        return $"""
            Hello,

            {bodyText}

            If you didn't request this, you can safely ignore this email.

            MeducateAPI is intended for educational and informational purposes only.
            """;
    }

    private static string BuildButtonBlock(string url, string label)
    {
        return $"""
            <p>
                <a href="{url}" style="{ButtonStyle}">{label}</a>
            </p>

            <p>If the button doesn't work, copy and paste this link into your browser:</p>

            <p><a href="{url}">{url}</a></p>
            """;
    }

    private async Task SendWithRetryAsync(EmailMessage message, string description)
    {
        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                await _resend.EmailSendAsync(message);
                return;
            }
            catch (ResendException ex) when (attempt < MaxRetries && ex.IsTransient)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                _logger.LogWarning(ex, "Attempt {Attempt}/{Max} failed for {Description} (HTTP {StatusCode}), retrying in {Delay}s",
                    attempt, MaxRetries, description, ex.StatusCode, delay.TotalSeconds);
                await Task.Delay(delay);
            }
        }

        // Final attempt — let it throw so callers know it failed
        await _resend.EmailSendAsync(message);
    }

    private sealed record RateState(int Count, DateTime ExpiresAt);

    private async Task<EmailResult> SendAsync(string to, string subject, string bodyHtml, string plainText, string logDescription)
    {
        var cacheKey = $"emaillimit:{to.ToLowerInvariant()}";

        lock (_rateLimitLock)
        {
            var state = _cache.GetOrCreate(cacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = RateLimitWindow;
                return new RateState(0, DateTime.UtcNow.Add(RateLimitWindow));
            })!;

            if (state.Count >= MaxEmailsPerRecipient)
            {
                _logger.LogWarning("Email rate limit reached for {Recipient}, suppressing {Description}",
                    MaskEmail(to), logDescription);
                return new EmailResult(false, state.ExpiresAt);
            }

            var remaining = state.ExpiresAt - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                remaining = TimeSpan.FromSeconds(1);
            _cache.Set(cacheKey, state with { Count = state.Count + 1 }, remaining);
        }

        var message = new EmailMessage
        {
            From = $"MeducateAPI <{_fromAddress}>",
            To = to,
            Subject = subject,
            HtmlBody = BuildEmailBody(bodyHtml),
            TextBody = BuildPlainTextBody(plainText)
        };

        _logger.LogInformation("Sending {Description}", logDescription);
        await SendWithRetryAsync(message, logDescription);
        _logger.LogInformation("Sent {Description}", logDescription);
        return new EmailResult(true);
    }

    public Task<EmailResult> SendVerificationEmailAsync(string email, string verificationUrl)
    {
        var body = $"""
            <p>Thanks for registering with <strong>MeducateAPI</strong>.</p>

            <p>Please verify your email address by clicking the button below:</p>

            {BuildButtonBlock(verificationUrl, "Verify email address")}

            <p>This link will expire in 24 hours.</p>
            """;

        var plainText = $"""
            Thanks for registering with MeducateAPI.

            Please verify your email address by visiting:
            {verificationUrl}

            This link will expire in 24 hours.
            """;

        return SendAsync(email, "Verify your MeducateAPI account", body, plainText,
            $"verification email to {MaskEmail(email)}");
    }

    public Task<EmailResult> SendLoginEmailAsync(string email, string loginUrl)
    {
        var body = $"""
            <p>We received a request to sign in to your <strong>MeducateAPI</strong> account.</p>

            <p>Click the button below to sign in:</p>

            {BuildButtonBlock(loginUrl, "Sign in to MeducateAPI")}

            <p>This link will expire in 24 hours and can only be used once.</p>
            """;

        var plainText = $"""
            We received a request to sign in to your MeducateAPI account.

            Sign in by visiting:
            {loginUrl}

            This link will expire in 24 hours and can only be used once.
            """;

        return SendAsync(email, "Sign in to MeducateAPI", body, plainText,
            $"login email to {MaskEmail(email)}");
    }

    public Task<EmailResult> SendRateLimitWarningEmailAsync(string email, string keyName, int currentUsage, int dailyLimit)
    {
        var percentUsed = (int)((double)currentUsage / dailyLimit * 100);
        var safeKeyName = WebUtility.HtmlEncode(keyName);

        var body = $"""
            <p>Your API key <strong>{safeKeyName}</strong> has used <strong>{percentUsed}%</strong> of its daily request limit.</p>

            <ul>
                <li>Current usage: {currentUsage} requests</li>
                <li>Daily limit: {dailyLimit} requests</li>
            </ul>

            <p>Once the limit is reached, further requests will be rejected until the limit resets at midnight UTC.</p>

            <p>If you need a higher limit, please contact us.</p>
            """;

        var plainText = $"""
            Your API key "{keyName}" has used {percentUsed}% of its daily request limit.

            Current usage: {currentUsage} requests
            Daily limit: {dailyLimit} requests

            Once the limit is reached, further requests will be rejected until the limit resets at midnight UTC.

            If you need a higher limit, please contact us.
            """;

        return SendAsync(email, $"MeducateAPI: {percentUsed}% of daily limit used", body, plainText,
            $"rate limit warning to {MaskEmail(email)}");
    }

    public Task<EmailResult> SendDataIntegrityAlertAsync(
        string email,
        int failureCount,
        int warningCount,
        int batchChecked,
        int batchIndex,
        int totalBatches,
        IReadOnlyList<string> failureDetails)
    {
        var failureRows = failureDetails.Count > 0
            ? string.Join("", failureDetails.Select(f => $"<li>{WebUtility.HtmlEncode(f)}</li>"))
            : "<li>No details available</li>";

        var failurePlainRows = failureDetails.Count > 0
            ? string.Join("\n", failureDetails.Select(f => $"  - {f}"))
            : "  - No details available";

        var body = $"""
            <p>The nightly data integrity check found <strong>{failureCount} failure(s)</strong> and {warningCount} warning(s) in the topic dataset.</p>

            <ul>
                <li>Batch checked: {batchIndex + 1} of {totalBatches} ({batchChecked} topics)</li>
                <li>Failures: {failureCount}</li>
                <li>Warnings: {warningCount}</li>
            </ul>

            <p><strong>Failures:</strong></p>
            <ul>
                {failureRows}
            </ul>

            <p>Check the Hangfire dashboard for the full job log.</p>
            """;

        var plainText = $"""
            The nightly data integrity check found {failureCount} failure(s) and {warningCount} warning(s).

            Batch checked: {batchIndex + 1} of {totalBatches} ({batchChecked} topics)
            Failures: {failureCount}
            Warnings: {warningCount}

            Failures:
            {failurePlainRows}

            Check the Hangfire dashboard for the full job log.
            """;

        return SendAsync(email, $"MeducateAPI: data integrity check failed ({failureCount} issue(s))", body, plainText,
            $"data integrity alert to {MaskEmail(email)}");
    }

    public async Task<EmailResult> SendBackupAsync(string toEmail, string fileName, byte[] content, string summary)
    {
        var message = new EmailMessage
        {
            From = $"MeducateAPI <{_fromAddress}>",
            To = toEmail,
            Subject = "MeducateAPI weekly data backup",
            HtmlBody = $"<p>{WebUtility.HtmlEncode(summary)}</p><p>The attached file holds accounts, organisations and the waitlist. It contains no API key secrets. Restore with POST /internal/backup/restore.</p>",
            TextBody = summary + "\n\nThe attached file holds accounts, organisations and the waitlist. It contains no API key secrets. Restore with POST /internal/backup/restore.",
            Attachments = [new EmailAttachment { Filename = fileName, Content = content }]
        };

        _logger.LogInformation("Sending weekly data backup ({Bytes} bytes)", content.Length);
        await SendWithRetryAsync(message, "weekly data backup");
        return new EmailResult(true);
    }

    public async Task<EmailResult> SendWaitlistNotificationAsync(string submittedEmail)
    {
        // One signup attempt per email address per 24 hours
        var cacheKey = $"waitlist:{submittedEmail.ToLowerInvariant()}";
        if (_cache.TryGetValue(cacheKey, out _))
            return new EmailResult(false);
        _cache.Set(cacheKey, true, TimeSpan.FromHours(24));

        var alertEmail = _config["Admin:AlertEmail"];
        if (string.IsNullOrWhiteSpace(alertEmail))
        {
            _logger.LogWarning("Waitlist signup from {Email} but Admin:AlertEmail is not configured — signup not forwarded", MaskEmail(submittedEmail));
            return new EmailResult(true); // Don't surface config gaps to users
        }

        var message = new EmailMessage
        {
            From = $"MeducateAPI <{_fromAddress}>",
            To = alertEmail,
            Subject = "New Pro waitlist signup",
            HtmlBody = $"<p>New Pro waitlist signup from <strong>{WebUtility.HtmlEncode(submittedEmail)}</strong>.</p>",
            TextBody = $"New Pro waitlist signup from {submittedEmail}."
        };

        _logger.LogInformation("Sending waitlist notification for {Email}", MaskEmail(submittedEmail));
        await SendWithRetryAsync(message, $"waitlist notification for {MaskEmail(submittedEmail)}");
        _logger.LogInformation("Sent waitlist notification for {Email}", MaskEmail(submittedEmail));
        return new EmailResult(true);
    }

    internal static string FormatRetryAfter(DateTime retryAfter)
    {
        var remaining = retryAfter - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) return "now";

        return remaining.TotalMinutes >= 1
            ? $"{(int)Math.Ceiling(remaining.TotalMinutes)} minute(s)"
            : $"{(int)Math.Ceiling(remaining.TotalSeconds)} second(s)";
    }
}
