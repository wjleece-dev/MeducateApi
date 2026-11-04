using Meducate.Domain.Services;

namespace Meducate.IntegrationTests;

public sealed class FakeEmailService : IEmailService
{
    public string? LastVerificationUrl { get; private set; }

    Task<EmailResult> IEmailService.SendVerificationEmailAsync(string email, string verificationUrl)
    {
        LastVerificationUrl = verificationUrl;
        return Task.FromResult(new EmailResult(true));
    }

    Task<EmailResult> IEmailService.SendLoginEmailAsync(string email, string loginUrl)
    {
        LastVerificationUrl = loginUrl;
        return Task.FromResult(new EmailResult(true));
    }

    Task<EmailResult> IEmailService.SendRateLimitWarningEmailAsync(string email, string keyName, int currentUsage, int dailyLimit) =>
        Task.FromResult(new EmailResult(true));

    Task<EmailResult> IEmailService.SendDataIntegrityAlertAsync(string email, int failureCount, int warningCount, int batchChecked, int batchIndex, int totalBatches, IReadOnlyList<string> failureDetails) =>
        Task.FromResult(new EmailResult(true));

    Task<EmailResult> IEmailService.SendWaitlistNotificationAsync(string submittedEmail) =>
        Task.FromResult(new EmailResult(true));

    public byte[]? LastBackup { get; private set; }

    Task<EmailResult> IEmailService.SendBackupAsync(string toEmail, string fileName, byte[] content, string summary)
    {
        LastBackup = content;
        return Task.FromResult(new EmailResult(true));
    }
}
