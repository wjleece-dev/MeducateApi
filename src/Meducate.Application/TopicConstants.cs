namespace Meducate.Application;

internal static class TopicConstants
{
    internal const int MinSourceLength = 100;
    internal const string TopicTypeOther = "Other";
    internal const string TopicTypeNonMedical = "Non-Medical";

    // Types whose category is fixed by definition (the classification prompt mandates the same mapping), so they
    // never need a model call and never depend on one being available.
    internal static readonly Dictionary<string, string> CategoryByType = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Drug"] = "Drugs & Medications",
        ["Procedure"] = "Medical Procedures & Interventions",
        ["Diagnostic Test"] = "Diagnostic & Laboratory",
        ["Vaccine"] = "Preventive Care & Screening",
        ["Nutrient"] = "Nutrition & Dietary",
        ["Lifestyle"] = "Health & Wellness",
    };

    // Topic types that map to an ICD-11-codeable clinical entity. Drug/Procedure/etc.
    // use other coding systems (or none) and are never looked up.
    internal static readonly HashSet<string> Icd11CodeableTypes =
        ["Disease", "Disorder", "Syndrome", "Symptom", "Mental Health"];
}
