using Meducate.Domain.Entities;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Meducate.API.Infrastructure;

// HealthTopicJsonConverter names the three lists by topic type, but Swashbuckle reflects over the
// C# type and would publish Observations/Factors/Actions, which never appear on the wire. This
// swaps in the default (disease) names and describes the per-type variation. Keep the description
// text in step with HealthTopicJsonConverter.GetFieldNames.
internal sealed class HealthTopicSchemaFilter : ISchemaFilter
{
    private const string TypeRule = "The JSON name of this list depends on topicType; see https://meducateapi.com/health-topics-api. ";

    private static readonly Dictionary<string, (string Name, string Description)> Renames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["observations"] = ("symptoms", TypeRule +
            "Disease, Disorder, Syndrome, Mental Health: symptoms. Symptom: relatedSymptoms. Drug and Vaccine: sideEffects. " +
            "Procedure and Diagnostic Test: risks. Anatomy: associatedConditions. Nutrient: deficiencySymptoms."),
        ["factors"] = ("causes", TypeRule +
            "Disease, Disorder, Syndrome, Mental Health: causes. Symptom: associatedConditions. Drug and Vaccine: contraindications. " +
            "Procedure and Diagnostic Test: reasons. Anatomy: riskFactors. Nutrient: dietarySources."),
        ["actions"] = ("treatments", TypeRule +
            "Disease, Disorder, Syndrome, Mental Health: treatments. Symptom: management. Drug: indications. Procedure: usedFor. " +
            "Diagnostic Test: testsFor. Vaccine: prevents. Anatomy: relatedTreatments. Nutrient: healthBenefits."),
    };

    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type != typeof(HealthTopic) || schema is not OpenApiSchema topic || topic.Properties is null)
            return;

        // Rebuild rather than mutate so the properties keep their original order.
        var rewritten = new Dictionary<string, IOpenApiSchema>();
        foreach (var (key, value) in topic.Properties)
        {
            if (!Renames.TryGetValue(key, out var rename))
            {
                rewritten[key] = value;
                continue;
            }

            rewritten[rename.Name] = new OpenApiSchema
            {
                Type = value.Type,
                Items = value.Items,
                Description = rename.Description,
            };
        }

        topic.Properties = rewritten;
        topic.Description = "A health topic. The three list fields are named by topic type; see their descriptions.";
    }
}
