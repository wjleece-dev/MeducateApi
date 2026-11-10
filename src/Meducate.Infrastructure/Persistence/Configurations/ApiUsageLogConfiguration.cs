using Meducate.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meducate.Infrastructure.Persistence.Configurations;

internal sealed class ApiUsageLogConfiguration : IEntityTypeConfiguration<ApiUsageLog>
{
    public void Configure(EntityTypeBuilder<ApiUsageLog> builder)
    {
        builder.HasIndex(u => new { u.ClientId, u.Timestamp });
    }
}
