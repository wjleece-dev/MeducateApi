using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Meducate.Infrastructure.Persistence.Configurations;

internal static class JsonListConversionExtensions
{
    // Stores a list of records as one jsonb document, compared by its JSON so in-place edits are noticed.
    internal static PropertyBuilder<List<T>?> HasJsonListConversion<T>(this PropertyBuilder<List<T>?> property)
    {
        property.HasConversion(
            list => list == null ? null : JsonSerializer.Serialize(list, (JsonSerializerOptions?)null),
            json => json == null ? null : JsonSerializer.Deserialize<List<T>>(json, (JsonSerializerOptions?)null));

        property.Metadata.SetValueComparer(new ValueComparer<List<T>?>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => v == null ? null : JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(v, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)));

        return property;
    }
}
