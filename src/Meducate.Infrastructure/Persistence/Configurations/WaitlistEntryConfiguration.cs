using Meducate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meducate.Infrastructure.Persistence.Configurations;

internal sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.HasIndex(w => w.Email).IsUnique();
        builder.HasIndex(w => w.CreatedAt);
        builder.Property(w => w.Email).HasMaxLength(320).IsRequired();
    }
}
