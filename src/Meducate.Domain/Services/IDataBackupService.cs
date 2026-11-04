namespace Meducate.Domain.Services;

// A restorable copy of the data that cannot be rebuilt from MedlinePlus: who has an account, their organisation,
// and the waitlist. Health topics are not included (the pipeline regenerates them), and neither are API key
// secrets or hashes, so nothing in the file can be used to call the API. Restoring brings back accounts; keys are re-issued.
internal sealed record BackupDocument(
    DateTime GeneratedAtUtc,
    IReadOnlyList<BackupUser> Users,
    IReadOnlyList<BackupWaitlistEntry> Waitlist);

internal sealed record BackupUser(
    string Email,
    bool IsEmailVerified,
    DateTime? AcceptedTermsAt,
    string? AcceptedTermsVersion,
    string? OrganisationName,
    IReadOnlyList<BackupKey> Keys);

// Reference only: enough to know what to re-issue.
internal sealed record BackupKey(string Name, int DailyLimit, bool IsActive, DateTime CreatedAt);

internal sealed record BackupWaitlistEntry(string Email, DateTime CreatedAt);

internal sealed record RestoreResult(int UsersAdded, int UsersAlreadyPresent, int OrganisationsAdded, int WaitlistAdded);

internal interface IDataBackupService
{
    Task<BackupDocument> ExportAsync(CancellationToken ct);

    // Idempotent: anything already present (matched by email) is left as it is.
    Task<RestoreResult> RestoreAsync(BackupDocument document, CancellationToken ct);
}
