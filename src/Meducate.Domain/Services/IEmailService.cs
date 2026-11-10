namespace Meducate.Domain.Services;

internal sealed record EmailResult(bool Sent, DateTime? RetryAfter = null);

internal interface IEmailService
{
    Task<EmailResult> SendVerificationEmailAsync(string email, string verificationUrl);
    Task<EmailResult> SendLoginEmailAsync(string email, string loginUrl);
    Task<EmailResult> SendRateLimitWarningEmailAsync(string email, string keyName, int currentUsage, int dailyLimit);
    Task<EmailResult> SendDataIntegrityAlertAsync(string email, int failureCount, int warningCount, int batchChecked, int batchIndex, int totalBatches, IReadOnlyList<string> failureDetails);
    Task<EmailResult> SendWaitlistNotificationAsync(string submittedEmail);
    Task<EmailResult> SendBackupAsync(string toEmail, string fileName, byte[] content, string summary);
}
