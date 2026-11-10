using System.IO.Compression;
using System.Text.Json;
using Hangfire;
using Hangfire.Console;
using Hangfire.Server;
using Meducate.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Meducate.Application.Jobs;

// Weekly: emails a gzipped JSON copy of the accounts and waitlist to Admin:AlertEmail. Railway's own backups
// need the Pro plan, and these tables cannot be regenerated like the health topics can.
internal sealed class BackupJob(
    IDataBackupService backup,
    IEmailService email,
    IConfiguration config,
    ILogger<BackupJob> logger)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    public async Task ExecuteAsync(IJobCancellationToken jobCancellationToken, PerformContext? context = null)
    {
        var ct = jobCancellationToken.ShutdownToken;
        var to = config["Admin:AlertEmail"];
        if (string.IsNullOrWhiteSpace(to))
        {
            logger.LogWarning("Backup skipped: Admin:AlertEmail is not configured");
            return;
        }

        var document = await backup.ExportAsync(ct);
        var bytes = Compress(JsonSerializer.SerializeToUtf8Bytes(document, Json));
        var summary = $"{document.Users.Count} accounts, {document.Users.Count(u => u.OrganisationName is not null)} organisations, {document.Waitlist.Count} waitlist entries, generated {document.GeneratedAtUtc:u}.";

        await email.SendBackupAsync(to, $"meducate-backup-{document.GeneratedAtUtc:yyyy-MM-dd}.json.gz", bytes, summary);
        logger.LogInformation("Backup emailed: {Summary}", summary);
        context?.WriteLine(summary);
    }

    internal static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            gzip.Write(data);
        return output.ToArray();
    }
}
