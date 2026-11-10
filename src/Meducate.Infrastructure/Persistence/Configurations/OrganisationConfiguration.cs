using Meducate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meducate.Infrastructure.Persistence.Configurations;

internal sealed class OrganisationConfiguration : IEntityTypeConfiguration<Organisation>
{
    public void Configure(EntityTypeBuilder<Organisation> builder)
    {
        builder.HasMany(o => o.ApiClients)
            .WithOne(c => c.Organisation)
            .HasForeignKey(c => c.OrganisationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
