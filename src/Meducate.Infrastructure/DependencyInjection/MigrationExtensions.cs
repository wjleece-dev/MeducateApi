using Meducate.Domain.Entities;
using Meducate.Infrastructure.ApiKeys;
using Meducate.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Meducate.Infrastructure.DependencyInjection;

public static class MigrationExtensions
{
    public static async Task ApplyMigrationsAsync(this Microsoft.AspNetCore.Builder.WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MeducateDbContext>();
        await db.Database.MigrateAsync();
        await SeedDemoKeyAsync(db);
    }

    private static async Task SeedDemoKeyAsync(MeducateDbContext db)
    {
        const string demoKeyId = "d3m0000000000000000000000000key1";
        const string demoSecret = "MEDUCATE_PUBLIC_DEMO_2026";
        const int demoDailyLimit = 300;

        var existing = await db.ApiClients.FirstOrDefaultAsync(c => c.KeyId == demoKeyId);

        if (existing is not null)
        {
            existing.IsActive = true;
            existing.DailyLimit = demoDailyLimit;
            await db.SaveChangesAsync();
            return;
        }

        var user = new User
        {
            Email = "demo@meducateapi.com",
            IsEmailVerified = true
        };

        var org = new Organisation
        {
            Name = "Demo",
            User = user
        };

        var (hashed, salt) = ApiKeyHasher.HashSecret(demoSecret);

        var client = new ApiClient
        {
            Organisation = org,
            KeyId = demoKeyId,
            Name = "Public Demo Key",
            DailyLimit = demoDailyLimit,
            IsActive = true,
            HashedSecret = hashed,
            Salt = salt
        };

        db.Users.Add(user);
        db.Organisations.Add(org);
        db.ApiClients.Add(client);
        await db.SaveChangesAsync();
    }
}
