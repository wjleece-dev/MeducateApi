using Meducate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meducate.Infrastructure.Persistence.Configurations;

internal sealed class LlmUsageConfiguration : IEntityTypeConfiguration<LlmUsage>
{
    public void Configure(EntityTypeBuilder<LlmUsage> builder)
    {
        builder.HasIndex(u => u.At);
        builder.Property(u => u.Model).HasMaxLength(100).IsRequired();
        builder.Property(u => u.CostUsd).HasPrecision(12, 6);
    }
}
