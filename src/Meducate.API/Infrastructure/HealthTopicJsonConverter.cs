using System.Text.Json;
using System.Text.Json.Serialization;
using Meducate.Domain.Entities;

namespace Meducate.API.Infrastructure;

internal sealed class HealthTopicJsonConverter : JsonConverter<HealthTopic>
{
    private static (string ObservationsKey, string FactorsKey, string ActionsKey) GetFieldNames(string? topicType)
    {
        return topicType switch
        {
            "Drug" => ("sideEffects", "contraindications", "indications"),
            "Procedure" => ("risks", "reasons", "usedFor"),
            "Diagnostic Test" => ("risks", "reasons", "testsFor"),
            "Vaccine" => ("sideEffects", "contraindications", "prevents"),
            "Symptom" => ("relatedSymptoms", "associatedConditions", "management"),
            "Anatomy" => ("associatedConditions", "riskFactors", "relatedTreatments"),
            "Nutrient" => ("deficiencySymptoms", "dietarySources", "healthBenefits"),
            "Mental Health" => ("symptoms", "causes", "treatments"),
            "Lifestyle" => ("healthRisks", "contributingFactors", "recommendations"),
            _ => ("symptoms", "causes", "treatments")
        };
    }

    public override HealthTopic? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        return new HealthTopic
        {
            Id = root.TryGetProperty("id", out var id) ? id.GetGuid() : Guid.NewGuid(),
            Name = root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            Summary = root.TryGetProperty("summary", out var summary) ? summary.GetString() : null,
            Observations = ReadStringList(root, "observations") ?? ReadStringList(root, "symptoms"),
            Factors = ReadStringList(root, "factors") ?? ReadStringList(root, "causes"),
            Actions = ReadStringList(root, "actions") ?? ReadStringList(root, "treatments"),
            Citations = ReadStringList(root, "citations"),
            Category = root.TryGetProperty("category", out var cat) ? cat.GetString() : null,
            TopicType = root.TryGetProperty("topicType", out var tt) ? tt.GetString() : null,
            Icd11Code = root.TryGetProperty("icd11Code", out var icdCode) ? icdCode.GetString() : null,
            Icd11Title = root.TryGetProperty("icd11Title", out var icdTitle) ? icdTitle.GetString() : null,
            Tags = ReadStringList(root, "tags"),
            Version = root.TryGetProperty("version", out var ver) ? ver.GetInt32() : 1,
            LastUpdated = root.TryGetProperty("lastUpdated", out var lu) ? lu.GetDateTime() : DateTime.UtcNow,
        };
    }

    public override void Write(Utf8JsonWriter writer, HealthTopic value, JsonSerializerOptions options)
    {
        var (observationsKey, factorsKey, actionsKey) = GetFieldNames(value.TopicType);

        writer.WriteStartObject();

        writer.WriteString("id", value.Id);
        writer.WriteString("name", value.Name);

        WriteNullableString(writer, "summary", value.Summary);
        WriteNullableString(writer, "topicType", value.TopicType);
        WriteNullableString(writer, "category", value.Category);
        WriteNullableString(writer, "icd11Code", value.Icd11Code);
        WriteNullableString(writer, "icd11Title", value.Icd11Title);

        WriteStringList(writer, observationsKey, value.Observations);
        WriteStringList(writer, factorsKey, value.Factors);
        WriteStringList(writer, actionsKey, value.Actions);
        WriteStringList(writer, "citations", value.Citations);
        WriteStringList(writer, "tags", value.Tags);

        writer.WriteString("lastUpdated", value.LastUpdated);
        writer.WriteNumber("version", value.Version);

        writer.WriteEndObject();
    }

    private static void WriteNullableString(Utf8JsonWriter writer, string propertyName, string? value)
    {
        if (value is not null)
            writer.WriteString(propertyName, value);
        else
            writer.WriteNull(propertyName);
    }

    private static void WriteStringList(Utf8JsonWriter writer, string propertyName, List<string>? items)
    {
        if (items is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteStartArray(propertyName);
        foreach (var item in items)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }

    private static List<string>? ReadStringList(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Array)
            return null;

        return element.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .ToList();
    }
}
