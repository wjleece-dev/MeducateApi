using Meducate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meducate.Infrastructure.Persistence.Configurations;

internal sealed class SeenTopicConfiguration : IEntityTypeConfiguration<SeenTopic>
{
    public void Configure(EntityTypeBuilder<SeenTopic> builder)
    {
        builder.HasKey(t => t.Name);
        builder.Property(t => t.Name).HasMaxLength(200);
        builder.Property(t => t.Status).IsRequired().HasMaxLength(50);
        builder.Property(t => t.TopicType).HasMaxLength(50);
    }
}
