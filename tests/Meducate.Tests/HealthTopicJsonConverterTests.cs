using System.Text.Json;
using Meducate.API.Infrastructure;
using Meducate.Domain.Entities;

namespace Meducate.Tests;

public class HealthTopicJsonConverterTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new HealthTopicJsonConverter() }
    };

    private static JsonElement Serialize(HealthTopic topic)
    {
        var json = JsonSerializer.Serialize(topic, Options);
        return JsonDocument.Parse(json).RootElement;
    }

    [Fact]
    public void Disease_UsesDefaultFieldNames()
    {
        var topic = new HealthTopic
        {
            Name = "Diabetes",
            TopicType = "Disease",
            Observations = ["fatigue"],
            Factors = ["genetics"],
            Actions = ["insulin"]
        };

        var root = Serialize(topic);

        Assert.True(root.TryGetProperty("symptoms", out _));
        Assert.True(root.TryGetProperty("causes", out _));
        Assert.True(root.TryGetProperty("treatments", out _));
        Assert.Equal("fatigue", root.GetProperty("symptoms")[0].GetString());
    }

    [Fact]
    public void Drug_UsesContextualFieldNames()
    {
        var topic = new HealthTopic
        {
            Name = "Aspirin",
            TopicType = "Drug",
            Observations = ["nausea"],
            Factors = ["allergy"],
            Actions = ["pain relief"]
        };

        var root = Serialize(topic);

        Assert.True(root.TryGetProperty("sideEffects", out _));
        Assert.True(root.TryGetProperty("contraindications", out _));
        Assert.True(root.TryGetProperty("indications", out _));
    }

    [Fact]
    public void Procedure_UsesContextualFieldNames()
    {
        var topic = new HealthTopic
        {
            Name = "Appendectomy",
            TopicType = "Procedure",
            Observations = ["infection"],
            Factors = ["appendicitis"],
            Actions = ["remove appendix"]
        };

        var root = Serialize(topic);

        Assert.True(root.TryGetProperty("risks", out _));
        Assert.True(root.TryGetProperty("reasons", out _));
        Assert.True(root.TryGetProperty("usedFor", out _));
    }

    [Fact]
    public void NullLists_SerializeAsNull()
    {
        var topic = new HealthTopic { Name = "Test", TopicType = "Disease" };

        var root = Serialize(topic);

        Assert.Equal(JsonValueKind.Null, root.GetProperty("symptoms").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("causes").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("treatments").ValueKind);
    }

    [Fact]
    public void RoundTrip_PreservesData()
    {
        var original = new HealthTopic
        {
            Name = "Flu",
            Summary = "Influenza",
            TopicType = "Disease",
            Category = "Infectious",
            Observations = ["fever", "cough"],
            Factors = ["virus"],
            Actions = ["rest", "fluids"],
            Citations = ["https://example.com"],
            Tags = ["respiratory"],
            Version = 3
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<HealthTopic>(json, Options)!;

        Assert.Equal(original.Name, deserialized.Name);
        Assert.Equal(original.Summary, deserialized.Summary);
        Assert.Equal(original.Category, deserialized.Category);
        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.Citations, deserialized.Citations);
        Assert.Equal(original.Tags, deserialized.Tags);
    }

    [Theory]
    [InlineData("Vaccine", "sideEffects", "contraindications", "prevents")]
    [InlineData("Symptom", "relatedSymptoms", "associatedConditions", "management")]
    [InlineData("Anatomy", "associatedConditions", "riskFactors", "relatedTreatments")]
    [InlineData("Nutrient", "deficiencySymptoms", "dietarySources", "healthBenefits")]
    [InlineData("Mental Health", "symptoms", "causes", "treatments")]
    [InlineData("Lifestyle", "healthRisks", "contributingFactors", "recommendations")]
    [InlineData("Diagnostic Test", "risks", "reasons", "testsFor")]
    public void AllTopicTypes_UseCorrectFieldNames(string topicType, string obsKey, string factorsKey, string actionsKey)
    {
        var topic = new HealthTopic
        {
            Name = "Test",
            TopicType = topicType,
            Observations = ["a"],
            Factors = ["b"],
            Actions = ["c"]
        };

        var root = Serialize(topic);

        Assert.True(root.TryGetProperty(obsKey, out _), $"Missing {obsKey}");
        Assert.True(root.TryGetProperty(factorsKey, out _), $"Missing {factorsKey}");
        Assert.True(root.TryGetProperty(actionsKey, out _), $"Missing {actionsKey}");
    }
}
