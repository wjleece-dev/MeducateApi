using Meducate.Domain.Entities;
using Meducate.Domain.Services;
using Meducate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Meducate.Infrastructure.Backup;

internal sealed class DataBackupService(MeducateDbContext db) : IDataBackupService
{
    public async Task<BackupDocument> ExportAsync(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new
            {
                u.Email,
                u.IsEmailVerified,
                u.AcceptedTermsAt,
                u.AcceptedTermsVersion,
                Organisation = db.Organisations.Where(o => o.UserId == u.Id)
                    .Select(o => new { o.Name, Keys = o.ApiClients.Select(k => new BackupKey(k.Name, k.DailyLimit, k.IsActive, k.CreatedAt)).ToList() })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var waitlist = await db.WaitlistEntries.AsNoTracking()
            .OrderBy(w => w.CreatedAt)
            .Select(w => new BackupWaitlistEntry(w.Email, w.CreatedAt))
            .ToListAsync(ct);

        return new BackupDocument(
            DateTime.UtcNow,
            [.. users.Select(u => new BackupUser(u.Email, u.IsEmailVerified, u.AcceptedTermsAt, u.AcceptedTermsVersion,
                u.Organisation?.Name, u.Organisation?.Keys ?? []))],
            waitlist);
    }

    public async Task<RestoreResult> RestoreAsync(BackupDocument document, CancellationToken ct)
    {
        var existingEmails = new HashSet<string>(await db.Users.Select(u => u.Email).ToListAsync(ct), StringComparer.OrdinalIgnoreCase);
        var existingWaitlist = new HashSet<string>(await db.WaitlistEntries.Select(w => w.Email).ToListAsync(ct), StringComparer.OrdinalIgnoreCase);

        int usersAdded = 0, usersPresent = 0, orgsAdded = 0, waitlistAdded = 0;

        foreach (var backup in document.Users)
        {
            if (!existingEmails.Add(backup.Email))
            {
                usersPresent++;
                continue;
            }

            var user = new User
            {
                Email = backup.Email,
                IsEmailVerified = backup.IsEmailVerified,
                AcceptedTermsAt = backup.AcceptedTermsAt,
                AcceptedTermsVersion = backup.AcceptedTermsVersion,
            };

            // An unverified account needs a token to verify with; a verified one signs in by magic link.
            if (!backup.IsEmailVerified)
                user.RotateVerificationToken();

            db.Users.Add(user);
            usersAdded++;

            if (backup.OrganisationName is not null)
            {
                db.Organisations.Add(new Organisation { Name = backup.OrganisationName, User = user });
                orgsAdded++;
            }
        }

        foreach (var entry in document.Waitlist)
        {
            if (!existingWaitlist.Add(entry.Email))
                continue;

            db.WaitlistEntries.Add(new WaitlistEntry { Email = entry.Email, CreatedAt = entry.CreatedAt });
            waitlistAdded++;
        }

        await db.SaveChangesAsync(ct);
        return new RestoreResult(usersAdded, usersPresent, orgsAdded, waitlistAdded);
    }
}
