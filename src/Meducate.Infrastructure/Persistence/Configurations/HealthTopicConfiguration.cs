using Meducate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meducate.Infrastructure.Persistence.Configurations;

internal sealed class HealthTopicConfiguration : IEntityTypeConfiguration<HealthTopic>
{
    public void Configure(EntityTypeBuilder<HealthTopic> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.OriginalName).HasMaxLength(200);
        builder.Property(t => t.Category).HasMaxLength(100);
        builder.Property(t => t.TopicType).HasMaxLength(50);
        builder.Property(t => t.Icd11Code).HasMaxLength(20);
        builder.Property(t => t.Icd11Title).HasMaxLength(300);
        builder.Property(t => t.Observations).HasColumnType("jsonb");
        builder.Property(t => t.Factors).HasColumnType("jsonb");
        builder.Property(t => t.Actions).HasColumnType("jsonb");
        builder.Property(t => t.Citations).HasColumnType("jsonb");
        builder.Property(t => t.Tags).HasColumnType("jsonb");

        builder.HasIndex(t => t.Name).IsUnique();
        builder.HasIndex(t => t.OriginalName);
        builder.HasIndex(t => t.TopicType);
    }
}
