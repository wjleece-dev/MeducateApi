using Meducate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meducate.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.VerificationToken);
        builder.Property(u => u.Email).HasMaxLength(320).IsRequired();
        builder.Property(u => u.AcceptedTermsVersion).HasMaxLength(20);
    }
}
